using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Controllers;
using EMB_Production.Models;
using static EMB_Production.ViewModels.MachineCostProfileViewModel;

[RoutePrefix("emb-products/machine-cost-profile")]
public class MachineCostProfileController : BaseController
{
    // ─────────────────────────────────────────────────────────────
    //  Sprint-2  AUDIT LOG (Machine Cost Profile)
    // ─────────────────────────────────────────────────────────────
    private void LogMcpAudit(int id, string action,
                             string oldValue = null, string newValue = null,
                             string detail = null)
    {
        try
        {
            db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.MachineCostProfileAuditLog
       (McpId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@mid, @act, @uid, @when, @ov, @nv, @det)",
                new SqlParameter("@mid",  id),
                new SqlParameter("@act",  action ?? ""),
                new SqlParameter("@uid",  GetSafeCurrentUserId()),
                new SqlParameter("@when", DateTime.Now),
                new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
        }
        catch { }
    }

    [HttpGet, Route("audit-log/{id:int}")]
    public ActionResult AuditLog(int id)
    {
        try
        {
            var entries = db.Database.SqlQuery<McpAuditEntry>(@"
SELECT  a.Id, a.McpId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.MachineCostProfileAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.McpId = @id
 ORDER BY a.Id DESC",
                new SqlParameter("@id", id)).ToList();

            var dto = entries.Select(e => new
            {
                id = e.Id, mcpId = e.McpId, action = e.Action,
                changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                changedDate = e.ChangedDate,
                changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
            }).ToList();
            return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Failed to load audit log. " + DescribeMcpError(ex) });
        }
    }

    private class McpAuditEntry
    {
        public int Id { get; set; }
        public int McpId { get; set; }
        public string Action { get; set; }
        public int? ChangedBy { get; set; }
        public string ChangedByName { get; set; }
        public DateTime? ChangedDate { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Detail { get; set; }
    }

