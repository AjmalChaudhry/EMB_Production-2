using System.Web.Mvc;
using PagedList;
using EMB_Production.Models;
using System.Collections.Generic;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    public class CategoriesPageViewModel
    {
        public IPagedList<Gen_Catagories> Categories { get; set; }
        public Gen_Catagories NewCategory { get; set; }
        public Gen_Catagories EditCategory { get; set; }       
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public IEnumerable<SelectListItem> BrandList { get; set; }
        public IEnumerable<SelectListItem> BranchList { get; set; }
    }
}
