using System.Web.Mvc;
using PagedList;
using EMB_Production.Models;
using System.Collections.Generic;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    public class BranchesPageViewModel
    {
        public IPagedList<BranchRowViewModel> Branches { get; set; }
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? CompanyFilter { get; set; }

        // ← Add this:
        public int? BranchId { get; set; }
        public IEnumerable<SelectListItem> BranchList { get; set; }
        public SelectList CompanyList { get; set; }

        public Branches NewBranch { get; set; }
        public Branches EditBranch { get; set; }
    }
}
