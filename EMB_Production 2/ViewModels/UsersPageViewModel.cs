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
    public class UsersPageViewModel
    {
        public string Search { get; set; }
        public string ActiveFilter { get; set; }

        public IPagedList<UserRowViewModel> Users { get; set; }

        // Create/Edit toggles
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        // Create/Edit payloads
        public Adm_Users NewUser { get; set; } = new Adm_Users();
        public Adm_Users EditUser { get; set; }

        // DROPDOWNS
        public IEnumerable<SelectListItem> RoleList { get; set; }
        public IEnumerable<SelectListItem> BranchList { get; set; }
        public IEnumerable<SelectListItem> CompanyList { get; set; }
    }

}