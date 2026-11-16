using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class UserRowViewModel
    {
        public int Id { get; set; }
        public int? EmployeeId { get; set; }

        // Role
        public int? RoleId { get; set; }
        public string RoleName { get; set; }

        // User Info
        public string UserName { get; set; }
        public string FullName { get; set; }    // convenient for display
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }

        // Contact Info
        public string Landline { get; set; }
        public string LandlineExt { get; set; }
        public string MobilePrimary { get; set; }
        public string MobileSecondary { get; set; }

        // Authentication
        public DateTime? LastLoginDate { get; set; }
        public DateTime? LastPasswordChangeDate { get; set; }
        public bool? DashboardAccess { get; set; }

        // Sales / Designation
        public bool? IsSalesmanLinked { get; set; }
        public string SalesmanCode { get; set; }
        public int? DesignationId { get; set; }

        // Approval / Status
        public string Approvel { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }

        // Branch / Company
        public int? BranchId { get; set; }
        public string BranchName { get; set; }
        public int? CompanyId { get; set; }
        public string CompanyName { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }

        // Optional
        public string FacialRecognition { get; set; }
    }
}