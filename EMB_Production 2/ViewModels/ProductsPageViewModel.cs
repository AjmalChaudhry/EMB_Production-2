using EMB_Production.Models;
using EMB_Production_2.Models;
using PagedList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class ProductsPageViewModel
    {
        public IPagedList<ProductRowViewModel> Products { get; set; }
        public Gen_Products NewProduct { get; set; }
        public Gen_Products EditProduct { get; set; }
        public SelectList CategoryList { get; set; }
        public SelectList BranchList { get; set; }
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
    }
}