using PagedList;
using EMB_Production.Models;
using System.Web.Mvc;
using System.Web;
using System.ComponentModel.DataAnnotations;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    // DTO for listing companies + user counts
    public class CompanyDto
    {
        public int CompanyID { get; set; }
        public string CompanyName { get; set; }
        public string Logo { get; set; }
        public string Email { get; set; }
        public string ContactNumber { get; set; }
        public string Address { get; set; }
        public int UserCount { get; set; }

        public bool ShowCurrencyName { get; set; }
        public bool StopLogin { get; set; }
        public bool TaxEnabled { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
    }

    public class CompanyPageViewModel
    {
        // Now page a list of CompanyDto, not the EF entity directly
        public IPagedList<CompanyDto> Companies { get; set; }

        // inline create/edit still binds to the EF entity
        public Adm_CompanyProfile NewCompany { get; set; } = new Adm_CompanyProfile();
        public Adm_CompanyProfile EditCompany { get; set; } = new Adm_CompanyProfile();

        [Display(Name = "Logo")]
        public HttpPostedFileBase LogoFile { get; set; }

        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }

        // Filters & search
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
        public int? ParentFilter { get; set; }
        public SelectList ParentList { get; set; }

        // site‐wide options
        public string[] ThemeOptions { get; set; }
        public string[] LanguageOptions { get; set; }
    }
}
