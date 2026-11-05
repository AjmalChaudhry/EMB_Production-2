
using PagedList;
using EMB_Production.Models;
using System.Collections.Generic;
using System.Web.Mvc;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    public class BrandsPageViewModel
    {
        public IPagedList<Gen_Brands> Brands { get; set; }
        public Gen_Brands NewBrand { get; set; }
        public Gen_Brands EditBrand { get; set; }

        // ← ADD THESE
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }
        public string SearchString { get; set; }
        public string ActiveFilter { get; set; }

        public IEnumerable<SelectListItem> BranchList { get; set; }
    }
}
