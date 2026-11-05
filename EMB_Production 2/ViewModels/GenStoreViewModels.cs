using System;
using System.Collections.Generic;

namespace EMB_Production.Models.ViewModels
{
    public class StoreRowViewModel
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
        public string ItemName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; }

        public DateTime? TransactionDate { get; set; }

        public string TransactionType { get; set; }
        public string ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string ReferenceNo { get; set; }

        public decimal OpeningQty { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal CurrentQty { get; set; }

        public string Unit { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }

        // Sprint-1: deep-link slug derived from ReferenceType so the
        // frontend can build a deterministic URL to the source module
        // without hard-coding the mapping in TypeScript. Examples:
        //   PURCHASE         → "purchase"
        //   MATERIAL_ISSUE   → "issue-materials"
        //   BRANCH_TRANSFER  → "store" (in-module)
        //   DISPATCH         → "dispatch"
        // Null when the ReferenceType isn't linkable (OPENING etc.).
        public string ReferenceModule { get; set; }

        // Sprint-1: posted within last 24h?  Drives the "recent" badge
        // on the ledger table — operators can spot fresh entries at a
        // glance without scanning the date column.
        public bool IsRecent { get; set; }
    }

    public class StoreDetailViewModel
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
        public string ItemName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; }

        public DateTime? TransactionDate { get; set; }

        public string TransactionType { get; set; }
        public string ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string ReferenceNo { get; set; }

        public decimal OpeningQty { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal CurrentQty { get; set; }

        public string Unit { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class StoreDashboardViewModel
    {
        public decimal TotalOpeningQty { get; set; }
        public decimal TotalInQty { get; set; }
        public decimal TotalOutQty { get; set; }
        public decimal TotalCurrentQty { get; set; }
        public decimal TotalStockValue { get; set; }

        public int DistinctItems { get; set; }
        public int DistinctBranches { get; set; }
    }

    public class StoreEntryDto
    {
        public int ItemId { get; set; }
        public int? BranchId { get; set; }
        public int? WarehouseId { get; set; }

        public DateTime? TransactionDate { get; set; }

        public string TransactionType { get; set; }
        public string ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string ReferenceNo { get; set; }

        public decimal? OpeningQty { get; set; }
        public decimal? InQty { get; set; }
        public decimal? OutQty { get; set; }

        public string Unit { get; set; }
        public decimal? Rate { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
    }

    // =========================================================
    // WORK ORDER MATERIAL REQUIREMENT ROW
    // Used when user selects Work Order in Store Material Issue
    // =========================================================
    public class WorkOrderMaterialRequirementViewModel
    {
        public int ItemId { get; set; }
        public string ItemType { get; set; }
        public string ItemName { get; set; }

        public decimal RequiredQty { get; set; }
        public decimal AvailableQty { get; set; }
        public decimal AlreadyIssuedQty { get; set; }
        public decimal PendingQty { get; set; }

        public decimal IssueQty { get; set; }

        public string Department { get; set; }
        public string Unit { get; set; }

        public decimal Rate { get; set; }
        public string Remarks { get; set; }
    }

    // =========================================================
    // MATERIAL ISSUE AGAINST WORK ORDER DTO
    // Posting this creates Gen_Store OUT rows
    // =========================================================
    public class MaterialIssueDto
    {
        public int WorkOrderId { get; set; }
        public int BranchId { get; set; }
        public int? WarehouseId { get; set; }

        public DateTime? TransactionDate { get; set; }

        public string ReferenceNo { get; set; }
        public string Remarks { get; set; }

        public List<MaterialIssueItemDto> Items { get; set; }

        public MaterialIssueDto()
        {
            Items = new List<MaterialIssueItemDto>();
        }
    }

    public class MaterialIssueItemDto
    {
        public int ItemId { get; set; }
        public string ItemType { get; set; }

        public decimal IssueQty { get; set; }

        public string Department { get; set; }
        public string Unit { get; set; }

        public decimal Rate { get; set; }
        public string Remarks { get; set; }
    }
}