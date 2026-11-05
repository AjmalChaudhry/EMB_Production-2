using System.Web.Mvc;
using PagedList;
using EMB_Production.Models;

namespace EMB_Production.ViewModels
{
    public class ComponentsPageViewModel
    {
        public IPagedList<ComponentsRowViewModel> Components { get; set; }

        public ComponentsRowViewModel NewComponent { get; set; }
        public ComponentsRowViewModel EditComponent { get; set; }

        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        // Filters and search
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? SelectedDesignId { get; set; }
        public SelectList BranchOptions { get; set; }
    }
}