    private static string DescribeMcpError(Exception ex)
    {
        if (ex == null) return "Unknown error.";
        var dbValEx = ex as DbEntityValidationException;
        if (dbValEx != null)
        {
            var firstError = dbValEx.EntityValidationErrors
                .SelectMany(e => e.ValidationErrors).FirstOrDefault();
            if (firstError != null)
                return firstError.PropertyName + ": " + firstError.ErrorMessage;
        }
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // ─────────────────────────────────────────────────────────────
    //  Sprint-3  BULK ACTIONS (Machine Cost Profile)
    // ─────────────────────────────────────────────────────────────
    public class McpBulkIdsDto { public List<int> Ids { get; set; } }

    private object DoBulkMcp(List<int> ids, Func<int, MachineCostProfile, string> runOne)
    {
        var failures = new List<object>();
        int ok = 0;
        foreach (var id in (ids ?? new List<int>()).Distinct())
        {
            var e = db.MachineCostProfile.FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (e == null)
            {
                failures.Add(new { id, code = "(missing)", reason = "Not found." });
                continue;
            }
            string code = "MCP#" + id;
            try
            {
                string err = runOne(id, e);
                if (string.IsNullOrEmpty(err)) ok++;
                else failures.Add(new { id, code, reason = err });
            }
            catch (Exception ex)
            {
                failures.Add(new { id, code, reason = DescribeMcpError(ex) });
            }
        }
        return new { successCount = ok, failureCount = failures.Count, failures };
    }

    [HttpPost, Route("bulk-activate")]
    public ActionResult BulkActivate(McpBulkIdsDto dto)
    {
        var data = DoBulkMcp(dto?.Ids, (id, e) =>
        {
            if (e.Active == true) return "Already active.";
            e.Active = true;
            db.SaveChanges();
            LogMcpAudit(id, "ACTIVATED", "Inactive", "Active");
            return null;
        });
        return Json(new { success = true, message = "Bulk activate completed.", data });
    }

    [HttpPost, Route("bulk-deactivate")]
    public ActionResult BulkDeactivate(McpBulkIdsDto dto)
    {
        var data = DoBulkMcp(dto?.Ids, (id, e) =>
        {
            if (e.Active != true) return "Already inactive.";
            e.Active = false;
            db.SaveChanges();
            LogMcpAudit(id, "DEACTIVATED", "Active", "Inactive");
            return null;
        });
        return Json(new { success = true, message = "Bulk deactivate completed.", data });
    }

    [HttpPost, Route("bulk-delete")]
    public ActionResult BulkDelete(McpBulkIdsDto dto)
    {
        var data = DoBulkMcp(dto?.Ids, (id, e) =>
        {
            e.Deleted = true;
            e.Active = false;
            db.SaveChanges();
            LogMcpAudit(id, "DELETED", "Active", "Deleted");
            return null;
        });
        return Json(new { success = true, message = "Bulk delete completed.", data });
    }

    [HttpGet, Route("list")]
    public ActionResult List(int page = 1, int pageSize = 10, string search = "", string activeFilter = "", int? machineId = null)
    {
        var query =
            from m in db.MachineCostProfile
            join mach in db.Gen_Machines on m.MachineId equals mach.Id
            where m.Deleted != true
            select new
            {
                m.Id,
                m.MachineId,

                MachineCode = mach.Code,
                Heads = mach.HeadsNum,

                m.HourlyRate,
                m.PowerKW,
                m.StitchPerMinute,

                m.OperatorHourlyRate,
                m.ElectricityUnitRate,

                m.EffectiveFrom,
                m.Active
            };

        // Phase R.6 - filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(x => x.MachineCode.Contains(search));
        }
        if (activeFilter == "active")
            query = query.Where(x => x.Active == true);
        else if (activeFilter == "inactive")
            query = query.Where(x => x.Active == false || x.Active == null);
        if (machineId.HasValue && machineId.Value > 0)
            query = query.Where(x => x.MachineId == machineId.Value);

        int total = query.Count();

        // Phase R.6 - summary aggregates
        var summarySource = query.ToList();
        int activeCount = summarySource.Count(s => s.Active == true);
        int inactiveCount = summarySource.Count(s => s.Active != true);
        int distinctMachines = summarySource.Select(s => s.MachineId).Distinct().Count();
        decimal avgHourly = summarySource.Any() ? summarySource.Average(s => s.HourlyRate ?? 0) : 0m;
        decimal avgOperator = summarySource.Any() ? summarySource.Average(s => s.OperatorHourlyRate ?? 0) : 0m;
        decimal avgPower = summarySource.Any() ? summarySource.Average(s => s.PowerKW ?? 0) : 0m;
        int avgStitch = summarySource.Any() ? (int)summarySource.Average(s => s.StitchPerMinute ?? 0) : 0;
        decimal avgElectricityUnit = summarySource.Any() ? summarySource.Average(s => s.ElectricityUnitRate ?? 0) : 0m;

        decimal avgTotalHourly = summarySource.Any()
            ? summarySource.Average(s => (s.HourlyRate ?? 0) + (s.OperatorHourlyRate ?? 0) + (s.PowerKW ?? 0) * (s.ElectricityUnitRate ?? 0))
            : 0m;

        var summary = new
        {
            count = total,
            activeCount,
            inactiveCount,
            distinctMachines,
            avgHourly = Math.Round(avgHourly, 2),
            avgOperator = Math.Round(avgOperator, 2),
            avgPower = Math.Round(avgPower, 2),
            avgStitch,
            avgElectricityUnit = Math.Round(avgElectricityUnit, 2),
            avgTotalHourly = Math.Round(avgTotalHourly, 2)
        };

        var items = query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(x => new MachineCostProfileListRowViewModel
            {
                Id = x.Id,
                MachineId = x.MachineId,

                MachineCode = x.MachineCode + " (" + x.Heads + "H)",

                HourlyRate = x.HourlyRate,
                PowerKW = x.PowerKW,
                StitchPerMinute = x.StitchPerMinute,

                OperatorHourlyRate = x.OperatorHourlyRate,
                ElectricityUnitRate = x.ElectricityUnitRate,

                EffectiveFromText = x.EffectiveFrom.HasValue
                    ? x.EffectiveFrom.Value.ToString("yyyy-MM-dd")
                    : "",

                Active = x.Active
            })
            .ToList();

        return JsonSuccess("OK", new
        {
            items = items,
            summary,
            totalItems = total
        });
    }

