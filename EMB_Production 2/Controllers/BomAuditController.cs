using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    // ============================================================
    // BOM vs Actual audit
    // ------------------------------------------------------------
    // For every posted Work Order, compares what the Design's BOM
    // said should be consumed vs what the floor actually issued out
    // of store. Classifies each (WorkOrder, Item) row as:
    //
    //   WRONG_ITEM        BOM=X but Issued=0  (BOM lists something
    //                                          production never uses)
    //   WRONG_QTY         BOM=X, Issued=Y, |X-Y|/X > tolerance
    //                                          (BOM over/understates)
    //   MISSING_FROM_BOM  BOM=0 but Issued=Y  (BOM doesn't list an
    //                                          item the floor needs)
    //   OK                within tolerance
    //
    // Source of truth: Gen_BOM × Gen_ComponentMaterials × Gen_RawItems.
    // Gen_ThreadUsage was previously a parallel BOM path for threads,
    // but its Usage values diverged from reality (designer's data
    // showed Cotton Sewing Thread 40s at 35/unit in Gen_ThreadUsage
    // vs 10/unit in Gen_ComponentMaterials — matching what was
    // actually issued). We now read materials only; the JOIN to
    // Gen_Threads is just for display (code/name/color/cost) via
    // RawItemId.
    //
    // Tolerance is configurable per request (default 20%).
    // ============================================================
    [RoutePrefix("emb-products/bom-audit")]
    public class BomAuditController : BaseController
    {
        // Per (Design, RawItem) drift row used by both the summary
        // and the detail endpoint.
        private class AuditRow
        {
            public int     WoId { get; set; }
            public string  WorkOrderCode { get; set; }
            public int     DesignId { get; set; }
            public string  DesignCode { get; set; }
            public string  DesignName { get; set; }
            public int     RawItemId { get; set; }
            public string  RawItemName { get; set; }
            public string  ThreadCode { get; set; }
            public string  ThreadName { get; set; }
            public decimal ExpectedQty { get; set; }
            public decimal IssuedQty { get; set; }
            public DateTime OrderDate { get; set; }
        }

        // The FULL OUTER JOIN happens server-side. EF6 doesn't translate
        // that well, so we run raw SQL and re-use AuditRow shape.
        private List<AuditRow> RunAuditSql(string fromDate, string toDate,
                                           int? designId, int? branchId)
        {
            DateTime? fromDt = null, toDt = null;
            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var f)) fromDt = f.Date;
            if (!string.IsNullOrWhiteSpace(toDate)   && DateTime.TryParse(toDate, out var t))   toDt   = t.Date.AddDays(1).AddTicks(-1);

            var ps = new List<SqlParameter>();
            string woFilter = " WHERE wo.IsPosted = 1 AND (wo.Deleted = 0 OR wo.Deleted IS NULL) AND wo.OrderQty > 0 ";
            if (fromDt.HasValue) { woFilter += " AND wo.OrderDate >= @from "; ps.Add(new SqlParameter("@from", fromDt.Value)); }
            if (toDt.HasValue)   { woFilter += " AND wo.OrderDate <= @to ";   ps.Add(new SqlParameter("@to",   toDt.Value)); }
            if (designId.HasValue && designId.Value > 0) { woFilter += " AND wo.DesignId = @did "; ps.Add(new SqlParameter("@did", designId.Value)); }
            if (branchId.HasValue && branchId.Value > 0) { woFilter += " AND wo.BranchId = @brid "; ps.Add(new SqlParameter("@brid", branchId.Value)); }

            string sql = @"
