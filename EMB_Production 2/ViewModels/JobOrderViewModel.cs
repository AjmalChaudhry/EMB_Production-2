using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class JobOrderRowViewModel
    {
        public int Id { get; set; }
        public string JobOrderCode { get; set; }

        public string OrderType { get; set; }
        public bool IsCompanyStockOrder { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerCode { get; set; }

        public int? DesignId { get; set; }
        public string DesignName { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductCode { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int OrderQty { get; set; }
        public int RemainingQty { get; set; }

        // Sprint-1 progress fields (populated by List endpoint via a single
        // batched query). ProducedQty is sum of posted DailyProduction.AcceptedQty
        // across all WorkOrders linked to this JO. CompletionPercent is
        // ProducedQty / OrderQty × 100, capped at 100.
        public int ProducedQty { get; set; }
        public decimal CompletionPercent { get; set; }

        // Sprint-2 quick-jump counts (populated by List endpoint via batched
        // group-by queries). Each count powers a clickable badge that
        // navigates to the related list page pre-filtered by this JO code.
        public int WoCount { get; set; }
        public int ProductionPlanCount { get; set; }
        public int DispatchCount { get; set; }

        public DateTime? OrderDate { get; set; }
        public DateTime? DueDate { get; set; }
        // ISO yyyy-MM-dd serialisation of dates. MVC's default Json() helper
        // emits raw DateTime as /Date(ms)/, which Angular's date pipe and
        // `new Date()` reject. The frontend reads these text fields for
        // both display and the due-soon / overdue badge (and now falls back
        // to OrderDateText when DueDateText is empty).
        public string DueDateText { get; set; }
        public string OrderDateText { get; set; }

        public string Priority { get; set; }
        public string Status { get; set; }

        public bool IsPosted { get; set; }
        public int? PostedBy { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedDateText { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class JobOrderDetailViewModel
    {
        public int Id { get; set; }
        public string JobOrderCode { get; set; }

        public string OrderType { get; set; }
        public bool IsCompanyStockOrder { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerCode { get; set; }

        public int DesignId { get; set; }
        public string DesignName { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductCode { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public int OrderQty { get; set; }
        public int RemainingQty { get; set; }

        public DateTime OrderDate { get; set; }
        public DateTime? DueDate { get; set; }

        public string Priority { get; set; }
        public string Status { get; set; }

        public bool Active { get; set; }

        public bool IsPosted { get; set; }
        public int? PostedBy { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedDateText { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public string CreatedDateText { get; set; }

        public int? UpdatedByEmployeeId { get; set; }
        public string UpdatedDateText { get; set; }

        public List<JobOrderItemDetailViewModel> Items { get; set; }
            = new List<JobOrderItemDetailViewModel>();
    }

    public class JobOrderDto
    {
        public int Id { get; set; }
        public string JobOrderCode { get; set; }

        // Customer / CompanyStock
        public string OrderType { get; set; }
        public bool IsCompanyStockOrder { get; set; }

        // Required only for Customer order
        public int? CustomerId { get; set; }

        // Required for both Customer and CompanyStock
        public int ProductId { get; set; }
        public int DesignId { get; set; }

        public int? BranchId { get; set; }
        public int OrderQty { get; set; }

        public bool IsPosted { get; set; }
        public int? PostedBy { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedDateText { get; set; }

        public string Priority { get; set; }
        public string Status { get; set; }

        public bool Active { get; set; }
    }
}