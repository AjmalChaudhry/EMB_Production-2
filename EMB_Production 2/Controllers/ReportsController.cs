using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;

namespace EMB_Production.Controllers
{
    // ============================================================
    // Reports
    // ------------------------------------------------------------
    // The Essential 4 reports for screen-only viewing with on-demand
    // Excel/PDF exports. Each report is an aggregation over existing
    // data (no new tables).
    //
    //   1. Production Summary       — daily output, by machine, by product, QC pass rate
    //   2. Sales & Dispatch         — what shipped, to whom, value, gross profit
    //   3. Inventory Valuation      — raw + finished stock balance at cost
    //   4. Customer Order Tracking  — JO → WO → finished → dispatched per customer
    //
    // Filters are kept consistent across reports (from/to date, branch).
    // Every report endpoint has parallel Excel + PDF siblings.
    // ============================================================
    [RoutePrefix("emb-products/reports")]
    public class ReportsController : BaseController
    {
        // ====================================================================
        // 1. PRODUCTION SUMMARY
        // ====================================================================
        [HttpGet, Route("production-summary")]
        public ActionResult ProductionSummary(
            string from = null, string to = null,
            int? branchId = null, int? machineId = null, int? productId = null)
        {
            try
            {
                var data = BuildProductionSummary(from, to, branchId, machineId, productId);
                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to build production summary.", ex);
            }
        }

