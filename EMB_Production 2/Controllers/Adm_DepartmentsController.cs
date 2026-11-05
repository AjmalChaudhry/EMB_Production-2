using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Models;


namespace EMB_Production.Controllers
{
    public class Adm_DepartmentsController : Controller
    {
        private EMB_ProductionEntities1 db = new EMB_ProductionEntities1();

        // GET: Adm_Departments
        public ActionResult Index()
        {
            return View(db.Adm_Departments.ToList());
        }

        // GET: Adm_Departments/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Adm_Departments adm_Departments = db.Adm_Departments.Find(id);
            if (adm_Departments == null)
            {
                return HttpNotFound();
            }
            return View(adm_Departments);
        }

        // GET: Adm_Departments/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: Adm_Departments/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "Id,DepartmentName,Active,Deleted")] Adm_Departments adm_Departments)
        {
            if (ModelState.IsValid)
            {
                db.Adm_Departments.Add(adm_Departments);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            return View(adm_Departments);
        }

        // GET: Adm_Departments/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Adm_Departments adm_Departments = db.Adm_Departments.Find(id);
            if (adm_Departments == null)
            {
                return HttpNotFound();
            }
            return View(adm_Departments);
        }

        // POST: Adm_Departments/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "Id,DepartmentName,Active,Deleted")] Adm_Departments adm_Departments)
        {
            if (ModelState.IsValid)
            {
                db.Entry(adm_Departments).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(adm_Departments);
        }

        // GET: Adm_Departments/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Adm_Departments adm_Departments = db.Adm_Departments.Find(id);
            if (adm_Departments == null)
            {
                return HttpNotFound();
            }
            return View(adm_Departments);
        }

        // POST: Adm_Departments/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Adm_Departments adm_Departments = db.Adm_Departments.Find(id);
            db.Adm_Departments.Remove(adm_Departments);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
