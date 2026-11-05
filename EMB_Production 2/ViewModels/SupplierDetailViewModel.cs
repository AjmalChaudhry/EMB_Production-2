using System;

namespace EMB_Production.ViewModels
{
    public class SupplierDetailViewModel
    {
        public int Id { get; set; }

        public string SupplierName { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedDate { get; set; }    // DETAIL VIEW — string
        public int? UpdatedBy { get; set; }
        public string UpdatedDate { get; set; }    // DETAIL VIEW — string
    }
}
