using System;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.ViewModels;
using System.Data.Entity;
using iTextSharp.text.pdf;
using iTextSharp.text;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using EMB_Production.Models;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/daily-production-stops")]
    public class DailyProductionStopsController : BaseController
    {
        private static readonly DateTime SqlMax = new DateTime(9999, 12, 31);

        // -------------------------------
        // LIST (paged)
        // -------------------------------
        [HttpGet, Route("list")]
        public ActionResult List(int? productionId = null, string search = "", int page = 1, int pageSize = 10)
        {
            var q = db.DailyProductionStops.AsQueryable();

            if (productionId.HasValue && productionId.Value > 0)
                q = q.Where(x => x.ProductionId == productionId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                q = q.Where(x => (x.Reason != null && x.Reason.Contains(search)));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var total = q.Count();

            // ✅ EF-safe load first
            var entities = q
                .OrderByDescending(x => x.StopStart)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // ✅ map in memory (NO EF translate issues)
            var items = entities.Select(x =>
            {
                var end = x.StopEnd ?? DateTime.Now;
                var minutes = (int)Math.Max(0, (end - x.StopStart).TotalMinutes);

                return new DailyProductionStopRowViewModel
                {
                    Id = x.Id,
                    ProductionId = x.ProductionId,
                    ProductionLabel = "DP-" + x.ProductionId,
                    StopStart = x.StopStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                    StopEnd = x.StopEnd.HasValue? x.StopEnd.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null,
                    Reason = x.Reason ?? "",
                    CreatedDate = x.CreatedDate.HasValue? x.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null,
                    DurationMinutes = minutes
                };
            }).ToList();

            return Json(new
            {
                success = true,
                message = "Loaded successfully.",
                data = new
                {
                    items,
                    totalItems = total,
                    total,
                    page,
                    pageSize
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // -------------------------------
        // DETAIL
        // -------------------------------
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var x = db.DailyProductionStops.FirstOrDefault(z => z.Id == id);
            if (x == null)
                return Json(new { success = false, message = "Not found." }, JsonRequestBehavior.AllowGet);

            var end = x.StopEnd ?? DateTime.Now;
            var minutes = (int)Math.Max(0, (end - x.StopStart).TotalMinutes);

            var vm = new DailyProductionStopDetailViewModel
            {
                Id = x.Id,
                ProductionId = x.ProductionId,
                ProductionLabel = "DP-" + x.ProductionId,
                StopStart = x.StopStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                StopEnd = x.StopEnd.HasValue? x.StopEnd.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null,
                Reason = x.Reason ?? "",
                CreatedDate = x.CreatedDate.HasValue? x.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null,
                DurationMinutes = minutes,
                IsOpenStop = x.StopEnd == null
            };

            return Json(new { success = true, message = "Loaded.", data = vm }, JsonRequestBehavior.AllowGet);
        }

        // -------------------------------
        // CREATE (no overlap)
        // -------------------------------
        [HttpPost, Route("create")]
        public ActionResult Create(DailyProductionStopInputViewModel dto)
        {
            if (dto == null)
                return Json(new { success = false, message = "Invalid payload." });

            if (dto.ProductionId <= 0)
                return Json(new { success = false, message = "Production is required." });

            if (string.IsNullOrWhiteSpace(dto.Reason))
                return Json(new { success = false, message = "Reason is required." });

            if (dto.StopEnd.HasValue && dto.StopEnd.Value <= dto.StopStart)
                return Json(new { success = false, message = "StopEnd must be greater than StopStart." });

            if (HasOverlap(dto.ProductionId, dto.StopStart, dto.StopEnd, null))
                return Json(new { success = false, message = "Stop time overlaps an existing stop for this production." });

            var entity = new DailyProductionStops()
            {
                ProductionId = dto.ProductionId,
                StopStart = dto.StopStart,
                StopEnd = dto.StopEnd,
                Reason = dto.Reason,
                CreatedDate = DateTime.Now
            };

            db.DailyProductionStops.Add(entity);
            db.SaveChanges();

            return Json(new { success = true, message = "Created successfully." });
        }

        // -------------------------------
        // UPDATE (no overlap)
        // -------------------------------
        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id, DailyProductionStopInputViewModel dto)
        {
            var entity = db.DailyProductionStops.FirstOrDefault(x => x.Id == id);
            if (entity == null)
                return Json(new { success = false, message = "Not found." });

            if (dto.ProductionId <= 0)
                return Json(new { success = false, message = "Production is required." });

            if (string.IsNullOrWhiteSpace(dto.Reason))
                return Json(new { success = false, message = "Reason is required." });

            if (dto.StopEnd.HasValue && dto.StopEnd.Value <= dto.StopStart)
                return Json(new { success = false, message = "StopEnd must be greater than StopStart." });

            if (HasOverlap(dto.ProductionId, dto.StopStart, dto.StopEnd, id))
                return Json(new { success = false, message = "Stop time overlaps an existing stop for this production." });

            entity.ProductionId = dto.ProductionId;
            entity.StopStart = dto.StopStart;
            entity.StopEnd = dto.StopEnd;
            entity.Reason = dto.Reason;

            db.SaveChanges();

            return Json(new { success = true, message = "Updated successfully." });
        }

        // -------------------------------
        // DELETE
        // -------------------------------
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            var entity = db.DailyProductionStops.FirstOrDefault(x => x.Id == id);
            if (entity == null)
                return Json(new { success = false, message = "Not found." });

            db.DailyProductionStops.Remove(entity);
            db.SaveChanges();

            return Json(new { success = true, message = "Deleted successfully." });
        }

        // -------------------------------
        // ANALYTICS (total + by reason)
        // -------------------------------
        [HttpGet, Route("analytics")]
        public ActionResult Analytics(int? productionId = null)
        {
            var q = db.DailyProductionStops.AsQueryable();

            if (productionId.HasValue && productionId.Value > 0)
                q = q.Where(x => x.ProductionId == productionId.Value);

            // ✅ load first then compute durations in memory
            var list = q.ToList();

            var now = DateTime.Now;
            var totalMinutes = list.Sum(x =>
            {
                var end = x.StopEnd ?? now;
                return (int)Math.Max(0, (end - x.StopStart).TotalMinutes);
            });

            var stats = list
                .GroupBy(x => (x.Reason ?? "").Trim())
                .Select(g =>
                {
                    var mins = g.Sum(x =>
                    {
                        var end = x.StopEnd ?? now;
                        return (int)Math.Max(0, (end - x.StopStart).TotalMinutes);
                    });

                    return new DowntimeReasonStatVm
                    {
                        Reason = string.IsNullOrWhiteSpace(g.Key) ? "(No Reason)" : g.Key,
                        Minutes = mins,
                        Count = g.Count(),
                        Percent = totalMinutes <= 0 ? 0 : (mins * 100.0 / totalMinutes)
                    };
                })
                .OrderByDescending(x => x.Minutes)
                .ToList();

            return Json(new
            {
                success = true,
                data = new
                {
                    totalMinutes,
                    reasons = stats
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // -------------------------------
        // OVERLAP CHECK (DB-SAFE)
        // -------------------------------
        private bool HasOverlap(int productionId, DateTime start, DateTime? end, int? excludeId)
        {
            var newEnd = end ?? SqlMax;

            var q = db.DailyProductionStops.Where(s => s.ProductionId == productionId);

            if (excludeId.HasValue)
                q = q.Where(s => s.Id != excludeId.Value);

            // overlap rule:
            // existingStart < newEnd  AND  (existingEndOrMax > newStart)
            return q.Any(s =>
                s.StopStart < newEnd &&
                (s.StopEnd ?? SqlMax) > start
            );
        }

        private IQueryable<DailyProductionStops> BaseQuery()
        {
            return db.DailyProductionStops
                .Include("DailyProduction")
                .Where(x => x.DailyProduction.Deleted == false
                         || x.DailyProduction.Deleted == null);
        }


        [HttpGet]
        [Route("export-excel")]
        public FileResult ExportExcel(int? productionId = null, string search = "")
        {
            // Phase Q.2 - export the actual Stops table, NOT DailyProduction.
            var q = db.DailyProductionStops.AsQueryable();
            if (productionId.HasValue && productionId.Value > 0)
                q = q.Where(x => x.ProductionId == productionId.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(x => x.Reason != null && x.Reason.Contains(s));
            }

            var entities = q.OrderByDescending(x => x.StopStart).ToList();

            var rows = entities.Select(x =>
            {
                var end = x.StopEnd ?? DateTime.Now;
                var minutes = (int)Math.Max(0, (end - x.StopStart).TotalMinutes);
                return new
                {
                    Id = x.Id,
                    Production = "DP-" + x.ProductionId,
                    StopStart = x.StopStart.ToString("yyyy-MM-dd HH:mm"),
                    StopEnd = x.StopEnd.HasValue ? x.StopEnd.Value.ToString("yyyy-MM-dd HH:mm") : "(open)",
                    DurationMinutes = minutes,
                    Reason = x.Reason ?? "",
                    CreatedDate = x.CreatedDate.HasValue
                        ? x.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : ""
                };
            }).ToList();

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Production Stops");

                string[] headers = new[]
                {
                    "Id", "Production", "Stop Start", "Stop End", "Duration (min)", "Reason", "Created"
                };
                for (int c = 0; c < headers.Length; c++)
                    ws.Cells[1, c + 1].Value = headers[c];

                using (var hr = ws.Cells[1, 1, 1, headers.Length])
                {
                    hr.Style.Font.Bold = true;
                    hr.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hr.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                int r = 2;
                foreach (var item in rows)
                {
                    ws.Cells[r, 1].Value = item.Id;
                    ws.Cells[r, 2].Value = item.Production;
                    ws.Cells[r, 3].Value = item.StopStart;
                    ws.Cells[r, 4].Value = item.StopEnd;
                    ws.Cells[r, 5].Value = item.DurationMinutes;
                    ws.Cells[r, 6].Value = item.Reason;
                    ws.Cells[r, 7].Value = item.CreatedDate;
                    r++;
                }

                // Totals row (only if there are stops)
                if (rows.Count > 0)
                {
                    ws.Cells[r, 1].Value = "";
                    ws.Cells[r, 4].Value = "Total:";
                    ws.Cells[r, 5].Value = rows.Sum(x => x.DurationMinutes);
                    using (var tot = ws.Cells[r, 1, r, headers.Length])
                    {
                        tot.Style.Font.Bold = true;
                        tot.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    }
                }

                if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(
                    package.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "DailyProductionStops.xlsx"
                );
            }
        }


        // =====================================================
        // EXPORT PDF
        // =====================================================
        [HttpGet]
        [Route("export-pdf")]
        public ActionResult ExportPdf()
        {
            var data = BaseQuery().ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 20f, 20f, 20f, 20f);
                var writer = PdfWriter.GetInstance(doc, ms);

                doc.Open();

                var title = new Paragraph("Daily Production Stops Report",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16));
                title.Alignment = Element.ALIGN_CENTER;
                title.SpacingAfter = 10f;

                doc.Add(title);

                var table = new PdfPTable(5) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 15, 20, 20, 30, 15 });

                AddHeader(table, "Production");
                AddHeader(table, "Start");
                AddHeader(table, "End");
                AddHeader(table, "Reason");
                AddHeader(table, "Minutes");

                foreach (var x in data)
                {
                    table.AddCell("DP-" + x.ProductionId);
                    table.AddCell(x.StopStart.ToString("yyyy-MM-dd HH:mm"));
                    table.AddCell(x.StopEnd?.ToString("yyyy-MM-dd HH:mm") ?? "-");
                    table.AddCell(x.Reason);
                    table.AddCell(
                        x.StopEnd.HasValue
                            ? ((int)(x.StopEnd.Value - x.StopStart).TotalMinutes).ToString()
                            : "0"
                    );
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", "DailyProductionStops.pdf");
            }
        }

        private void AddHeader(PdfPTable table, string text)
        {
            var cell = new PdfPCell(new Phrase(text,
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LIGHT_GRAY,
                HorizontalAlignment = Element.ALIGN_CENTER
            };
            table.AddCell(cell);
        }



        [HttpGet]
        [Route("print")]
        public ActionResult Print()
        {
            var data = BaseQuery().ToList();

            var html = @"
                <html>
                <head>
                    <title>Daily Production Stops</title>
                    <style>
                        body { font-family: Arial; }
                        table { width:100%; border-collapse: collapse; }
                        th, td { border:1px solid #000; padding:6px; }
                        th { background:#f2f2f2; }
                    </style>
                </head>
                <body>
                <h2>Daily Production Stops</h2>
                <table>
                    <thead>
                        <tr>
                            <th>ID</th>
                            <th>Production</th>
                            <th>Start</th>
                            <th>End</th>
                            <th>Reason</th>
                            <th>Duration (Min)</th>
                        </tr>
                    </thead>
                    <tbody>";

            foreach (var x in data)
            {
                html += $@"
                    <tr>
                        <td>{x.Id}</td>
                        <td>DP-{x.ProductionId}</td>
                        <td>{x.StopStart:yyyy-MM-dd HH:mm}</td>
                        <td>{(x.StopEnd.HasValue ? x.StopEnd.Value.ToString("yyyy-MM-dd HH:mm") : "-")}</td>
                        <td>{x.Reason}</td>
                        <td>{(x.StopEnd.HasValue ? (int)(x.StopEnd.Value - x.StopStart).TotalMinutes : 0)}</td>
                    </tr>";
            }

            html += @"
                    </tbody>
                </table>
                <script>window.print();</script>
                </body>
                </html>";

            return Content(html, "text/html");
        }
    }
}

