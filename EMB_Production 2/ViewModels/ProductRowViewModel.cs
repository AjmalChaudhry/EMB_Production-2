using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class ProductRowViewModel
    {
        public int Id { get; set; }
        public string CategoryName { get; set; }
        public string Prod_Name { get; set; }
        public string Prod_Code { get; set; }
        public int? Prod_Qty { get; set; }
        public decimal? Price { get; set; }
        public string BranchName { get; set; }
        public bool Active { get; set; }
    }

}