;WITH wo_scope AS (
    SELECT wo.Id AS WoId, wo.WorkOrderCode, wo.DesignId,
           d.D_Code AS DesignCode, d.CollectionName AS DesignName,
           wo.OrderQty, wo.OrderDate
    FROM   dbo.WorkOrders wo
    JOIN   dbo.Gen_Design d ON d.Id = wo.DesignId
    " + woFilter + @"
),
bom_expected AS (
    -- Single source of truth: Gen_ComponentMaterials. QtyPerUnit ×
    -- OrderQty, rolled up per (WO, RawItemId). Thread code/name/
    -- color come from a LEFT JOIN to Gen_Threads on RawItemId — kept
    -- for display only, not part of the qty math.
    SELECT  s.WoId, s.WorkOrderCode, s.DesignId, s.DesignCode, s.DesignName,
            s.OrderDate, cm.RawItemId,
            SUM(cm.QtyPerUnit * s.OrderQty) AS ExpectedQty,
            MAX(t.Code) AS ThreadCode,
            MAX(t.Name) AS ThreadName
    FROM    wo_scope s
    JOIN    dbo.Gen_BOM bom               ON bom.DesignId = s.DesignId
    JOIN    dbo.Gen_ComponentMaterials cm ON cm.CompId    = bom.CompId
    LEFT JOIN dbo.Gen_Threads t           ON t.RawItemId  = cm.RawItemId
                                          AND t.Deleted   != 1
    WHERE   (bom.Deleted = 0 OR bom.Deleted IS NULL)
       AND  (cm.Deleted  = 0 OR cm.Deleted  IS NULL)
       AND  (cm.Active   = 1 OR cm.Active   IS NULL)
    GROUP BY s.WoId, s.WorkOrderCode, s.DesignId, s.DesignCode, s.DesignName,
             s.OrderDate, cm.RawItemId
),
issued_actual AS (
    -- One row per (WO, RawItem) of what the floor actually issued.
    -- ReferenceId on MATERIAL_ISSUE rows is the WO id.
    SELECT  st.ReferenceId AS WoId, st.ItemId AS RawItemId,
            SUM(ISNULL(st.OutQty, 0)) AS IssuedQty
    FROM    dbo.Gen_Store st
    WHERE   st.ReferenceType = 'MATERIAL_ISSUE'
       AND  (st.Deleted = 0 OR st.Deleted IS NULL)
       AND  st.OutQty > 0
       AND  st.ReferenceId IN (SELECT WoId FROM wo_scope)
    GROUP BY st.ReferenceId, st.ItemId
)
SELECT
    ISNULL(b.WoId,         i_wo.WoId)         AS WoId,
    ISNULL(b.WorkOrderCode, i_wo.WorkOrderCode) AS WorkOrderCode,
    ISNULL(b.DesignId,     i_wo.DesignId)     AS DesignId,
    ISNULL(b.DesignCode,   i_wo.DesignCode)   AS DesignCode,
    ISNULL(b.DesignName,   i_wo.DesignName)   AS DesignName,
    ISNULL(b.RawItemId,    ia.RawItemId)      AS RawItemId,
    r.ItemName                                AS RawItemName,
    b.ThreadCode                              AS ThreadCode,
    b.ThreadName                              AS ThreadName,
    ISNULL(b.ExpectedQty, 0)                  AS ExpectedQty,
    ISNULL(ia.IssuedQty, 0)                   AS IssuedQty,
    ISNULL(b.OrderDate,    i_wo.OrderDate)    AS OrderDate
FROM bom_expected b
FULL OUTER JOIN issued_actual ia
              ON ia.WoId = b.WoId AND ia.RawItemId = b.RawItemId
-- Pull WO header back for rows that came in via issued_actual only
LEFT JOIN wo_scope i_wo
              ON i_wo.WoId = ISNULL(b.WoId, ia.WoId)
LEFT JOIN dbo.Gen_RawItems r
              ON r.Id = ISNULL(b.RawItemId, ia.RawItemId)
WHERE ISNULL(b.WoId, ia.WoId) IS NOT NULL
ORDER BY ISNULL(b.DesignCode, i_wo.DesignCode),
         ISNULL(b.WorkOrderCode, i_wo.WorkOrderCode);