    // ============================================================
    // Phase R.6 - PROFILE
    // ============================================================
    [HttpGet, Route("profile/{id:int}")]
    public ActionResult GetProfile(int id)
    {
        try
        {
            var c = db.MachineCostProfile.FirstOrDefault(x => x.Id == id);
            if (c == null) return JsonError("Cost profile not found.");

            var machine = db.Gen_Machines.FirstOrDefault(m => m.Id == c.MachineId);

            var hourly = c.HourlyRate ?? 0;
            var op = c.OperatorHourlyRate ?? 0;
            var power = c.PowerKW ?? 0;
            var elec = c.ElectricityUnitRate ?? 0;
            var spm = c.StitchPerMinute ?? 0;

            var electricHourly = power * elec;
            var totalHourly = hourly + op + electricHourly;
            var minuteRate = totalHourly / 60m;
            var stitchRate = spm > 0 ? totalHourly / (spm * 60m) : 0m;

            var header = new
            {
                id = c.Id,
                machineId = c.MachineId,
                machineCode = machine != null ? machine.Code : "",
                brandName = machine != null && machine.Gen_Brands != null ? machine.Gen_Brands.BrandName : "",
                heads = machine != null ? machine.HeadsNum : null,
                needles = machine != null ? machine.NeedlesNum : null,
                hourlyRate = hourly,
                powerKW = power,
                stitchPerMinute = spm,
                operatorHourlyRate = op,
                electricityUnitRate = elec,
                effectiveFromText = c.EffectiveFrom.HasValue ? c.EffectiveFrom.Value.ToString("dd-MMM-yyyy") : null,
                active = c.Active ?? false
            };

            var kpis = new
            {
                machineHourly = Math.Round(hourly, 2),
                operatorHourly = Math.Round(op, 2),
                electricHourly = Math.Round(electricHourly, 4),
                totalHourly = Math.Round(totalHourly, 2),
                minuteRate = Math.Round(minuteRate, 4),
                stitchRate = Math.Round(stitchRate, 6),
                stitchPerMinute = spm
            };

            // Capacity estimates
            var capacity = new
            {
                stitchPerHour = spm * 60,
                stitchPerShift8h = spm * 60 * 8,
                stitchPerShift12h = spm * 60 * 12
            };

            // Other cost profiles for the same machine (history)
            var siblings = (
                from s in db.MachineCostProfile
                where s.MachineId == c.MachineId
                   && s.Id != id
                   && (s.Deleted == false || s.Deleted == null)
                orderby s.Id descending
                select new
                {
                    s.Id,
                    s.HourlyRate,
                    s.PowerKW,
                    s.StitchPerMinute,
                    s.OperatorHourlyRate,
                    s.ElectricityUnitRate,
                    s.EffectiveFrom,
                    Active = s.Active ?? false
                }
            ).ToList().Select(s => new
            {
                s.Id,
                s.HourlyRate,
                s.PowerKW,
                s.StitchPerMinute,
                s.OperatorHourlyRate,
                s.ElectricityUnitRate,
                effectiveFromText = s.EffectiveFrom.HasValue ? s.EffectiveFrom.Value.ToString("dd-MMM-yyyy") : "",
                s.Active
            }).ToList();

            return JsonSuccess("OK", new
            {
                header,
                kpis,
                capacity,
                siblings
            });
        }
        catch (Exception ex)
        {
            return JsonError("Profile failed. " + ex.Message);
        }
    }

