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
    public class Gen_JobOrderItemsController : Controller
    {
        private EMB_ProductionEntities1 db = new EMB_ProductionEntities1();

        // GET: Gen_JobOrderItems
        public ActionResult Index()
        {
            var gen_JobOrderItems = db.Gen_JobOrderItems.Include(g => g.Gen_Components).Include(g => g.Gen_JobOrders);
            return View(gen_JobOrderItems.ToList());
        }

        // GET: Gen_JobOrderItems/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_JobOrderItems gen_JobOrderItems = db.Gen_JobOrderItems.Find(id);
            if (gen_JobOrderItems == null)
            {
                return HttpNotFound();
            }
            return View(gen_JobOrderItems);
        }

        // GET: Gen_JobOrderItems/Create
        public ActionResult Create()
        {
            ViewBag.CompId = new SelectList(db.Gen_Components, "Id", "Name");
            ViewBag.JobOrderId = new SelectList(db.Gen_JobOrders, "Id", "JobOrderCode");
            return View();
        }

        // POST: Gen_JobOrderItems/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "Id,JobOrderId,CompId,ReqRepeats,ReqHead,Panni,Solving,CreatedBy,CreatedDate,UpdatedBy,UpdatedDate,Active,Deleted,BranchId")] Gen_JobOrderItems gen_JobOrderItems)
        {
            if (ModelState.IsValid)
            {
                db.Gen_JobOrderItems.Add(gen_JobOrderItems);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.CompId = new SelectList(db.Gen_Components, "Id", "Name", gen_JobOrderItems.CompId);
            ViewBag.JobOrderId = new SelectList(db.Gen_JobOrders, "Id", "JobOrderCode", gen_JobOrderItems.JobOrderId);
            return View(gen_JobOrderItems);
        }

        // GET: Gen_JobOrderItems/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_JobOrderItems gen_JobOrderItems = db.Gen_JobOrderItems.Find(id);
            if (gen_JobOrderItems == null)
            {
                return HttpNotFound();
            }
            ViewBag.CompId = new SelectList(db.Gen_Components, "Id", "Name", gen_JobOrderItems.CompId);
            ViewBag.JobOrderId = new SelectList(db.Gen_JobOrders, "Id", "JobOrderCode", gen_JobOrderItems.JobOrderId);
            return View(gen_JobOrderItems);
        }

        // POST: Gen_JobOrderItems/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "Id,JobOrderId,CompId,ReqRepeats,ReqHead,Panni,Solving,CreatedBy,CreatedDate,UpdatedBy,UpdatedDate,Active,Deleted,BranchId")] Gen_JobOrderItems gen_JobOrderItems)
        {
            if (ModelState.IsValid)
            {
                db.Entry(gen_JobOrderItems).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.CompId = new SelectList(db.Gen_Components, "Id", "Name", gen_JobOrderItems.CompId);
            ViewBag.JobOrderId = new SelectList(db.Gen_JobOrders, "Id", "JobOrderCode", gen_JobOrderItems.JobOrderId);
            return View(gen_JobOrderItems);
        }

        // GET: Gen_JobOrderItems/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_JobOrderItems gen_JobOrderItems = db.Gen_JobOrderItems.Find(id);
            if (gen_JobOrderItems == null)
            {
                return HttpNotFound();
            }
            return View(gen_JobOrderItems);
        }

        // POST: Gen_JobOrderItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Gen_JobOrderItems gen_JobOrderItems = db.Gen_JobOrderItems.Find(id);
            db.Gen_JobOrderItems.Remove(gen_JobOrderItems);
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