        [HttpGet, Route("production-summary/export-excel")]
        public ActionResult ProductionSummaryExcel(
            string from = null, string to = null,
            int? branchId = null, int? machineId = null, int? productId = null)
        {
            var data = BuildProductionSummary(from, to, branchId, machineId, productId);

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Production Summary");
                int row = 1;

                // ── Title
                ws.Cells[row, 1, row, 4].Merge = true;
                ws.Cells[row, 1].Value = "Production Summary Report";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 14;
                row += 2;

                // ── Filters echo
                ws.Cells[row, 1].Value = "From";      ws.Cells[row, 2].Value = data.filters.from ?? "all"; row++;
                ws.Cells[row, 1].Value = "To";        ws.Cells[row, 2].Value = data.filters.to   ?? "all"; row++;
                ws.Cells[row, 1].Value = "Generated"; ws.Cells[row, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); row++;
                for (int r = row - 3; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── KPI block
                ws.Cells[row, 1].Value = "Days covered";     ws.Cells[row, 2].Value = data.summary.daysCovered;   ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Total produced";   ws.Cells[row, 2].Value = data.summary.totalProduced; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Total accepted";   ws.Cells[row, 2].Value = data.summary.totalAccepted; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Total rejected";   ws.Cells[row, 2].Value = data.summary.totalRejected; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "QC pass rate (%)"; ws.Cells[row, 2].Value = data.summary.qcPassRate;    ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.0"; row++;
                for (int r = row - 5; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── Detail sections, all on this sheet
                row = AppendReportSection(ws, row, "By Day",
                    new[] { "Date", "Produced", "Accepted", "Rejected" },
                    data.byDay.Select(x => new object[] { x.date, x.produced, x.accepted, x.rejected }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0", "#,##0" },
                    totalsColumns: new[] { 2, 3, 4 });

                row = AppendReportSection(ws, row, "By Machine",
                    new[] { "Machine", "Produced", "Accepted" },
                    data.byMachine.Select(x => new object[] { x.machineName, x.produced, x.accepted }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0" },
                    totalsColumns: new[] { 2, 3 });

                row = AppendReportSection(ws, row, "By Product",
                    new[] { "Product", "Produced", "Accepted" },
                    data.byProduct.Select(x => new object[] { x.productName, x.produced, x.accepted }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0" },
                    totalsColumns: new[] { 2, 3 });

                if (ws.Dimension != null)
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "ProductionSummary.xlsx");
            }
        }

        [HttpGet, Route("production-summary/export-pdf")]
        public ActionResult ProductionSummaryPdf(
            string from = null, string to = null,
            int? branchId = null, int? machineId = null, int? productId = null)
        {
            var data = BuildProductionSummary(from, to, branchId, machineId, productId);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25f, 25f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                AddTitleAndFilters(doc, "Production Summary Report",
                    "From " + (data.filters.from ?? "all") + " to " + (data.filters.to ?? "all"));

                AddKpiTable(doc, new[] {
                    ("Days covered", data.summary.daysCovered.ToString("N0")),
                    ("Total produced", data.summary.totalProduced.ToString("N0")),
                    ("Total accepted", data.summary.totalAccepted.ToString("N0")),
                    ("Total rejected", data.summary.totalRejected.ToString("N0")),
                    ("QC pass rate", data.summary.qcPassRate.ToString("N1") + "%")
                });

                AddPdfTable(doc, "Daily Output", new[] { "Date", "Produced", "Accepted", "Rejected" },
                    data.byDay.Select(x => new[] { x.date, x.produced.ToString("N0"), x.accepted.ToString("N0"), x.rejected.ToString("N0") }));
                AddPdfTable(doc, "By Machine", new[] { "Machine", "Produced", "Accepted" },
                    data.byMachine.Select(x => new[] { x.machineName, x.produced.ToString("N0"), x.accepted.ToString("N0") }));
                AddPdfTable(doc, "By Product", new[] { "Product", "Produced", "Accepted" },
                    data.byProduct.Select(x => new[] { x.productName, x.produced.ToString("N0"), x.accepted.ToString("N0") }));

                AddPdfFooter(doc, data.byDay.Count + data.byMachine.Count + data.byProduct.Count);
                doc.Close();
                return File(ms.ToArray(), "application/pdf", "ProductionSummary.pdf");
            }
        }

        public class ProductionReportVm
        {
            public FilterEcho filters { get; set; }
            public ProductionKpiVm summary { get; set; }
            public List<ProductionDayVm> byDay { get; set; }
            public List<ProductionMachineVm> byMachine { get; set; }
            public List<ProductionProductVm> byProduct { get; set; }
        }
        public class ProductionKpiVm
        {
            public int daysCovered { get; set; }
            public decimal totalProduced { get; set; }
            public decimal totalAccepted { get; set; }
            public decimal totalRejected { get; set; }
            public decimal qcPassRate { get; set; }
        }
        public class ProductionDayVm     { public string date { get; set; } public decimal produced { get; set; } public decimal accepted { get; set; } public decimal rejected { get; set; } }
        public class ProductionMachineVm { public string machineName { get; set; } public decimal produced { get; set; } public decimal accepted { get; set; } }
        public class ProductionProductVm { public string productName { get; set; } public decimal produced { get; set; } public decimal accepted { get; set; } }

        private ProductionReportVm BuildProductionSummary(string from, string to,
            int? branchId, int? machineId, int? productId)
        {
            DateTime fromDt, toDt;
            ParseRange(from, to, out fromDt, out toDt);

            var q = db.DailyProduction
                .Include("Gen_Machines")
                .Include("WorkOrderItems")
                .Where(x => x.Deleted != true && x.IsPosted == true
                         && x.CreatedDate.HasValue
                         && x.CreatedDate >= fromDt
                         && x.CreatedDate <= toDt);

            if (machineId.HasValue && machineId.Value > 0)
                q = q.Where(x => x.MachineId == machineId.Value);

            var rows = q.ToList();

            // Branch + product filters via the related WorkOrder
            if (branchId.HasValue && branchId.Value > 0)
            {
                var woIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToList();
                var woBranchMap = db.WorkOrders
                    .Where(w => woIds.Contains(w.Id))
                    .ToDictionary(w => w.Id, w => w.BranchId);
                rows = rows.Where(r => r.WorkOrderId.HasValue
                                    && woBranchMap.ContainsKey(r.WorkOrderId.Value)
                                    && woBranchMap[r.WorkOrderId.Value] == branchId.Value).ToList();
            }

            var allWoIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToList();
            var woProductMap = db.WorkOrders
                .Where(w => allWoIds.Contains(w.Id))
                .ToDictionary(w => w.Id, w => new {
                    ProductId = w.ProductId,
                    ProductName = w.Gen_Products != null ? w.Gen_Products.Prod_Name : null
                });

            if (productId.HasValue && productId.Value > 0)
                rows = rows.Where(r => r.WorkOrderId.HasValue
                                    && woProductMap.ContainsKey(r.WorkOrderId.Value)
                                    && woProductMap[r.WorkOrderId.Value].ProductId == productId.Value).ToList();

            // QC rollup over the same date range
            var qcRows = db.QC_Inspection
                .Where(x => x.Deleted != true && x.IsPosted == true
                         && x.InspectionDate >= fromDt && x.InspectionDate <= toDt)
                .Select(x => new { x.PassedQty, x.FailedQty, x.ReworkQty, x.InspectedQty })
                .ToList();

            decimal qcInspected = qcRows.Sum(x => x.InspectedQty ?? 0m);
            decimal qcPassed    = qcRows.Sum(x => x.PassedQty   ?? 0m);
            decimal qcPassRate  = qcInspected > 0m ? Math.Round((qcPassed / qcInspected) * 100m, 1) : 0m;

            var byDay = rows
                .GroupBy(r => r.CreatedDate.Value.Date)
                .Select(g => new ProductionDayVm {
                    date     = g.Key.ToString("yyyy-MM-dd"),
                    produced = g.Sum(r => (decimal)(r.ActualQty ?? r.AcceptedQty ?? 0)),
                    accepted = g.Sum(r => (decimal)(r.AcceptedQty ?? 0)),
                    rejected = g.Sum(r => (decimal)(r.RejectedQty ?? 0))
                })
                .OrderBy(x => x.date)
                .ToList();

            var byMachine = rows
                .GroupBy(r => new { r.MachineId, MachineCode = r.Gen_Machines != null ? r.Gen_Machines.Code : null })
                .Select(g => new ProductionMachineVm {
                    machineName = g.Key.MachineCode ?? ("Machine #" + g.Key.MachineId),
                    produced    = g.Sum(r => (decimal)(r.ActualQty ?? r.AcceptedQty ?? 0)),
                    accepted    = g.Sum(r => (decimal)(r.AcceptedQty ?? 0))
                })
                .OrderByDescending(x => x.produced)
                .ToList();

            var byProduct = rows
                .GroupBy(r => r.WorkOrderId.HasValue && woProductMap.ContainsKey(r.WorkOrderId.Value)
                              ? woProductMap[r.WorkOrderId.Value].ProductName
                              : "-")
                .Select(g => new ProductionProductVm {
                    productName = g.Key ?? "-",
                    produced    = g.Sum(r => (decimal)(r.ActualQty ?? r.AcceptedQty ?? 0)),
                    accepted    = g.Sum(r => (decimal)(r.AcceptedQty ?? 0))
                })
                .OrderByDescending(x => x.produced)
                .ToList();

            decimal totalProduced = rows.Sum(r => (decimal)(r.ActualQty ?? r.AcceptedQty ?? 0));
            decimal totalAccepted = rows.Sum(r => (decimal)(r.AcceptedQty ?? 0));
            decimal totalRejected = rows.Sum(r => (decimal)(r.RejectedQty ?? 0));

            return new ProductionReportVm {
                filters = new FilterEcho { from = from, to = to },
                summary = new ProductionKpiVm {
                    daysCovered   = byDay.Count,
                    totalProduced = Math.Round(totalProduced, 0),
                    totalAccepted = Math.Round(totalAccepted, 0),
                    totalRejected = Math.Round(totalRejected, 0),
                    qcPassRate    = qcPassRate
                },
                byDay = byDay,
                byMachine = byMachine,
                byProduct = byProduct
            };
        }

        // ====================================================================
        // 2. SALES & DISPATCH
        // ====================================================================
        [HttpGet, Route("sales-dispatch")]
        public ActionResult SalesDispatch(
            string from = null, string to = null,
            int? branchId = null, int? customerId = null)
        {
            try
            {
                var data = BuildSalesDispatch(from, to, branchId, customerId);
                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to build sales report.", ex);
            }
        }

        [HttpGet, Route("sales-dispatch/export-excel")]
        public ActionResult SalesDispatchExcel(
            string from = null, string to = null,
            int? branchId = null, int? customerId = null)
        {
            var data = BuildSalesDispatch(from, to, branchId, customerId);

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Sales & Dispatch");
                int row = 1;

                // ── Title
                ws.Cells[row, 1, row, 4].Merge = true;
                ws.Cells[row, 1].Value = "Sales & Dispatch Report";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 14;
                row += 2;

                // ── Filters echo
                ws.Cells[row, 1].Value = "From";      ws.Cells[row, 2].Value = data.filters.from ?? "all"; row++;
                ws.Cells[row, 1].Value = "To";        ws.Cells[row, 2].Value = data.filters.to   ?? "all"; row++;
                ws.Cells[row, 1].Value = "Branch";    ws.Cells[row, 2].Value = ResolveBranchLabel(branchId); row++;
                ws.Cells[row, 1].Value = "Customer";  ws.Cells[row, 2].Value = ResolveCustomerLabel(customerId); row++;
                ws.Cells[row, 1].Value = "Generated"; ws.Cells[row, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); row++;
                for (int r = row - 5; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── KPI block
                ws.Cells[row, 1].Value = "Dispatches";   ws.Cells[row, 2].Value = data.summary.dispatchCount; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Total qty";    ws.Cells[row, 2].Value = data.summary.totalQty;      ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Total value";  ws.Cells[row, 2].Value = data.summary.totalValue;    ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00"; row++;
                ws.Cells[row, 1].Value = "Total cost";   ws.Cells[row, 2].Value = data.summary.totalCost;     ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00"; row++;
                ws.Cells[row, 1].Value = "Gross profit"; ws.Cells[row, 2].Value = data.summary.grossProfit;   ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00"; row++;
                ws.Cells[row, 1].Value = "Margin (%)";   ws.Cells[row, 2].Value = data.summary.marginPercent; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.0"; row++;
                for (int r = row - 6; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── Detail sections, all on this sheet
                row = AppendReportSection(ws, row, "By Customer",
                    new[] { "Customer", "Dispatches", "Qty", "Value" },
                    data.byCustomer.Select(x => new object[] { x.customerName, x.dispatchCount, x.qty, x.value }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0", "#,##0.00" },
                    totalsColumns: new[] { 2, 3, 4 });

                row = AppendReportSection(ws, row, "By Product",
                    new[] { "Product", "Qty", "Value" },
                    data.byProduct.Select(x => new object[] { x.productName, x.qty, x.value }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0.00" },
                    totalsColumns: new[] { 2, 3 });

                row = AppendReportSection(ws, row, "By Day",
                    new[] { "Date", "Dispatches", "Qty", "Value" },
                    data.byDay.Select(x => new object[] { x.date, x.dispatches, x.qty, x.value }).ToList(),
                    columnFormats: new[] { "", "#,##0", "#,##0", "#,##0.00" },
                    totalsColumns: new[] { 2, 3, 4 });

                if (ws.Dimension != null)
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "SalesDispatch.xlsx");
            }
        }

        [HttpGet, Route("sales-dispatch/export-pdf")]
        public ActionResult SalesDispatchPdf(
            string from = null, string to = null,
            int? branchId = null, int? customerId = null)
        {
            var data = BuildSalesDispatch(from, to, branchId, customerId);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25f, 25f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                AddTitleAndFilters(doc, "Sales & Dispatch Report",
                    "From " + (data.filters.from ?? "all") + " to " + (data.filters.to ?? "all"));

                AddKpiTable(doc, new[] {
                    ("Dispatches",   data.summary.dispatchCount.ToString("N0")),
                    ("Total qty",    data.summary.totalQty.ToString("N0")),
                    ("Total value",  data.summary.totalValue.ToString("N2")),
                    ("Total cost",   data.summary.totalCost.ToString("N2")),
                    ("Gross profit", data.summary.grossProfit.ToString("N2")),
                    ("Margin (%)",   data.summary.marginPercent.ToString("N1"))
                });

                AddPdfTable(doc, "By Customer", new[] { "Customer", "Dispatches", "Qty", "Value" },
                    data.byCustomer.Select(x => new[] { x.customerName, x.dispatchCount.ToString("N0"), x.qty.ToString("N0"), x.value.ToString("N2") }));
                AddPdfTable(doc, "By Product",  new[] { "Product", "Qty", "Value" },
                    data.byProduct.Select(x => new[] { x.productName, x.qty.ToString("N0"), x.value.ToString("N2") }));
                AddPdfTable(doc, "Daily",       new[] { "Date", "Dispatches", "Qty", "Value" },
                    data.byDay.Select(x => new[] { x.date, x.dispatches.ToString("N0"), x.qty.ToString("N0"), x.value.ToString("N2") }));

                AddPdfFooter(doc, data.byCustomer.Count + data.byProduct.Count + data.byDay.Count);
                doc.Close();
                return File(ms.ToArray(), "application/pdf", "SalesDispatch.pdf");
            }
        }

        public class SalesReportVm
        {
            public FilterEcho filters { get; set; }
            public SalesKpiVm summary { get; set; }
            public List<SalesCustomerVm> byCustomer { get; set; }
            public List<SalesProductVm>  byProduct  { get; set; }
            public List<SalesDayVm>      byDay      { get; set; }
        }
        public class SalesKpiVm
        {
            public int     dispatchCount { get; set; }
            public decimal totalQty       { get; set; }
            public decimal totalValue     { get; set; }
            public decimal totalCost      { get; set; }
            public decimal grossProfit    { get; set; }
            public decimal marginPercent  { get; set; }
        }
        public class SalesCustomerVm { public string customerName { get; set; } public int dispatchCount { get; set; } public decimal qty { get; set; } public decimal value { get; set; } }
        public class SalesProductVm  { public string productName  { get; set; } public decimal qty { get; set; } public decimal value { get; set; } }
        public class SalesDayVm      { public string date { get; set; } public int dispatches { get; set; } public decimal qty { get; set; } public decimal value { get; set; } }

        private SalesReportVm BuildSalesDispatch(string from, string to, int? branchId, int? customerId)
        {
            DateTime fromDt, toDt;
            ParseRange(from, to, out fromDt, out toDt);

            var dispatches = db.Dispatch
                .Include("Hr_Customers")
                .Where(d => d.Deleted != true && d.IsPosted == true && d.IsReversed != true
                         && d.DispatchDate >= fromDt && d.DispatchDate <= toDt);

            if (branchId.HasValue && branchId.Value > 0)
                dispatches = dispatches.Where(d => d.BranchId == branchId.Value);
            if (customerId.HasValue && customerId.Value > 0)
                dispatches = dispatches.Where(d => d.CustomerId == customerId.Value);

            var dispatchList = dispatches.ToList();
            var dispatchIds  = dispatchList.Select(d => d.Id).ToList();

            var items = dispatchIds.Count == 0
                ? new List<DispatchItemFlat>()
                : (from di in db.DispatchItems
                   where di.Deleted != true && dispatchIds.Contains(di.DispatchId)
                   join p in db.Gen_Products on di.ProductId equals p.Id into pj
                   from p in pj.DefaultIfEmpty()
                   select new DispatchItemFlat
                   {
                       DispatchId  = di.DispatchId,
                       ProductId   = di.ProductId,
                       ProductName = p != null ? p.Prod_Name : "-",
                       Qty         = di.DispatchQty ?? 0m,
                       Rate        = di.SalePrice    ?? 0m,
                       Amount      = di.Amount       ?? 0m,
                       Cost        = (di.CostPrice   ?? 0m) * (di.DispatchQty ?? 0m)
                   }).ToList();

            var dispatchById = dispatchList.ToDictionary(d => d.Id);

            var byCustomer = items
                .GroupBy(i => new {
                    CustomerName = (dispatchById[i.DispatchId].Hr_Customers != null
                                    ? dispatchById[i.DispatchId].Hr_Customers.Customer_Name
                                    : (string.Equals(dispatchById[i.DispatchId].SaleType, "DIRECT_SALE", StringComparison.OrdinalIgnoreCase)
                                        ? "Walk-in" : "Stock")),
                    DispatchId = i.DispatchId
                })
                .GroupBy(g => g.Key.CustomerName)
                .Select(g => new SalesCustomerVm {
                    customerName  = g.Key ?? "-",
                    dispatchCount = g.Select(x => x.Key.DispatchId).Distinct().Count(),
                    qty           = g.SelectMany(x => x).Sum(x => x.Qty),
                    value         = g.SelectMany(x => x).Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.value)
                .ToList();

            var byProduct = items
                .GroupBy(i => i.ProductName)
                .Select(g => new SalesProductVm {
                    productName = g.Key ?? "-",
                    qty         = g.Sum(i => i.Qty),
                    value       = g.Sum(i => i.Amount)
                })
                .OrderByDescending(x => x.value)
                .ToList();

            var byDay = items
                .GroupBy(i => dispatchById[i.DispatchId].DispatchDate.Date)
                .Select(g => new SalesDayVm {
                    date       = g.Key.ToString("yyyy-MM-dd"),
                    dispatches = g.Select(x => x.DispatchId).Distinct().Count(),
                    qty        = g.Sum(i => i.Qty),
                    value      = g.Sum(i => i.Amount)
                })
                .OrderBy(x => x.date)
                .ToList();

            decimal totalQty   = items.Sum(i => i.Qty);
            decimal totalValue = items.Sum(i => i.Amount);
            decimal totalCost  = items.Sum(i => i.Cost);
            decimal grossProfit = totalValue - totalCost;
            decimal margin = totalValue > 0m ? Math.Round((grossProfit / totalValue) * 100m, 1) : 0m;

            return new SalesReportVm {
                filters = new FilterEcho { from = from, to = to },
                summary = new SalesKpiVm {
                    dispatchCount  = dispatchList.Count,
                    totalQty       = Math.Round(totalQty, 0),
                    totalValue     = Math.Round(totalValue, 2),
                    totalCost      = Math.Round(totalCost, 2),
                    grossProfit    = Math.Round(grossProfit, 2),
                    marginPercent  = margin
                },
                byCustomer = byCustomer,
                byProduct  = byProduct,
                byDay      = byDay
            };
        }

        private class DispatchItemFlat
        {
            public int     DispatchId  { get; set; }
            public int?    ProductId   { get; set; }
            public string  ProductName { get; set; }
            public decimal Qty         { get; set; }
            public decimal Rate        { get; set; }
            public decimal Amount      { get; set; }
            public decimal Cost        { get; set; }
        }

        // ====================================================================
        // 3. INVENTORY VALUATION
        // ====================================================================
        [HttpGet, Route("inventory-valuation")]
        public ActionResult InventoryValuation(int? branchId = null, string stockType = "all")
        {
            try
            {
                var data = BuildInventoryValuation(branchId, stockType);
                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to build inventory valuation.", ex);
            }
        }

        [HttpGet, Route("inventory-valuation/export-excel")]
        public ActionResult InventoryValuationExcel(int? branchId = null, string stockType = "all")
        {
            var data = BuildInventoryValuation(branchId, stockType);
            string branchLabel = ResolveBranchLabel(branchId);
            string stockLabel  = StockTypeLabel(stockType);

            using (var pkg = new ExcelPackage())
            {
                // One sheet, mirrors the web layout so users don't have to
                // switch tabs to see the per-item rows.
                var ws = pkg.Workbook.Worksheets.Add("Inventory Valuation");
                int row = 1;

                // ── Title
                ws.Cells[row, 1, row, 6].Merge = true;
                ws.Cells[row, 1].Value = "Inventory Valuation";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 14;
                row += 2;

                // ── Filters echo
                ws.Cells[row, 1].Value = "Stock type"; ws.Cells[row, 2].Value = stockLabel;  row++;
                ws.Cells[row, 1].Value = "Branch";     ws.Cells[row, 2].Value = branchLabel; row++;
                ws.Cells[row, 1].Value = "Generated";  ws.Cells[row, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); row++;
                for (int r = row - 3; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── KPI block
                ws.Cells[row, 1].Value = "Raw items count";      ws.Cells[row, 2].Value = data.summary.rawItemCount;     ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Raw value";            ws.Cells[row, 2].Value = data.summary.rawValue;         ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00"; row++;
                ws.Cells[row, 1].Value = "Finished items count"; ws.Cells[row, 2].Value = data.summary.finishedItemCount; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Finished value";       ws.Cells[row, 2].Value = data.summary.finishedValue;    ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00"; row++;
                ws.Cells[row, 1].Value = "Grand total";          ws.Cells[row, 2].Value = data.summary.grandTotal;       ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[row, 1, row, 2].Style.Font.Bold = true;
                for (int r = row - 4; r <= row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row += 2;

                // ── Raw Materials section
                ws.Cells[row, 1, row, 5].Merge = true;
                ws.Cells[row, 1].Value = "Raw Materials";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 12;
                ws.Cells[row, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 244, 253));
                row++;
                row = WriteRawMaterialsBlock(ws, row, data.rawItems);
                row++;

                // ── Finished Stock section
                ws.Cells[row, 1, row, 6].Merge = true;
                ws.Cells[row, 1].Value = "Finished Stock";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 12;
                ws.Cells[row, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 244, 253));
                row++;
                WriteFinishedStockBlock(ws, row, data.finishedStock);

                if (ws.Dimension != null)
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "InventoryValuation.xlsx");
            }
        }

        private int WriteRawMaterialsBlock(ExcelWorksheet ws, int startRow, List<InventoryRawVm> rows)
        {
            string[] headers = { "Item", "Type", "Balance", "Last Rate", "Value" };
            int headerRow = startRow;
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cells[headerRow, c + 1].Value = headers[c];
                ws.Cells[headerRow, c + 1].Style.Font.Bold = true;
                ws.Cells[headerRow, c + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[headerRow, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                ws.Cells[headerRow, c + 1].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            int r = headerRow + 1;
            foreach (var x in rows)
            {
                ws.Cells[r, 1].Value = x.name;
                ws.Cells[r, 2].Value = x.type;
                ws.Cells[r, 3].Value = x.balance;  ws.Cells[r, 3].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 4].Value = x.lastRate; ws.Cells[r, 4].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 5].Value = x.value;    ws.Cells[r, 5].Style.Numberformat.Format = "#,##0.00";
                r++;
            }

            if (r > headerRow + 1)
            {
                int lastDataRow = r - 1;
                ws.Cells[r, 1].Value = "Total";
                ws.Cells[r, 3].Formula = "SUM(C" + (headerRow + 1) + ":C" + lastDataRow + ")";
                ws.Cells[r, 5].Formula = "SUM(E" + (headerRow + 1) + ":E" + lastDataRow + ")";
                ws.Cells[r, 3].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 5].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 1, r, 5].Style.Font.Bold = true;
                ws.Cells[r, 1, r, 5].Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                r++;
            }
            else
            {
                ws.Cells[r, 1].Value = "(no raw stock)";
                ws.Cells[r, 1].Style.Font.Italic = true;
                r++;
            }
            return r;
        }

        private int WriteFinishedStockBlock(ExcelWorksheet ws, int startRow, List<InventoryFinishedVm> rows)
        {
            string[] headers = { "Product", "Reserved", "Free", "Total", "Unit Cost", "Value" };
            int headerRow = startRow;
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cells[headerRow, c + 1].Value = headers[c];
                ws.Cells[headerRow, c + 1].Style.Font.Bold = true;
                ws.Cells[headerRow, c + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[headerRow, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                ws.Cells[headerRow, c + 1].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            int r = headerRow + 1;
            foreach (var x in rows)
            {
                ws.Cells[r, 1].Value = x.productName;
                ws.Cells[r, 2].Value = x.reserved; ws.Cells[r, 2].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 3].Value = x.free;     ws.Cells[r, 3].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 4].Value = x.balance;  ws.Cells[r, 4].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 5].Value = x.unitCost; ws.Cells[r, 5].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 6].Value = x.value;    ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.00";
                r++;
            }

