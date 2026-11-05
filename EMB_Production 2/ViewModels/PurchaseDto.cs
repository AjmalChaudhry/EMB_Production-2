using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class PurchaseDto
    {
        public int? Id { get; set; }
        public string PurchaseDate { get; set; }

        public int BranchId { get; set; }
        public int? SupplierId { get; set; }

        public string InvoiceNo { get; set; }
        public string Remarks { get; set; }

        // PO linkage (optional). When set, this Purchase is treated as a GRN
        // against the named Purchase Order. After save, the PO controller's
        // /sync-receipts endpoint should be called to refresh per-line
        // ReceivedQty and PO header status.
        public int? PurchaseOrderId { get; set; }

        public List<PurchaseItemDto> Items { get; set; } = new List<PurchaseItemDto>();
    }

    public class PurchaseItemDto
    {
        public int ItemId { get; set; }
        public string ItemType { get; set; }

        public decimal Qty { get; set; }
        public decimal Rate { get; set; }

        public string Remarks { get; set; }

        // PO line linkage. When this GRN line satisfies a specific PO line,
        // PoItemId points at PurchaseOrderItems.Id. ReceivedQty rollup is
        // computed from these references.
        public int? PoItemId { get; set; }
    }
}