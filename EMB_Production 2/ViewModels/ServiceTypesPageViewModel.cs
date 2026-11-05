using PagedList;
using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class ServiceTypesPageViewModel
    {
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? BranchFilter { get; set; }

        public int Page { get; set; }
        public IPagedList<ServiceTypeRowViewModel> Rows { get; set; }

        // dropdowns
        public SelectList BranchList { get; set; }

        // inline forms (if needed)
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public ServiceTypeRowViewModel NewType { get; set; }
        public ServiceTypeRowViewModel EditType { get; set; }
    }
}
