using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class StockLedgerRowViewModel
    {
        public DateTime? Date { get; set; }

        public int? ItemId { get; set; }
        public string ItemName { get; set; }
        public string ItemType { get; set; }

        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }

        public decimal Balance { get; set; }

        public string Description { get; set; }
    }
}
