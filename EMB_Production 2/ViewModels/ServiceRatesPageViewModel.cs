using PagedList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class ServiceRatesPageViewModel
    {
        public IPagedList<ServiceRatesRowViewModel> Rows { get; set; }
        public ServiceRatesRowViewModel NewRate { get; set; }
        public ServiceRatesRowViewModel EditRate { get; set; }
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int EditId { get; set; }
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? BranchFilter { get; set; }

        public IEnumerable<SelectListItem> ServiceList { get; set; }
        public IEnumerable<SelectListItem> ProductList { get; set; }
        public IEnumerable<SelectListItem> BranchList { get; set; }
    }
}