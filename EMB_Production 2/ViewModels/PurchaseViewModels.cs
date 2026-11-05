using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class PurchaseRowViewModel
    {
        public int Id { get; set; }
        public string PurchaseDateText { get; set; }
        // Sprint-1: ISO date string for client-side recency / "received
        // within last N days" badges and sorting consistency.
        public string PurchaseDate { get; set; }
        public string InvoiceNo { get; set; }
        // Sprint-1: drives the "No invoice on file" placeholder + the
        // warning chip on Posted rows where invoice is missing
        // (data-quality flag for procurement managers).
        public bool HasInvoice { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public int? SupplierId { get; set; }
        public string SupplierName { get; set; }

        // Sprint-1: PO-source linkage. Populated when this GRN was
        // created via "Receive Against PO". Drives a clickable badge in
        // the list that deep-links to the PO detail drawer.
        public int? PurchaseOrderId { get; set; }
        public string PoCode { get; set; }

        public string ItemDisplay { get; set; }
        public string ItemTooltip { get; set; }
        // Total number of item lines on this invoice (after filtering out
        // unresolved items). Drives the "N items" badge on the list row.
        public int ItemCount { get; set; }

        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }

        public bool IsPosted { get; set; }
        public bool Active { get; set; }

        // Sprint-1: canonical status string. DRAFT / POSTED / REVERSED.
        // REVERSED is derived: a row that was once posted but whose
        // Gen_Store IN rows carry a [PUR-REV-N] tag in Remarks. The
        // schema doesn't store a flag; surfacing it here lets the list
        // chip distinguish "never posted" from "posted then reversed".
        public string Status { get; set; }
        public bool IsReversed { get; set; }
    }

    public class PurchaseDetailItemViewModel
    {
        public int Id { get; set; }
        public string ItemType { get; set; }

        public int ItemId { get; set; }
        public string ItemName { get; set; }

        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }

        public bool IsPosted { get; set; }
        public bool Active { get; set; }

        public string Remarks { get; set; }
    }

    public class PurchaseLedgerEntryViewModel
    {
        public int Id { get; set; }
        public string TransactionDateText { get; set; }

        public int ItemId { get; set; }
        public string ItemName { get; set; }
        public string Unit { get; set; }

        public string TransactionType { get; set; }
        public string ReferenceType { get; set; }
        public string ReferenceNo { get; set; }

        public decimal OpeningQty { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal CurrentQty { get; set; }

        public decimal Rate { get; set; }
        public decimal Amount { get; set; }

        public string Remarks { get; set; }
        public bool Active { get; set; }
    }

    public class PurchaseDetailViewModel
    {
        public int Id { get; set; }
        public string PurchaseDate { get; set; }
        public string PurchaseDateText { get; set; }

        public string InvoiceNo { get; set; }
        public string Remarks { get; set; }
        // Sprint-1: same "data quality" flag as the row VM — Posted GRN
        // without an invoice is a follow-up flag for procurement.
        public bool HasInvoice { get; set; }

        public int? SupplierId { get; set; }
        public string SupplierName { get; set; }

        // Sprint-1: PO-source linkage shown as a clickable badge in the
        // detail drawer header so the operator can hop straight to the
        // originating PO. Null when the GRN was entered ad-hoc.
        public int? PurchaseOrderId { get; set; }
        public string PoCode { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }

        public bool IsPosted { get; set; }
        public bool Active { get; set; }

        // Sprint-1: canonical status + reversal-derived flag.
        public string Status { get; set; }
        public bool IsReversed { get; set; }

        public int? CreatedByUserId { get; set; }
        public string CreatedByEmployeeCode { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedByUserId { get; set; }
        public string UpdatedByEmployeeCode { get; set; }
        public DateTime? UpdatedDate { get; set; }

        // Sprint-2: posted-by / reversed-by audit fields for the
        // workflow stepper. PostedBy/Date already live on Purchase;
        // ReversedBy/Date are derived from the Gen_Store reversal tag.
        public int? PostedByUserId { get; set; }
        public string PostedByEmployeeCode { get; set; }
        public DateTime? PostedDate { get; set; }

        // Sprint-2: workflow stepper data. Stages array is the canonical
        // pipeline; StageIndex tells the client which dot to colour as
        // "current". For a REVERSED row the StageIndex is set to the
        // REVERSED off-shoot rather than DRAFT — the stepper renders it
        // in red, sitting beside the green DRAFT/POSTED chain.
        public string[] WorkflowStages { get; set; }
        public int WorkflowStageIndex { get; set; }

        public List<PurchaseDetailItemViewModel> Items { get; set; } = new List<PurchaseDetailItemViewModel>();
        public List<PurchaseLedgerEntryViewModel> Ledger { get; set; } = new List<PurchaseLedgerEntryViewModel>();
    }
}