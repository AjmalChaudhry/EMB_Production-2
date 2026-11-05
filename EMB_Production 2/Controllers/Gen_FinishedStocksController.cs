using System;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/finished-stocks")]
    public class Gen_FinishedStocksController : BaseController
    {
        // =====================================================
        // LIST
        // =====================================================
        [AllowAnonymous]
        [HttpGet, Route("")]
        public ActionResult GetAll(string search = "")
        {
            var query = db.Gen_FinishedStocks
                .Where(x => x.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x =>
                    x.Gen_Products.Prod_Name.Contains(search));

            var list = query
                .OrderByDescending(x => x.Id)
                .Select(x => new FinishedStockRowViewModel
                {
                    Id = x.Id,
                    ProductName = x.Gen_Products.Prod_Name,
                    BranchName = x.Branches.Name,
                    Qty = x.Qty,
                    Unit = x.Unit,
                    IsReserved = x.IsReserved
                })
                .ToList();

            return JsonSuccess("Finished stock list loaded.", list);
        }

        // =====================================================
        // DETAIL
        // =====================================================
        [AllowAnonymous]
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var data = db.Gen_FinishedStocks
                .Where(x => x.Id == id && x.Deleted != true)
                .Select(x => new FinishedStockDetailViewModel
                {
                    Id = x.Id,
                    ProductName = x.Gen_Products.Prod_Name,
                    BranchName = x.Branches.Name,
                    Qty = x.Qty,
                    Unit = x.Unit,
                    SourceType = x.SourceType,
                    SourceId = x.SourceId,
                    CreatedDateText = x.CreatedDate.ToString("yyyy-MM-dd HH:mm")
                })
                .FirstOrDefault();

            if (data == null)
                return JsonError("Finished stock not found.");

            return JsonSuccess("Finished stock detail loaded.", data);
        }

        // =====================================================
        // RESERVE
        // =====================================================
        [HttpPost, Route("reserve")]
        public ActionResult Reserve(int id, decimal qty)
        {
            var stock = db.Gen_FinishedStocks
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (stock == null)
                return JsonError("Stock not found.");

            if (qty <= 0 || qty > stock.Qty)
                return JsonError("Invalid reserve quantity.");

            stock.IsReserved = true;
            stock.UpdatedBy = GetSafeCurrentUserId();
            stock.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            return JsonSuccess("Stock reserved successfully.");
        }

        // =====================================================
        // RELEASE
        // =====================================================
        [HttpPost, Route("release")]
        public ActionResult Release(int id)
        {
            var stock = db.Gen_FinishedStocks
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (stock == null)
                return JsonError("Stock not found.");

            stock.IsReserved = false;
            stock.UpdatedBy = GetSafeCurrentUserId();
            stock.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            return JsonSuccess("Reservation released successfully.");
        }

        // =====================================================
        // ADJUST (ADMIN / SYSTEM)
        // =====================================================
        [HttpPost, Route("adjust")]
        public ActionResult Adjust(int id, decimal qty, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return JsonError("Adjustment reason is required.");

            var stock = db.Gen_FinishedStocks
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (stock == null)
                return JsonError("Stock not found.");

            stock.Qty += qty;
            stock.SourceType = "ADJUSTMENT";
            stock.UpdatedBy = GetSafeCurrentUserId();
            stock.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            return JsonSuccess("Stock adjusted successfully.");
        }
    }
}
