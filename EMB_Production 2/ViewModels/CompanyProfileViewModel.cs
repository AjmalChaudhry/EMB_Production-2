using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class CompanyProfileViewModel
    {
        public int CompanyID { get; set; }
        public string CompanyName { get; set; }
        public string NickName { get; set; }
        public string Email { get; set; }
        public string ContactNumber { get; set; }
        public string Address { get; set; }
        public string Approval { get; set; }
        public bool Active { get; set; }
        public string Logo { get; set; }

        // Audit fields (for Detail view only)
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public string DeletedBy { get; set; }
    }
}