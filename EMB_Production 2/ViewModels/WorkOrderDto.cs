using System;

namespace EMB_Production.ViewModels
{
    public class WorkOrderDto
    {
        public int Id { get; set; }

        public int? JobOrderId { get; set; }
        public string JobOrderCode { get; set; }

        public string WorkOrderCode { get; set; }
        
        public string OrderType { get; set; }
        public bool IsCompanyStockOrder { get; set; }
       
        public int? BranchId { get; set; }
        public string BranchName { get; set; }       

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
       
        public int? DesignId { get; set; }
        public string DesignCode { get; set; }
        public string DesignName { get; set; }
       
        public int? ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        
        public int? OrderQty { get; set; }

        public string OrderDate { get; set; }
        public string DueDate { get; set; }

        public string ReferenceNo { get; set; }
        public string Remarks { get; set; }
        
        public string Status { get; set; }
        public bool Active { get; set; }

        public bool IsPosted { get; set; }

        // Production Plan gate.
        // A Work Order normally requires an APPROVED Production Plan that
        // references the same Job Order. Authorized roles (Administrator,
        // Manager) may override this by setting BypassPlan = true and
        // providing a non-empty BypassReason — used for rework, sample
        // runs, R&D, or any case where no plan was made. The reason is
        // appended to the Work Order's Remarks for audit traceability.
        public bool BypassPlan { get; set; }
        public string BypassReason { get; set; }

        public int? PostedBy { get; set; }
        public string PostedByName { get; set; }
        public string PostedDate { get; set; }
       
        public int? CreatedBy { get; set; }
        public string CreatedByName { get; set; }
        public string CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public string UpdatedByName { get; set; }
        public string UpdatedDate { get; set; }
    }
}