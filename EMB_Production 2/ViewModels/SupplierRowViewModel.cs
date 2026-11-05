using System;

namespace EMB_Production.ViewModels
{
    public class SupplierRowViewModel
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
        public DateTime? CreatedDate { get; set; }  // LIST VIEW — keep DateTime
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
