using PagedList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class ServicesPageViewModel
    {
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? ServiceTypeFilter { get; set; }
        public int? BranchFilter { get; set; }

        // Paging
        public int Page { get; set; }
        public IPagedList<ServiceRowViewModel> Rows { get; set; }

        // Dropdown lists for filters and forms
        public SelectList ServiceTypeList { get; set; }
        public SelectList BranchList { get; set; }

        // Inline create/edit flags and models
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public ServiceRowViewModel NewService { get; set; }
        public ServiceRowViewModel EditService { get; set; }
    }
}