using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Infrastructure;
using EMB_Production.Models;


namespace EMB_Production.Controllers
{
    [RoutePrefix("api/adm-roles")]
    [SessionAuthorize]
    public class Adm_RolesController : Controller
    {
        private EMB_ProductionEntities1 db = new EMB_ProductionEntities1();

        [HttpGet, Route("")]
        public ActionResult GetAll()
        {
            try
            {
                var roles = db.Adm_Roles
                    .Where(r => r.Deleted != true)
                    .Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.AuthorityLevel,
                        r.WorkingCriteria,
                        r.Active,
                        r.CreatedDate,
                        r.UpdatedDate
                    })
                    .OrderByDescending(r => r.Id)
                    .ToList();

                return Json(new { success = true, data = roles }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // =======================
        // GET: api/adm-roles/detail/5
        // =======================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var r = (from role in db.Adm_Roles
                         where role.Id == id && role.Deleted != true
                         select new
                         {
                             role.Id,
                             role.Name,
                             role.AuthorityLevel,
                             role.WorkingCriteria,
                             role.Active,

                             // 🔹 Audit Fields
                             role.CreatedBy,
                             CreatedByName = db.Adm_Users
                                 .Where(u => u.Id == role.CreatedBy)
                                 .Select(u => (u.FirstName + " " + u.LastName))
                                 .FirstOrDefault(),
                             role.CreatedDate,
                             role.UpdatedBy,
                             UpdatedByName = db.Adm_Users
                                 .Where(u => u.Id == role.UpdatedBy)
                                 .Select(u => (u.FirstName + " " + u.LastName))
                                 .FirstOrDefault(),
                             role.UpdatedDate
                         }).FirstOrDefault();

                if (r == null)
                    return Json(new { success = false, message = "Role not found." }, JsonRequestBehavior.AllowGet);

                return Json(new { success = true, data = r }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // =======================
        // POST: api/adm-roles
        // =======================
        [HttpPost, Route("")]
        public ActionResult Create(Adm_Roles model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Name))
                    return Json(new { success = false, message = "Role name is required." });

                model.Active = true;
                model.Deleted = false;
                model.CreatedBy = 1; // replace with logged-in user ID if available
                model.CreatedDate = DateTime.Now;

                db.Adm_Roles.Add(model);
                db.SaveChanges();

                return Json(new { success = true, message = "Role created successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =======================
        // PUT: api/adm-roles/5
        // =======================
        [HttpPut, Route("{id:int}")]
        public ActionResult Update(int id, Adm_Roles model)
        {
            try
            {
                var role = db.Adm_Roles.FirstOrDefault(r => r.Id == id && r.Deleted != true);
                if (role == null)
                    return Json(new { success = false, message = "Role not found." });

                role.Name = model.Name;
                role.AuthorityLevel = model.AuthorityLevel;
                role.WorkingCriteria = model.WorkingCriteria;
                role.Active = model.Active;
                role.UpdatedBy = 1; // replace with logged-in user ID
                role.UpdatedDate = DateTime.Now;

                db.SaveChanges();
                return Json(new { success = true, message = "Role updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =======================
        // DELETE: api/adm-roles/5
        // =======================
        [HttpDelete, Route("{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var role = db.Adm_Roles.FirstOrDefault(r => r.Id == id && r.Deleted != true);
                if (role == null)
                    return Json(new { success = false, message = "Role not found." });

                role.Deleted = true;
                role.UpdatedBy = 1; // replace with logged-in user ID
                role.UpdatedDate = DateTime.Now;

                db.SaveChanges();
                return Json(new { success = true, message = "Role deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}