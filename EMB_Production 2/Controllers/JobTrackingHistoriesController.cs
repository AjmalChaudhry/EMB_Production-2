using System;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/job-tracking-history")]
    public class JobTrackingHistoryController : BaseController
    {       
        [HttpGet, Route("list")]
        public ActionResult ApiList(int workOrderItemId, int page = 1, int pageSize = 10)
        {
            try
            {
                var query = db.JobTrackingHistory
                    .Where(x => x.WorkOrderItemId == workOrderItemId && x.Deleted != true);

                var total = query.Count();

                var list = query
                    .OrderByDescending(x => x.StatusDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new JobTrackingHistoryRowViewModel
                    {
                        Id = x.Id,

                        WorkOrderItemId = x.WorkOrderItemId,
                        WorkOrderItemLabel = "WOI #" + x.WorkOrderItemId,

                        Status = x.Status,
                        StatusDate = x.StatusDate,   // ✅ DateTime only (NO StatusDateText)

                        Remarks = x.Remarks,

                        Active = x.Active,
                        Deleted = x.Deleted,

                        BranchId = x.BranchId,
                        BranchName = x.Branches != null
                            ? x.Branches.Name
                            : "",

                        CreatedBy = x.CreatedBy,
                        CreatedByName = x.Adm_Users != null
                            ? x.Adm_Users.EmployeeId.ToString()
                            : "",

                        CreatedDate = x.CreatedDate,

                        UpdatedBy = x.UpdatedBy,
                        UpdatedByName = x.Adm_Users1 != null
                            ? x.Adm_Users1.EmployeeId.ToString()
                            : "",

                        UpdatedDate = x.UpdatedDate
                    })
                        .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items = list,
                        total = total
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    message = "Failed to load job tracking history."
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ---------------------------
        // DETAIL
        // ---------------------------
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult ApiDetail(int id)
        {
            try
            {
                var x = db.JobTrackingHistory.FirstOrDefault(a => a.Id == id);
                if (x == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Record not found."
                    }, JsonRequestBehavior.AllowGet);
                }

                var vm = new JobTrackingHistoryDetailViewModel
                {
                    Id = x.Id,

                    // -------------------------
                    // WORK ORDER ITEM
                    // -------------------------
                    WorkOrderItemId = x.WorkOrderItemId,
                    WorkOrderItemLabel = x.WorkOrderItemId.HasValue
                        ? "WOI #" + x.WorkOrderItemId.Value
                        : "",

                    // -------------------------
                    // STATUS
                    // -------------------------
                    Status = x.Status,
                    StatusDate = x.StatusDate,
                    StatusDateText = x.StatusDate.ToString("yyyy-MM-dd HH:mm"),

                    Remarks = x.Remarks,

                    // -------------------------
                    // FLAGS
                    // -------------------------
                    Active = x.Active,
                    Deleted = x.Deleted,

                    // -------------------------
                    // BRANCH
                    // -------------------------
                    BranchId = x.BranchId,
                    BranchName = x.Branches != null ? x.Branches.Name : "",

                    // -------------------------
                    // AUDIT (DB STORES UserId)
                    // UI SHOWS EmployeeId
                    // -------------------------
                    CreatedBy = x.CreatedBy,
                    CreatedByName = GetEmployeeId(x.CreatedBy)?.ToString(),
                    CreatedDate = x.CreatedDate,
                    CreatedDateText = x.CreatedDate.HasValue
                        ? x.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : null,

                    UpdatedBy = x.UpdatedBy,
                    UpdatedByName = GetEmployeeId(x.UpdatedBy)?.ToString(),
                    UpdatedDate = x.UpdatedDate,
                    UpdatedDateText = x.UpdatedDate.HasValue
                        ? x.UpdatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : null
                };

                return Json(new
                {
                    success = true,
                    message = "Detail loaded.",
                    data = vm
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    message = "An unexpected error occurred."
                }, JsonRequestBehavior.AllowGet);
            }
        }


        // ---------------------------
        // CREATE
        // ---------------------------
        [HttpPost, Route("create")]
        public ActionResult ApiCreate(JobTrackingHistoryDto dto)
        {
            // ------------------------
            // VALIDATION
            // ------------------------
            if (dto == null || !dto.WorkOrderItemId.HasValue || dto.WorkOrderItemId <= 0)
                return Json(new { success = false, message = "Invalid WorkOrderItemId" });

            if (!db.WorkOrderItems.Any(x => x.Id == dto.WorkOrderItemId.Value))
                return Json(new { success = false, message = "WorkOrderItem not found" });

            if (string.IsNullOrWhiteSpace(dto.Status))
                return Json(new { success = false, message = "Status is required" });

            var userId = GetCurrentUserId();   // ✅ USER ID ONLY
            var now = DateTime.Now;

            // ------------------------
            // CREATE ENTITY
            // ------------------------
            var entity = new JobTrackingHistory
            {
                WorkOrderItemId = dto.WorkOrderItemId.Value,
                BranchId = dto.BranchId,

                Status = dto.Status.Trim(),
                StatusDate = SafeDate(dto.StatusDate),   // ✅ CRITICAL FIX
                Remarks = dto.Remarks,

                Active = dto.Active,
                Deleted = false,

                CreatedBy = userId,   // ✅ FK SAFE
                CreatedDate = now,
                UpdatedBy = userId,   // ✅ FK SAFE
                UpdatedDate = now
            };

            db.JobTrackingHistory.Add(entity);
            db.SaveChanges();

            return Json(new { success = true, message = "Created successfully" });
        }


        // ---------------------------
        // UPDATE
        // ---------------------------

        [HttpPost, Route("update")]
        public ActionResult ApiUpdate(JobTrackingHistoryDto dto)
        {
            if (dto == null || dto.Id <= 0)
                return Json(new { success = false, message = "Invalid payload." });

            var entity = db.JobTrackingHistory.FirstOrDefault(x => x.Id == dto.Id);
            if (entity == null)
                return Json(new { success = false, message = "Record not found." });

            // 🔒 FK MUST NEVER CHANGE
            if (!dto.WorkOrderItemId.HasValue || dto.WorkOrderItemId <= 0)
                return Json(new { success = false, message = "Invalid WorkOrderItemId." });

            entity.Status = dto.Status?.Trim();
            entity.StatusDate = SafeDate(dto.StatusDate);   // ✅ FIX
            entity.Remarks = dto.Remarks;
            entity.BranchId = dto.BranchId;
            entity.Active = dto.Active;

            entity.UpdatedBy = GetCurrentUserId();           // ✅ USER ID (FK)
            entity.UpdatedDate = DateTime.Now;               // ✅ ALWAYS SAFE

            db.SaveChanges();

            return Json(new { success = true, message = "Updated successfully." });
        }


        // ---------------------------
        // DELETE (SOFT)
        // ---------------------------
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult ApiDelete(int id)
        {
            try
            {
                var x = db.JobTrackingHistory.FirstOrDefault(a => a.Id == id);
                if (x == null)
                    return Json(new { success = false, message = "Not found." });

                var userId = GetCurrentUserId();   // ✅ MUST BE USER ID
                var now = DateTime.Now;

                x.Deleted = true;
                x.Active = false;

                x.UpdatedBy = userId;              // ✅ FK SAFE
                x.UpdatedDate = now;

                db.SaveChanges();

                return Json(new { success = true, message = "Deleted successfully." });
            }
            catch (Exception ex)
            {
                // 🔴 TEMP LOG (remove later)
                return Json(new
                {
                    success = false,
                    message = "Delete failed.",
                    error = ex.Message
                });
            }
        }


        private DateTime SafeDate(DateTime dt)
        {
            return dt < new DateTime(1753, 1, 1)
                ? DateTime.Now
                : dt;
        }


        // ---------------------------
        // Mapper
        // ---------------------------
        private JobTrackingHistoryRowViewModel MapRow(JobTrackingHistory x)
        {
            return new JobTrackingHistoryRowViewModel
            {
                Id = x.Id,
                WorkOrderItemId = x.WorkOrderItemId,
                WorkOrderItemLabel = x.WorkOrderItems != null ? "WOI #" + x.WorkOrderItems.Id : "",

                Status = x.Status,
                StatusDate = x.StatusDate,
                Remarks = x.Remarks,

                Active = x.Active,
                Deleted = x.Deleted,

                BranchId = x.BranchId,
                BranchName = x.Branches != null ? x.Branches.Name : "",

                CreatedBy = x.CreatedBy,
                CreatedByName = x.Adm_Users != null ? x.Adm_Users.EmployeeId.ToString() : "",
                CreatedDate = x.CreatedDate,

                UpdatedBy = x.UpdatedBy,
                UpdatedByName = x.Adm_Users1 != null ? x.Adm_Users1.EmployeeId.ToString() : "",
                UpdatedDate = x.UpdatedDate
            };
        }


        [HttpGet, Route("export/excel")]
        public ActionResult ExportExcel(int workOrderItemId)
        {
            var rows = db.JobTrackingHistory
                         .Where(x =>
                             x.WorkOrderItemId == workOrderItemId &&
                             (x.Deleted == false || x.Deleted == null))
                         .OrderBy(x => x.StatusDate)
                         .ToList();

            var csv = "Id,Status,StatusDate,Remarks,Branch,Active\r\n";

            foreach (var x in rows)
            {
                csv += string.Format("{0},\"{1}\",{2},\"{3}\",\"{4}\",{5}\r\n",
                    x.Id,
                    x.Status,
                    x.StatusDate.ToString("yyyy-MM-dd HH:mm"),
                    x.Remarks?.Replace("\"", "\"\""),
                    x.Branches != null ? x.Branches.Name : "",
                    x.Active == true ? "Yes" : "No"
                );
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

            return File(
                bytes,
                "text/csv",
                $"JobTrackingHistory_WOI_{workOrderItemId}.csv"
            );
        }
        [HttpGet, Route("export/pdf")]
        public ActionResult ExportPdf(int workOrderItemId)
        {
            var items = db.JobTrackingHistory
                          .Where(x =>
                              x.WorkOrderItemId == workOrderItemId &&
                              (x.Deleted == false || x.Deleted == null))
                          .OrderBy(x => x.StatusDate)
                          .ToList();

            var html = "<html><head><style>" +
                       "body{font-family:Arial;font-size:12px}" +
                       "table{width:100%;border-collapse:collapse}" +
                       "th,td{border:1px solid #ccc;padding:6px}" +
                       "th{background:#f2f2f2}" +
                       "</style></head><body>";

            html += $"<h2>Job Tracking History (WOI: {workOrderItemId})</h2>";
            html += "<table><thead><tr>" +
                    "<th>Id</th><th>Status</th><th>Date</th><th>Remarks</th><th>Branch</th><th>Active</th>" +
                    "</tr></thead><tbody>";

            foreach (var x in items)
            {
                html += "<tr>" +
                        $"<td>{x.Id}</td>" +
                        $"<td>{x.Status}</td>" +
                        $"<td>{x.StatusDate:yyyy-MM-dd HH:mm}</td>" +
                        $"<td>{x.Remarks}</td>" +
                        $"<td>{(x.Branches != null ? x.Branches.Name : "")}</td>" +
                        $"<td>{(x.Active == true ? "Yes" : "No")}</td>" +
                        "</tr>";
            }

            html += "</tbody></table></body></html>";

            return Content(html, "text/html");
        }
        [HttpGet, Route("export/print")]
        public ActionResult Print(int workOrderItemId)
        {
            var result = ExportPdf(workOrderItemId) as ContentResult;

            var printHtml = result.Content.Replace(
                "</body>",
                "<script>window.onload=function(){window.print();}</script></body>"
            );

            return Content(printHtml, "text/html");
        }

    }
}