    [HttpGet, Route("detail/{id:int}")]
    public ActionResult Detail(int id)
    {
        var item = db.MachineCostProfile
            .FirstOrDefault(x => x.Id == id && x.Deleted != true);

        if (item == null)
            return JsonError("Record not found.");

        var machine = db.Gen_Machines
            .FirstOrDefault(x => x.Id == item.MachineId);

        var data = new MachineCostProfileDetailViewModel
        {
            Id = item.Id,
            MachineId = item.MachineId,
            MachineCode = machine?.Code ?? "",

            HourlyRate = item.HourlyRate,
            PowerKW = item.PowerKW,
            StitchPerMinute = item.StitchPerMinute,

            OperatorHourlyRate = item.OperatorHourlyRate,
            ElectricityUnitRate = item.ElectricityUnitRate,

            EffectiveFrom = item.EffectiveFrom,
            EffectiveFromText = item.EffectiveFrom.HasValue
                ? item.EffectiveFrom.Value.ToString("yyyy-MM-dd")
                : "",

            Active = item.Active
        };

        return JsonSuccess("OK", data);
    }

    /* ================= SAVE ================= */
    [HttpPost, Route("save")]
    public ActionResult Save(MachineCostProfile model)
    {
        try
        {
            if (model == null)
                return JsonError("Invalid data.");

            if (model.MachineId <= 0)
                return JsonError("Machine is required.");

            // ===============================
            // PREVENT DUPLICATE MACHINE PROFILE
            // ===============================
            bool exists = db.MachineCostProfile.Any(x =>
                x.MachineId == model.MachineId &&
                x.Id != model.Id &&
                x.Deleted != true);

            if (exists)
                return JsonError("Cost profile already exists for this machine.");

            // ===============================
            // CREATE
            // ===============================
            int savedId;
            bool isCreate;
            if (model.Id == 0)
            {
                isCreate = true;
                var entity = new MachineCostProfile
                {
                    MachineId = model.MachineId,

                    HourlyRate = model.HourlyRate ?? 0,
                    PowerKW = model.PowerKW ?? 0,
                    StitchPerMinute = model.StitchPerMinute ?? 0,

                    OperatorHourlyRate = model.OperatorHourlyRate ?? 0,
                    ElectricityUnitRate = model.ElectricityUnitRate ?? 0,

                    EffectiveFrom = model.EffectiveFrom,

                    Active = model.Active ?? true,
                    Deleted = false,
                };

                db.MachineCostProfile.Add(entity);
                db.SaveChanges();
                savedId = entity.Id;
            }
            else
            {
                isCreate = false;
                // ===============================
                // UPDATE
                // ===============================
                var existing = db.MachineCostProfile
                    .FirstOrDefault(x => x.Id == model.Id && x.Deleted != true);

                if (existing == null)
                    return JsonError("Record not found.");

                existing.HourlyRate = model.HourlyRate ?? 0;
                existing.PowerKW = model.PowerKW ?? 0;
                existing.StitchPerMinute = model.StitchPerMinute ?? 0;

                existing.OperatorHourlyRate = model.OperatorHourlyRate ?? 0;
                existing.ElectricityUnitRate = model.ElectricityUnitRate ?? 0;

                existing.EffectiveFrom = model.EffectiveFrom;

                existing.Active = model.Active ?? true;

                db.SaveChanges();
                savedId = existing.Id;
            }

            LogMcpAudit(savedId, isCreate ? "CREATED" : "EDITED",
                oldValue: null, newValue: (model.Active ?? true) ? "Active" : "Inactive",
                detail: "Machine #" + model.MachineId
                      + " · Hourly " + (model.HourlyRate ?? 0)
                      + " · KW " + (model.PowerKW ?? 0)
                      + " · St/min " + (model.StitchPerMinute ?? 0));

            return JsonSuccess("Machine cost profile saved successfully.");
        }
        catch (Exception ex)
        {
            return JsonError("Save failed. " + DescribeMcpError(ex));
        }
    }
    /* ================= DELETE ================= */
    [HttpPost, Route("delete/{id:int}")]
    public ActionResult Delete(int id)
    {
        try
        {
            var item = db.MachineCostProfile.FirstOrDefault(x => x.Id == id);
            if (item == null)
                return JsonError("Record not found.");

            item.Deleted = true;
            item.Active = false;
            db.SaveChanges();

            LogMcpAudit(id, "DELETED", oldValue: "Active", newValue: "Deleted");

            return JsonSuccess("Deleted successfully.");
        }
        catch (Exception ex)
        {
            return JsonError("Delete failed. " + DescribeMcpError(ex));
        }
    }
}