            if (r > headerRow + 1)
            {
                int lastDataRow = r - 1;
                ws.Cells[r, 1].Value = "Total";
                ws.Cells[r, 2].Formula = "SUM(B" + (headerRow + 1) + ":B" + lastDataRow + ")"; ws.Cells[r, 2].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 3].Formula = "SUM(C" + (headerRow + 1) + ":C" + lastDataRow + ")"; ws.Cells[r, 3].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 4].Formula = "SUM(D" + (headerRow + 1) + ":D" + lastDataRow + ")"; ws.Cells[r, 4].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 6].Formula = "SUM(F" + (headerRow + 1) + ":F" + lastDataRow + ")"; ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 1, r, 6].Style.Font.Bold = true;
                ws.Cells[r, 1, r, 6].Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                r++;
            }
            else
            {
                ws.Cells[r, 1].Value = "(no finished stock)";
                ws.Cells[r, 1].Style.Font.Italic = true;
                r++;
            }
            return r;
        }

        private string ResolveBranchLabel(int? branchId)
        {
            if (!branchId.HasValue || branchId.Value <= 0)
                return "All branches";
            var name = db.Branches.Where(b => b.Id == branchId.Value)
                .Select(b => b.Name).FirstOrDefault();
            return string.IsNullOrWhiteSpace(name) ? ("Branch #" + branchId.Value) : name;
        }

        private string StockTypeLabel(string stockType)
        {
            var st = (stockType ?? "all").Trim().ToLower();
            if (st == "raw")      return "Raw materials only";
            if (st == "finished") return "Finished stock only";
            return "All (raw + finished)";
        }


        [HttpGet, Route("inventory-valuation/export-pdf")]
        public ActionResult InventoryValuationPdf(int? branchId = null, string stockType = "all")
        {
            var data = BuildInventoryValuation(branchId, stockType);
            string branchLabel = ResolveBranchLabel(branchId);
            string stockLabel  = StockTypeLabel(stockType);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25f, 25f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                AddTitleAndFilters(doc, "Inventory Valuation",
                    "Stock type: " + stockLabel + "   |   Branch: " + branchLabel +
                    "   |   Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

                AddKpiTable(doc, new[] {
                    ("Raw items count",      data.summary.rawItemCount.ToString("N0")),
                    ("Raw value",            data.summary.rawValue.ToString("N2")),
                    ("Finished items count", data.summary.finishedItemCount.ToString("N0")),
                    ("Finished value",       data.summary.finishedValue.ToString("N2")),
                    ("Grand total",          data.summary.grandTotal.ToString("N2"))
                });

                AddPdfTable(doc, "Raw Materials", new[] { "Item", "Type", "Balance", "Last Rate", "Value" },
                    data.rawItems.Select(x => new[] { x.name, x.type, x.balance.ToString("N2"), x.lastRate.ToString("N2"), x.value.ToString("N2") }));
                AddPdfTable(doc, "Finished Stock", new[] { "Product", "Reserved", "Free", "Total", "Unit Cost", "Value" },
                    data.finishedStock.Select(x => new[] { x.productName, x.reserved.ToString("N0"), x.free.ToString("N0"), x.balance.ToString("N0"), x.unitCost.ToString("N2"), x.value.ToString("N2") }));

                AddPdfFooter(doc, data.rawItems.Count + data.finishedStock.Count);
                doc.Close();
                return File(ms.ToArray(), "application/pdf", "InventoryValuation.pdf");
            }
        }

        public class InventoryReportVm
        {
            public InventoryKpiVm summary { get; set; }
            public List<InventoryRawVm> rawItems { get; set; }
            public List<InventoryFinishedVm> finishedStock { get; set; }
        }
        public class InventoryKpiVm
        {
            public int     rawItemCount     { get; set; }
            public decimal rawValue         { get; set; }
            public int     finishedItemCount { get; set; }
            public decimal finishedValue    { get; set; }
            public decimal grandTotal       { get; set; }
        }
        public class InventoryRawVm      { public string name { get; set; } public string type { get; set; } public decimal balance { get; set; } public decimal lastRate { get; set; } public decimal value { get; set; } }
        public class InventoryFinishedVm { public string productName { get; set; } public decimal reserved { get; set; } public decimal free { get; set; } public decimal balance { get; set; } public decimal unitCost { get; set; } public decimal value { get; set; } }

        private InventoryReportVm BuildInventoryValuation(int? branchId, string stockType)
        {
            // branchId 0 / null = All branches. Matches the Store page,
            // which lets users opt out of branch scoping.
            int bId = (branchId.HasValue && branchId.Value > 0) ? branchId.Value : 0;
            bool allBranches = (bId == 0);
            string st = (stockType ?? "all").Trim().ToLower();

            var raw = new List<InventoryRawVm>();
            var finished = new List<InventoryFinishedVm>();

            if (st == "all" || st == "raw")
            {
                // Aggregate the ledger the same way the Store page does:
                //   balance = Σ Opening + Σ In - Σ Out  per (itemId, branchId)
                // The Gen_Store.CurrentQty column is a running-balance snapshot
                // that some write paths fail to keep in sync, so reading the
                // last row's CurrentQty (the old method) under-counted balances
                // for any item with an Opening entry. Aggregation is the
                // single source of truth used by GetCurrentBalance() and the
                // Store balance-list endpoint.
                var storeQ = db.Gen_Store
                    .Include("Gen_RawItems")
                    .Where(s => s.Deleted == false
                             && s.Gen_RawItems != null
                             && s.Gen_RawItems.Deleted != true);

                if (!allBranches)
                    storeQ = storeQ.Where(s => s.BranchId == bId);

                var perItemBranch = storeQ
                    .ToList()
                    .GroupBy(s => new {
                        s.ItemId,
                        ItemName = s.Gen_RawItems.ItemName,
                        Type     = s.Gen_RawItems.Type,
                        s.BranchId
                    })
                    .Select(g => {
                        decimal balance = g.Sum(x => (x.OpeningQty ?? 0m)
                                                   + (x.InQty ?? 0m)
                                                   - (x.OutQty ?? 0m));
                        decimal rate = g
                            .OrderByDescending(x => x.TransactionDate)
                            .ThenByDescending(x => x.Id)
                            .Select(x => x.Rate ?? 0m)
                            .FirstOrDefault();
                        return new {
                            g.Key.ItemId,
                            g.Key.ItemName,
                            g.Key.Type,
                            Balance = balance,
                            Rate    = rate
                        };
                    })
                    .ToList();

                // Roll up across branches so each raw item appears once in the
                // report. Balance sums; rate is the qty-weighted average across
                // the per-branch latest rates so an item that exists in two
                // branches at different last-purchase rates still values
                // correctly. Falls back to the simple latest rate if total
                // balance is zero.
                var grouped = perItemBranch
                    .Where(x => x.Balance > 0m)
                    .GroupBy(x => new { x.ItemId, x.ItemName, x.Type })
                    .Select(g => {
                        decimal totalBal = g.Sum(x => x.Balance);
                        decimal effectiveRate = totalBal > 0m
                            ? g.Sum(x => x.Balance * x.Rate) / totalBal
                            : g.Select(x => x.Rate).FirstOrDefault();
                        return new InventoryRawVm {
                            name     = g.Key.ItemName,
                            type     = g.Key.Type ?? "-",
                            balance  = Math.Round(totalBal, 2),
                            lastRate = Math.Round(effectiveRate, 2),
                            value    = Math.Round(totalBal * effectiveRate, 2)
                        };
                    })
                    .ToList();

                raw.AddRange(grouped);
            }

            if (st == "all" || st == "finished")
            {
                // Aggregate Gen_Stocks by ProdId / ItemId for FINISHED stage.
                // Free vs Reserved is derived per-branch by the BaseController
                // helpers, so when allBranches is on we iterate the unique
                // (productId, branchId) pairs and sum across branches.
                var finQ = db.Gen_Stocks
                    .Where(s => s.Deleted != true
                             && s.StockStage == "FINISHED"
                             && (s.ItemType == "PRODUCT" || s.ItemType == "FINISHED"));

                if (!allBranches)
                    finQ = finQ.Where(s => s.BranchId == bId);

                var fin = finQ.ToList();

                var pairs = fin
                    .Where(s => s.BranchId.HasValue)
                    .Select(s => new {
                        ProductId = s.ProdId ?? s.ItemId ?? 0,
                        BranchId  = s.BranchId.Value
                    })
                    .Where(x => x.ProductId > 0)
                    .Distinct()
                    .ToList();

                var perPair = pairs
                    .Select(p => new {
                        p.ProductId,
                        p.BranchId,
                        Free     = GetFreeCompanyStockBalance(p.ProductId, p.BranchId),
                        Reserved = GetCustomerReservedStockBalance(p.ProductId, p.BranchId),
                        UnitCost = GetProductMaterialPerUnitCost(p.ProductId, p.BranchId)
                    })
                    .ToList();

                var grouped = perPair
                    .GroupBy(x => x.ProductId)
                    .Select(g => {
                        decimal free = g.Sum(x => x.Free);
                        decimal reserved = g.Sum(x => x.Reserved);
                        decimal balance = free + reserved;
                        // Weighted unit cost across branches that hold stock
                        decimal weightedCost = balance > 0m
                            ? g.Sum(x => (x.Free + x.Reserved) * x.UnitCost) / balance
                            : g.Select(x => x.UnitCost).FirstOrDefault();
                        return new {
                            ProductId = g.Key,
                            Free      = free,
                            Reserved  = reserved,
                            Balance   = balance,
                            UnitCost  = weightedCost
                        };
                    })
                    .Where(x => x.Balance > 0m)
                    .ToList();

                var productIds = grouped.Select(x => x.ProductId).ToList();
                var productMap = db.Gen_Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionary(p => p.Id, p => p.Prod_Name);

                foreach (var g in grouped)
                {
                    finished.Add(new InventoryFinishedVm {
                        productName = productMap.ContainsKey(g.ProductId) ? productMap[g.ProductId] : ("Product #" + g.ProductId),
                        reserved    = Math.Round(g.Reserved, 0),
                        free        = Math.Round(g.Free, 0),
                        balance     = Math.Round(g.Balance, 0),
                        unitCost    = Math.Round(g.UnitCost, 2),
                        value       = Math.Round(g.Balance * g.UnitCost, 2)
                    });
                }
            }

            raw = raw.OrderByDescending(x => x.value).ToList();
            finished = finished.OrderByDescending(x => x.value).ToList();

            return new InventoryReportVm {
                rawItems = raw,
                finishedStock = finished,
                summary = new InventoryKpiVm {
                    rawItemCount      = raw.Count,
                    rawValue          = raw.Sum(x => x.value),
                    finishedItemCount = finished.Count,
                    finishedValue     = finished.Sum(x => x.value),
                    grandTotal        = raw.Sum(x => x.value) + finished.Sum(x => x.value)
                }
            };
        }

        // ====================================================================
        // 4. CUSTOMER ORDER TRACKING
        // ====================================================================
        [HttpGet, Route("customer-tracking")]
        public ActionResult CustomerTracking(int? customerId = null, string status = "all")
        {
            try
            {
                var data = BuildCustomerTracking(customerId, status);
                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to build customer tracking.", ex);
            }
        }

        [HttpGet, Route("customer-tracking/export-excel")]
        public ActionResult CustomerTrackingExcel(int? customerId = null, string status = "all")
        {
            var data = BuildCustomerTracking(customerId, status);
            string statusLabel   = CustomerTrackingStatusLabel(status);
            string customerLabel = ResolveCustomerLabel(customerId);

            using (var pkg = new ExcelPackage())
            {
                // One sheet, mirrors the web layout.
                var ws = pkg.Workbook.Worksheets.Add("Customer Tracking");
                int row = 1;

                // ── Title (spans the orders table width)
                ws.Cells[row, 1, row, 12].Merge = true;
                ws.Cells[row, 1].Value = "Customer Order Tracking";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 14;
                row += 2;

                // ── Filters echo
                ws.Cells[row, 1].Value = "Filter status"; ws.Cells[row, 2].Value = statusLabel;   row++;
                ws.Cells[row, 1].Value = "Customer";      ws.Cells[row, 2].Value = customerLabel; row++;
                ws.Cells[row, 1].Value = "Generated";     ws.Cells[row, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); row++;
                for (int r = row - 3; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── KPI block
                ws.Cells[row, 1].Value = "Total orders";      ws.Cells[row, 2].Value = data.summary.totalOrders;     ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Open orders";       ws.Cells[row, 2].Value = data.summary.openCount;       ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Fully dispatched";  ws.Cells[row, 2].Value = data.summary.fullyDispatched; ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Partial / running"; ws.Cells[row, 2].Value = data.summary.partialCount;    ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                ws.Cells[row, 1].Value = "Outstanding qty";   ws.Cells[row, 2].Value = data.summary.outstandingQty;  ws.Cells[row, 2].Style.Numberformat.Format = "#,##0"; row++;
                for (int r = row - 5; r < row; r++) ws.Cells[r, 1].Style.Font.Bold = true;
                row++;

                // ── Orders section
                ws.Cells[row, 1, row, 12].Merge = true;
                ws.Cells[row, 1].Value = "Orders";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Size = 12;
                ws.Cells[row, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 244, 253));
                row++;

                WriteCustomerTrackingBlock(ws, row, data.rows);

                if (ws.Dimension != null)
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "CustomerTracking.xlsx");
            }
        }

        private string CustomerTrackingStatusLabel(string status)
        {
            var s = (status ?? "all").Trim().ToLower();
            if (s == "open")       return "Open";
            if (s == "partial")    return "Partial / Running";
            if (s == "dispatched") return "Fully Dispatched";
            return "All Orders";
        }

        private string ResolveCustomerLabel(int? customerId)
        {
            if (!customerId.HasValue || customerId.Value <= 0)
                return "All customers";
            var name = db.Hr_Customers.Where(c => c.Id == customerId.Value)
                .Select(c => c.Customer_Name).FirstOrDefault();
            return string.IsNullOrWhiteSpace(name) ? ("Customer #" + customerId.Value) : name;
        }

        private int WriteCustomerTrackingBlock(ExcelWorksheet ws, int startRow, List<CustomerTrackingRowVm> rows)
        {
            string[] headers = {
                "JO Code", "Customer", "Product", "Design",
                "Order Qty", "Order Date", "Due Date",
                "WOs", "Finished", "Dispatched", "Outstanding", "Status"
            };
            int headerRow = startRow;
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cells[headerRow, c + 1].Value = headers[c];
                ws.Cells[headerRow, c + 1].Style.Font.Bold = true;
                ws.Cells[headerRow, c + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[headerRow, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                ws.Cells[headerRow, c + 1].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            int r = headerRow + 1;
            foreach (var x in rows)
            {
                ws.Cells[r, 1].Value  = x.jobOrderCode;
                ws.Cells[r, 2].Value  = x.customerName;
                ws.Cells[r, 3].Value  = x.productName;
                ws.Cells[r, 4].Value  = x.designCode;
                ws.Cells[r, 5].Value  = x.orderQty;        ws.Cells[r, 5].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 6].Value  = x.orderDate;
                ws.Cells[r, 7].Value  = x.dueDate;
                ws.Cells[r, 8].Value  = x.workOrderCount;  ws.Cells[r, 8].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 9].Value  = x.finishedQty;     ws.Cells[r, 9].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 10].Value = x.dispatchedQty;   ws.Cells[r, 10].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 11].Value = x.outstandingQty;  ws.Cells[r, 11].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 12].Value = x.status;
                r++;
            }

            if (r > headerRow + 1)
            {
                int lastDataRow = r - 1;
                ws.Cells[r, 1].Value = "Total";
                ws.Cells[r, 5].Formula  = "SUM(E"  + (headerRow + 1) + ":E"  + lastDataRow + ")"; ws.Cells[r, 5].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 8].Formula  = "SUM(H"  + (headerRow + 1) + ":H"  + lastDataRow + ")"; ws.Cells[r, 8].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 9].Formula  = "SUM(I"  + (headerRow + 1) + ":I"  + lastDataRow + ")"; ws.Cells[r, 9].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 10].Formula = "SUM(J"  + (headerRow + 1) + ":J"  + lastDataRow + ")"; ws.Cells[r, 10].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 11].Formula = "SUM(K"  + (headerRow + 1) + ":K"  + lastDataRow + ")"; ws.Cells[r, 11].Style.Numberformat.Format = "#,##0";
                ws.Cells[r, 1, r, 12].Style.Font.Bold = true;
                ws.Cells[r, 1, r, 12].Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                r++;
            }
            else
            {
                ws.Cells[r, 1].Value = "(no orders match the filter)";
                ws.Cells[r, 1].Style.Font.Italic = true;
                r++;
            }
            return r;
        }

        [HttpGet, Route("customer-tracking/export-pdf")]
        public ActionResult CustomerTrackingPdf(int? customerId = null, string status = "all")
        {
            var data = BuildCustomerTracking(customerId, status);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 20f, 20f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                AddTitleAndFilters(doc, "Customer Order Tracking",
                    "Status: " + CustomerTrackingStatusLabel(status) +
                    "   |   Customer: " + ResolveCustomerLabel(customerId) +
                    "   |   Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

                AddKpiTable(doc, new[] {
                    ("Total orders",      data.summary.totalOrders.ToString("N0")),
                    ("Open orders",       data.summary.openCount.ToString("N0")),
                    ("Fully dispatched",  data.summary.fullyDispatched.ToString("N0")),
                    ("Partial / running", data.summary.partialCount.ToString("N0")),
                    ("Outstanding qty",   data.summary.outstandingQty.ToString("N0"))
                });

                AddPdfTable(doc, "Orders",
                    new[] { "JO", "Customer", "Product", "Design", "Ord", "Date", "Due", "WO", "Finished", "Dispatched", "Out", "Status" },
                    data.rows.Select(x => new[] {
                        x.jobOrderCode, x.customerName, x.productName, x.designCode,
                        x.orderQty.ToString("N0"), x.orderDate, x.dueDate ?? "-",
                        x.workOrderCount.ToString("N0"),
                        x.finishedQty.ToString("N0"), x.dispatchedQty.ToString("N0"),
                        x.outstandingQty.ToString("N0"), x.status
                    }));

                AddPdfFooter(doc, data.rows.Count);
                doc.Close();
                return File(ms.ToArray(), "application/pdf", "CustomerTracking.pdf");
            }
        }

        public class CustomerTrackingReportVm
        {
            public CustomerTrackingKpiVm summary { get; set; }
            public List<CustomerTrackingRowVm> rows { get; set; }
        }
        public class CustomerTrackingKpiVm
        {
            public int     totalOrders      { get; set; }
            public int     openCount        { get; set; }
            public int     fullyDispatched  { get; set; }
            public int     partialCount     { get; set; }
            public decimal outstandingQty   { get; set; }
        }
        public class CustomerTrackingRowVm
        {
            public int     jobOrderId      { get; set; }
            public string  jobOrderCode    { get; set; }
            public string  customerName    { get; set; }
            public string  productName     { get; set; }
            public string  designCode      { get; set; }
            public int     orderQty        { get; set; }
            public string  orderDate       { get; set; }
            public string  dueDate         { get; set; }
            public int     workOrderCount  { get; set; }
            public decimal finishedQty     { get; set; }
            public decimal dispatchedQty   { get; set; }
            public decimal outstandingQty  { get; set; }
            public string  status          { get; set; }
        }

        private CustomerTrackingReportVm BuildCustomerTracking(int? customerId, string status)
        {
            string st = (status ?? "all").Trim().ToLower();

            var jos = db.Gen_JobOrders
                .Include("Gen_Products")
                .Include("Gen_Design")
                .Include("Hr_Customers")
                .Where(j => j.Deleted != true && j.IsPosted == true)
                .AsQueryable();

            if (customerId.HasValue && customerId.Value > 0)
                jos = jos.Where(j => j.CustomerId == customerId.Value);

            var joList = jos.OrderByDescending(j => j.Id).Take(500).ToList();
            var joIds  = joList.Select(j => j.Id).ToList();

            // Work order count per JO
            var woCounts = db.WorkOrders
                .Where(w => w.JobOrderId.HasValue && joIds.Contains(w.JobOrderId.Value) && w.Deleted != true)
                .GroupBy(w => w.JobOrderId.Value)
                .Select(g => new { JobOrderId = g.Key, Count = g.Count() })
                .ToList()
                .ToDictionary(x => x.JobOrderId, x => x.Count);

            // Finished qty per JO: sum QC_Inspection.PassedQty across all WOs of the JO
            var woIdsByJo = db.WorkOrders
                .Where(w => w.JobOrderId.HasValue && joIds.Contains(w.JobOrderId.Value) && w.Deleted != true)
                .Select(w => new { w.JobOrderId, w.Id })
                .ToList()
                .GroupBy(x => x.JobOrderId.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

            var qcPassedByJo = new Dictionary<int, decimal>();
            foreach (var kv in woIdsByJo)
            {
                decimal passed = db.QC_Inspection
                    .Where(q => q.Deleted != true && q.IsPosted == true
                             && q.WorkOrderId.HasValue && kv.Value.Contains(q.WorkOrderId.Value))
                    .Sum(q => (decimal?)q.PassedQty) ?? 0m;
                qcPassedByJo[kv.Key] = passed;
            }

            // Dispatched qty per JO: dispatch items where the related Work Order's JobOrderId matches.
            // Note: Gen_JobOrders.ProductId is non-nullable int (no .HasValue/.Value);
            //       Gen_JobOrders.CustomerId IS nullable.
            var dispatchByJo = new Dictionary<int, decimal>();
            foreach (var kv in woIdsByJo)
            {
                int joIdLocal = kv.Key;
                var jo = joList.FirstOrDefault(x => x.Id == joIdLocal);
                if (jo == null || !jo.CustomerId.HasValue) continue;

                int productIdLocal  = jo.ProductId;
                int customerIdLocal = jo.CustomerId.Value;

                decimal qty = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productIdLocal
                              && di.Dispatch.CustomerId == customerIdLocal
                              && di.Dispatch.IsPosted == true
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsReversed != true)
                    .Sum(di => (decimal?)di.DispatchQty) ?? 0m;
                dispatchByJo[joIdLocal] = qty;
            }

            var rows = new List<CustomerTrackingRowVm>();
            foreach (var j in joList)
            {
                decimal finished = qcPassedByJo.ContainsKey(j.Id) ? qcPassedByJo[j.Id] : 0m;
                decimal ordered  = (decimal)j.OrderQty;

                // Dispatched and Outstanding are computed differently per JO type,
                // but Status uses the same scale (dispatched vs ordered) for both:
                //
                //   Customer JO — virtual "customer" = the actual customer.
                //     dispatched  = sum of DispatchItems matching this customer + product
                //     outstanding = ordered − dispatched   (units still owed)
                //
                //   Stock JO — virtual "customer" = the walk-in sales pool. The 1,000
                //     produced units enter free stock and any walk-in dispatch drains
                //     them. Attribute drained units to this JO:
                //       dispatched  = max(0, finished − current free stock)
                //       outstanding = min(free stock, finished)
                //     Once all 1,000 are sold (free stock = 0), the JO graduates to
                //     DISPATCHED status — same as a customer JO that's fully delivered.
                bool isStock = j.IsCompanyStockOrder;
                decimal dispatched;
                decimal outstanding;

                if (isStock)
                {
                    decimal freeStock = j.ProductId > 0
                        ? GetFreeCompanyStockBalance(j.ProductId, j.BranchId)
                        : 0m;
                    if (freeStock < 0m) freeStock = 0m;

                    dispatched  = Math.Max(0m, finished - freeStock);
                    outstanding = Math.Min(freeStock, finished);
                }
                else
                {
                    dispatched  = dispatchByJo.ContainsKey(j.Id) ? dispatchByJo[j.Id] : 0m;
                    outstanding = ordered - dispatched;
                    if (outstanding < 0m) outstanding = 0m;
                }

                string rowStatus;
                if (dispatched >= ordered && ordered > 0m) rowStatus = "DISPATCHED";
                else if (dispatched > 0m)                  rowStatus = "PARTIAL";
                else if (finished > 0m)                    rowStatus = "READY";
                else                                       rowStatus = "OPEN";

                if (st == "open"   &&  rowStatus == "DISPATCHED") continue;
                if (st == "closed" &&  rowStatus != "DISPATCHED") continue;

                rows.Add(new CustomerTrackingRowVm {
                    jobOrderId      = j.Id,
                    jobOrderCode    = j.JobOrderCode,
                    customerName    = j.Hr_Customers != null ? j.Hr_Customers.Customer_Name : (j.IsCompanyStockOrder ? "Stock" : "-"),
                    productName     = j.Gen_Products  != null ? j.Gen_Products.Prod_Name    : "-",
                    designCode      = j.Gen_Design    != null ? (j.Gen_Design.D_Code ?? "") : "-",
                    orderQty        = (int)ordered,
                    orderDate       = j.OrderDate.ToString("yyyy-MM-dd"),
                    dueDate         = j.DueDate.HasValue ? j.DueDate.Value.ToString("yyyy-MM-dd") : null,
                    workOrderCount  = woCounts.ContainsKey(j.Id) ? woCounts[j.Id] : 0,
                    finishedQty     = Math.Round(finished, 0),
                    dispatchedQty   = Math.Round(dispatched, 0),
                    outstandingQty  = Math.Round(outstanding, 0),
                    status          = rowStatus
                });
            }

            var summary = new CustomerTrackingKpiVm {
                totalOrders     = rows.Count,
                openCount       = rows.Count(r => r.status == "OPEN"),
                fullyDispatched = rows.Count(r => r.status == "DISPATCHED"),
                partialCount    = rows.Count(r => r.status == "PARTIAL" || r.status == "READY"),
                outstandingQty  = rows.Sum(r => r.outstandingQty)
            };

            return new CustomerTrackingReportVm { rows = rows, summary = summary };
        }

        // ====================================================================
        // SHARED HELPERS
        // ====================================================================
        public class FilterEcho { public string from { get; set; } public string to { get; set; } }

        private void ParseRange(string from, string to, out DateTime fromDt, out DateTime toDt)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out parsed))
                fromDt = parsed.Date;
            else
                fromDt = DateTime.Today.AddDays(-30);

            if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out parsed))
                toDt = parsed.Date.AddDays(1).AddTicks(-1);
            else
                toDt = DateTime.Today.AddDays(1).AddTicks(-1);
        }

        // Generic helper used by all four reports to keep everything on a
        // single worksheet so users don't have to switch tabs. Writes a
        // section title row, a header row, the data rows, and an optional
        // SUM totals row. Returns the next row index (with a blank gap).
        private int AppendReportSection(ExcelWorksheet ws, int startRow,
            string sectionTitle, string[] headers, List<object[]> dataRows,
            string[] columnFormats = null, int[] totalsColumns = null)
        {
            int sectionWidth = headers.Length;

            // Section title
            ws.Cells[startRow, 1, startRow, sectionWidth].Merge = true;
            ws.Cells[startRow, 1].Value = sectionTitle;
            ws.Cells[startRow, 1].Style.Font.Bold = true;
            ws.Cells[startRow, 1].Style.Font.Size = 12;
            ws.Cells[startRow, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            ws.Cells[startRow, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 244, 253));
            int row = startRow + 1;

            // Header row
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cells[row, c + 1].Value = headers[c];
                ws.Cells[row, c + 1].Style.Font.Bold = true;
                ws.Cells[row, c + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                ws.Cells[row, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                ws.Cells[row, c + 1].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }
            int firstDataRow = row + 1;
            row = firstDataRow;

            // Data
            foreach (var vals in dataRows)
            {
                for (int c = 0; c < vals.Length; c++)
                {
                    ws.Cells[row, c + 1].Value = vals[c];
                    if (columnFormats != null && c < columnFormats.Length && !string.IsNullOrEmpty(columnFormats[c]))
                        ws.Cells[row, c + 1].Style.Numberformat.Format = columnFormats[c];
                }
                row++;
            }
            int lastDataRow = row - 1;

            if (lastDataRow < firstDataRow)
            {
                ws.Cells[row, 1].Value = "(no data)";
                ws.Cells[row, 1].Style.Font.Italic = true;
                row++;
            }
            else if (totalsColumns != null && totalsColumns.Length > 0)
            {
                ws.Cells[row, 1].Value = "Total";
                foreach (int col in totalsColumns)
                {
                    string colLetter = ExcelColumnLetter(col);
                    ws.Cells[row, col].Formula = "SUM(" + colLetter + firstDataRow + ":" + colLetter + lastDataRow + ")";
                    if (columnFormats != null && col - 1 < columnFormats.Length && !string.IsNullOrEmpty(columnFormats[col - 1]))
                        ws.Cells[row, col].Style.Numberformat.Format = columnFormats[col - 1];
                }
                ws.Cells[row, 1, row, sectionWidth].Style.Font.Bold = true;
                ws.Cells[row, 1, row, sectionWidth].Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                row++;
            }

            return row + 1; // blank spacer row
        }

        private static string ExcelColumnLetter(int col)
        {
            string s = "";
            while (col > 0)
            {
                int rem = (col - 1) % 26;
                s = (char)('A' + rem) + s;
                col = (col - 1) / 26;
            }
            return s;
        }

        private void AddTitleAndFilters(Document doc, string title, string filtersLine)
        {
            var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.BLACK);
            var smallFont  = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 9, BaseColor.GRAY);

            var t = new Paragraph(title, titleFont) { Alignment = Element.ALIGN_CENTER };
            doc.Add(t);
            if (!string.IsNullOrWhiteSpace(filtersLine))
                doc.Add(new Paragraph(filtersLine, smallFont) { Alignment = Element.ALIGN_CENTER });
            doc.Add(new Paragraph(" "));
        }

        private void AddKpiTable(Document doc, (string label, string value)[] kpis)
        {
            var labelFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.GRAY);
            var valueFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.BLACK);

            int cols = Math.Min(5, kpis.Length);
            var t = new PdfPTable(cols) { WidthPercentage = 100f };
            foreach (var k in kpis)
            {
                var cell = new PdfPCell { Padding = 6f };
                cell.AddElement(new Paragraph(k.label, labelFont));
                cell.AddElement(new Paragraph(k.value, valueFont));
                t.AddCell(cell);
            }
            doc.Add(t);
            doc.Add(new Paragraph(" "));
        }

        private void AddPdfTable(Document doc, string heading, string[] headers, IEnumerable<string[]> rows)
        {
            var headFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.BLACK);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  BaseColor.WHITE);
            var bodyFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8.5f,    BaseColor.BLACK);

            doc.Add(new Paragraph(heading, headFont) { SpacingBefore = 6f });
            var t = new PdfPTable(headers.Length) { WidthPercentage = 100f, SpacingBefore = 4f };
            foreach (var h in headers)
            {
                t.AddCell(new PdfPCell(new Phrase(h, headerFont)) {
                    BackgroundColor = new BaseColor(13, 110, 253),
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 4f
                });
            }
            int rowCount = 0;
            foreach (var row in rows)
            {
                foreach (var cell in row)
                    t.AddCell(new PdfPCell(new Phrase(cell ?? "-", bodyFont)) { Padding = 3f });
                rowCount++;
            }
            if (rowCount == 0)
            {
                var c = new PdfPCell(new Phrase("(no rows)", bodyFont)) { Padding = 6f, Colspan = headers.Length, HorizontalAlignment = Element.ALIGN_CENTER };
                t.AddCell(c);
            }
            doc.Add(t);
        }

        private void AddPdfFooter(Document doc, int totalRows)
        {
            var f = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY);
            doc.Add(new Paragraph(" "));
            doc.Add(new Paragraph(
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | Rows: " + totalRows, f)
                { Alignment = Element.ALIGN_RIGHT });
        }
    }
}
