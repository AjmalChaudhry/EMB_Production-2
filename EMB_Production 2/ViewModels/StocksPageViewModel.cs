using EMB_Production.Models;
using PagedList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class StocksPageViewModel
    {
        public StockDashboardViewModel Dashboard { get; set; }
        public List<StockRowViewModel> Rows { get; set; }

        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }

        public string Search { get; set; }
        public int? BranchId { get; set; }
    }
}