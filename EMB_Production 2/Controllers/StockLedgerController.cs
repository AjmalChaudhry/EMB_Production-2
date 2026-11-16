using EMB_Production.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/stock-ledger")]
    public class StockLedgerController : BaseController
    {
        // ========================= LIST =========================
        [HttpGet, Route("list")]
        public ActionResult List(
            int? itemId = null,
            string itemType = "",
            DateTime? from = null,
            DateTime? to = null)
        {
            var query = db.Gen_Stocks
                .Where(x => x.Deleted == false || x.Deleted == null)
                .Where(x => x.BranchId == GetCurrentBranchId());

            // Finished/product stock rows (Gen_StocksController's own mapping,
            // e.g. ProductId = s.ProdId ?? s.ItemId) key off ProdId rather than
            // ItemId, so filtering on ItemId alone returns an empty ledger for
            // a finished/produced item even though matching rows exist.
            if (itemId.HasValue)
                query = query.Where(x => x.ItemId == itemId.Value || x.ProdId == itemId.Value);

            if (!string.IsNullOrWhiteSpace(itemType))
                query = query.Where(x => x.ItemType == itemType);

            if (from.HasValue)
                query = query.Where(x => x.RecordTimeStamp >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.RecordTimeStamp <= to.Value);

            var raw = query
                .OrderBy(x => x.RecordTimeStamp)
                .ToList();

            // Running balance
            decimal running = 0;

            var list = raw.Select(x =>
            {
                running += (x.InQty ?? 0) - (x.OutQty ?? 0);

                return new StockLedgerRowViewModel
                {
                    Date = x.RecordTimeStamp,

                    ItemId = x.ItemId,
                    ItemName = x.Gen_Products != null
                        ? x.Gen_Products.Prod_Name
                        : "",

                    ItemType = x.ItemType,

                    InQty = x.InQty ?? 0,
                    OutQty = x.OutQty ?? 0,

                    Balance = running,

                    Description = x.StockDescription
                };
            }).ToList();

            return JsonSuccess("Loaded successfully.", list);
        }
    }
}
