using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class StocksPrintViewModel
    {
        public StockDashboardViewModel Dashboard { get; set; }
        public List<StockRowViewModel> Rows { get; set; }
    }
}