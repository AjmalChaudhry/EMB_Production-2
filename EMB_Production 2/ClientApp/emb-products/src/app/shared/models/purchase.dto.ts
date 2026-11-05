export interface PurchaseItemDto {
  itemId: number;
  itemType: string;
  qty: number;
  rate: number;
  remarks?: string | null;
  // Optional link back to PurchaseOrderItems.Id. Set when this GRN line
  // satisfies a specific PO line — drives ReceivedQty rollup on the PO.
  poItemId?: number | null;
}

export interface PurchaseDto {
  id?: number | null;
  purchaseDate: string;
  branchId: number;
  supplierId?: number | null;
  invoiceNo: string;
  remarks: string;
  items: PurchaseItemDto[];
  // Optional link to PurchaseOrder.Id when this Purchase is a GRN
  // against a specific PO. Triggers /sync-receipts after save.
  purchaseOrderId?: number | null;
}

// Payload returned by /emb-products/purchase/prefill-from-po/{poId}.
// One row per PO line still awaiting receipt (outstandingQty > 0).
export interface PoPrefillLine {
  poItemId: number;
  /** id the form's dropdown matches against — Gen_Threads.Id when itemType=THREAD, Gen_RawItems.Id otherwise. */
  itemId: number;
  /** Always the underlying Gen_RawItems.Id, regardless of itemType. */
  rawItemId: number;
  rawItemName: string;
  itemType: 'RAW' | 'FABRIC' | 'THREAD';
  unitId: number | null;
  unitCode: string | null;
  orderedQty: number;
  alreadyReceivedQty: number;
  outstandingQty: number;
  suggestedQty: number;
  rate: number;
  remarks: string;
}

export interface PoPrefillPayload {
  poId: number;
  poCode: string;
  supplierId: number;
  supplierName: string;
  branchId: number;
  branchName: string;
  purchaseDate: string;
  invoiceNo: string;
  remarks: string;
  items: PoPrefillLine[];
}
