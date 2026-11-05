using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class FinishedStockRowViewModel
    {
        public int Id { get; set; }
        public string ProductName { get; set; }
        public string BranchName { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public bool IsPosted { get; set; }
        public bool IsReserved { get; set; }
    }

    public class FinishedStockDetailViewModel
    {
        public int Id { get; set; }
        public string ProductName { get; set; }
        public string BranchName { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public string CreatedDateText { get; set; }
    }
}