";

            return db.Database.SqlQuery<AuditRow>(sql, ps.ToArray()).ToList();
        }

        // Returns a classified drift label given expected vs issued + tolerance.
        // Keep this in C# (not SQL) so the tolerance threshold is testable.
        private static string Classify(decimal expected, decimal issued, decimal tolerancePct)
        {
            if (expected == 0m && issued == 0m) return "OK";              // shouldn't surface, but defensive
            if (expected == 0m) return "MISSING_FROM_BOM";                // floor needs item BOM doesn't list
            if (issued   == 0m) return "WRONG_ITEM";                      // BOM lists item floor doesn't use
            decimal driftPct = Math.Abs(expected - issued) / expected;
            return driftPct > tolerancePct ? "WRONG_QTY" : "OK";
        }

        // ============================================================
        // SUMMARY — one row per design, with drift counts
        // ============================================================
        [HttpGet, Route("summary")]
        public ActionResult Summary(
            string fromDate = null, string toDate = null,
            int? designId = null, int? branchId = null,
            decimal tolerancePct = 0.20m)
        {
            try
            {
                if (tolerancePct < 0m) tolerancePct = 0m;
                if (tolerancePct > 1m) tolerancePct = 1m;

                var diagnostics = ComputeDiagnostics(fromDate, toDate, designId, branchId);
                var rows = RunAuditSql(fromDate, toDate, designId, branchId);

                var groups = rows
                    .GroupBy(r => new { r.DesignId, r.DesignCode, r.DesignName })
                    .Select(g =>
                    {
                        var classified = g.Select(r => new
                        {
                            r.WoId, r.WorkOrderCode, r.RawItemId, r.RawItemName,
                            r.ThreadCode, r.ThreadName, r.ExpectedQty, r.IssuedQty,
                            kind = Classify(r.ExpectedQty, r.IssuedQty, tolerancePct)
                        }).ToList();

                        return new
                        {
                            designId       = g.Key.DesignId,
                            designCode     = g.Key.DesignCode,
                            designName     = g.Key.DesignName ?? "",
                            workOrderCount = classified.Select(x => x.WoId).Distinct().Count(),
                            itemRowCount   = classified.Count,
                            okCount        = classified.Count(x => x.kind == "OK"),
                            wrongItemCount = classified.Count(x => x.kind == "WRONG_ITEM"),
                            wrongQtyCount  = classified.Count(x => x.kind == "WRONG_QTY"),
                            missingCount   = classified.Count(x => x.kind == "MISSING_FROM_BOM"),
                            totalExpected  = Math.Round(classified.Sum(x => x.ExpectedQty), 2),
                            totalIssued    = Math.Round(classified.Sum(x => x.IssuedQty), 2)
                        };
                    })
                    // Worst designs first — most drift rows on top
                    .OrderByDescending(g => g.wrongItemCount + g.wrongQtyCount + g.missingCount)
                    .ThenBy(g => g.designCode)
                    .ToList();

                // Top-of-page KPIs across the whole filtered set
                int totalRows  = rows.Count;
                int designsAudited = groups.Count;
                int wrongItem  = groups.Sum(g => g.wrongItemCount);
                int wrongQty   = groups.Sum(g => g.wrongQtyCount);
                int missing    = groups.Sum(g => g.missingCount);
                int okRows     = groups.Sum(g => g.okCount);

                return JsonSuccess("OK", new
                {
                    kpis = new
                    {
                        designsAudited,
                        totalRows,
                        okRows,
                        wrongItem,
                        wrongQty,
                        missing,
                        driftRate = totalRows == 0 ? 0m
                                  : Math.Round((decimal)(wrongItem + wrongQty + missing) / totalRows * 100m, 1)
                    },
                    appliedTolerancePct = tolerancePct,
                    diagnostics,
                    items = groups
                });
            }
            catch (Exception ex) { return JsonError("Failed to compute BOM audit.", ex); }
        }

        // ============================================================
        // DIAGNOSTICS — count each pipeline stage so we can show the
        // user exactly where data is dropping off. Empty audit can mean:
        //   (a) no WOs match the date / branch filter
        //   (b) WOs exist but those designs have no BOM data
        //   (c) BOM exists but no MATERIAL_ISSUE rows tied to those WOs
        //
        // Implemented as raw SQL so EF6 LINQ translation failures can't
        // tank the whole audit response. Any per-count failure falls
        // back to -1 (rendered as "?") rather than throwing.
        // ============================================================
        private object ComputeDiagnostics(string fromDate, string toDate,
                                          int? designId, int? branchId)
        {
            DateTime? fromDt = null, toDt = null;
            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var f)) fromDt = f.Date;
            if (!string.IsNullOrWhiteSpace(toDate)   && DateTime.TryParse(toDate, out var t))   toDt   = t.Date.AddDays(1).AddTicks(-1);

            // Build the same WO scope WHERE clause used by RunAuditSql.
            string woFilter = " WHERE wo.IsPosted = 1 AND (wo.Deleted = 0 OR wo.Deleted IS NULL) AND wo.OrderQty > 0 ";
            var ps = new List<SqlParameter>();
            if (fromDt.HasValue) { woFilter += " AND wo.OrderDate >= @from "; ps.Add(new SqlParameter("@from", fromDt.Value)); }
            if (toDt.HasValue)   { woFilter += " AND wo.OrderDate <= @to ";   ps.Add(new SqlParameter("@to",   toDt.Value)); }
            if (designId.HasValue && designId.Value > 0) { woFilter += " AND wo.DesignId = @did "; ps.Add(new SqlParameter("@did", designId.Value)); }
            if (branchId.HasValue && branchId.Value > 0) { woFilter += " AND wo.BranchId = @brid "; ps.Add(new SqlParameter("@brid", branchId.Value)); }

            int workOrdersInScope    = TryScalar(@"SELECT COUNT(1) FROM dbo.WorkOrders wo " + woFilter, ps);
            int distinctDesigns      = TryScalar(@"SELECT COUNT(DISTINCT wo.DesignId) FROM dbo.WorkOrders wo " + woFilter + " AND wo.DesignId IS NOT NULL ", ps);

            int bomRowCount          = TryScalar(@"
                SELECT COUNT(1)
                FROM   dbo.Gen_ComponentMaterials cm
                JOIN   dbo.Gen_BOM bom ON bom.CompId = cm.CompId
                WHERE  (bom.Deleted = 0 OR bom.Deleted IS NULL)
                  AND  (cm.Deleted  = 0 OR cm.Deleted  IS NULL)
                  AND  (cm.Active   = 1 OR cm.Active   IS NULL)
                  AND  bom.DesignId IN (
                         SELECT DISTINCT wo.DesignId FROM dbo.WorkOrders wo "
                         + woFilter + " AND wo.DesignId IS NOT NULL )", ps);

            int issueRowCount        = TryScalar(@"
                SELECT COUNT(1)
                FROM   dbo.Gen_Store st
                WHERE  st.ReferenceType = 'MATERIAL_ISSUE'
                  AND  (st.Deleted = 0 OR st.Deleted IS NULL)
                  AND  st.OutQty > 0
                  AND  st.ReferenceId IN (
                         SELECT wo.Id FROM dbo.WorkOrders wo " + woFilter + " )", ps);

            return new
            {
                workOrdersInScope,
                distinctDesignsInScope = distinctDesigns,
                bomComponentMaterialRows = bomRowCount,
                materialIssueRows = issueRowCount
            };
        }

        // Runs a scalar COUNT query. Returns -1 if SQL fails so a broken
        // diagnostic count doesn't take down the whole audit endpoint —
        // the UI renders -1 as "?" to signal that this stage couldn't
        // be measured.
        private int TryScalar(string sql, List<SqlParameter> ps)
        {
            try
            {
                // SqlParameter instances can't be reused across multiple
                // commands, so clone each one before passing.
                var clones = ps.Select(p => new SqlParameter(p.ParameterName, p.Value)).ToArray();
                return db.Database.SqlQuery<int>(sql, clones).FirstOrDefault();
            }
            catch
            {
                return -1;
            }
        }

        // ============================================================
        // DETAIL — every (WO, Item) row for one design
        // ============================================================
        [HttpGet, Route("detail/{designId:int}")]
        public ActionResult Detail(int designId,
            string fromDate = null, string toDate = null,
            int? branchId = null, decimal tolerancePct = 0.20m)
        {
            try
            {
                if (tolerancePct < 0m) tolerancePct = 0m;
                if (tolerancePct > 1m) tolerancePct = 1m;

                var rows = RunAuditSql(fromDate, toDate, designId, branchId);

                var items = rows.Select(r => new
                {
                    workOrderId   = r.WoId,
                    workOrderCode = r.WorkOrderCode,
                    designCode    = r.DesignCode,
                    designName    = r.DesignName ?? "",
                    rawItemId     = r.RawItemId,
                    rawItemName   = r.RawItemName ?? "—",
                    threadCode    = r.ThreadCode,
                    threadName    = r.ThreadName,
                    expectedQty   = Math.Round(r.ExpectedQty, 2),
                    issuedQty     = Math.Round(r.IssuedQty, 2),
                    driftQty      = Math.Round(r.IssuedQty - r.ExpectedQty, 2),
                    driftPct      = r.ExpectedQty == 0
                                       ? (decimal?)null
                                       : Math.Round((r.IssuedQty - r.ExpectedQty) / r.ExpectedQty * 100m, 1),
                    kind          = Classify(r.ExpectedQty, r.IssuedQty, tolerancePct),
                    orderDate     = r.OrderDate.ToString("yyyy-MM-dd")
                })
                .OrderBy(x => x.kind == "OK" ? 1 : 0)   // drift rows first
                .ThenBy(x => x.workOrderCode)
                .ThenBy(x => x.rawItemName)
                .ToList();

                return JsonSuccess("OK", new { items });
            }
            catch (Exception ex) { return JsonError("Failed to load BOM audit detail.", ex); }
        }
    }
}
