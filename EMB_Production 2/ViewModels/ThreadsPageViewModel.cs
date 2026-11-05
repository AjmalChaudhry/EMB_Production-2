
using PagedList;
using System;

using System.Web.Mvc;

namespace EMB_Production.ViewModels
{
    public class ThreadsPageViewModel
    {
        public IPagedList<ThreadRowViewModel> Threads { get; set; }

        // ✅ CORRECT TYPES
        public ThreadFormViewModel NewThread { get; set; }
        public ThreadFormViewModel EditThread { get; set; }

        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        public string Search { get; set; }
        public string ActiveFilter { get; set; }

        // dropdowns
        public SelectList BranchList { get; set; }
        public SelectList SupplierList { get; set; }
    }

}