using System.Web.Mvc;
using EMB_Production.Models;
using PagedList;

namespace EMB_Production.ViewModels
{
    public class JobOrdersPageViewModel
    {
        // List grid
        public IPagedList<JobOrderRowViewModel> Orders { get; set; }

        // Create / Edit
        public Gen_JobOrders NewOrder { get; set; }
        public Gen_JobOrders EditOrder { get; set; }

        // Dropdowns
        public SelectList CustomerList { get; set; }
        public SelectList DesignList { get; set; }
        public SelectList BranchList { get; set; }

        // UI State
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        // Filters
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
    }
}