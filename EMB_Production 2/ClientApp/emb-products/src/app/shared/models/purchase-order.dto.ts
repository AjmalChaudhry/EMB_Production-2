export type PoStatus =
  'DRAFT' | 'SENT' | 'PARTIALLY_RECEIVED' | 'FULLY_RECEIVED' | 'CLOSED' | 'CANCELLED';

export interface PoRow {
  id: number;
  poCode: string;
  prId: number | null;
  prCode: string | null;
  branchId: number | null;
  branchName: string | null;
  supplierId: number;
  supplierName: string;
  orderDate: string;
  expectedDeliveryDate: string | null;
  status: PoStatus;
  totalAmount: number;
  receivedAmount: number;
  paymentTerms: string | null;
  deliveryTerms: string | null;
  remarks: string | null;
  sentBy: number | null;
  sentDate: string | null;
  closedDate: string | null;
  cancelledDate: string | null;
  cancellationReason: string | null;
  active: boolean | null;
  createdDate: string | null;
  updatedDate: string | null;

  // Sprint-1 enrichments — capped 0-100 progress and overdue-days hint
  // when the expected delivery date has slipped on an open PO.
  receivedPercent?: number;
  overdueDays?: number | null;
}

export interface PoItemRow {
  id: number;
  poId: number;
  prItemId: number | null;
  rawItemId: number;
  rawItemName: string;
  unitId: number | null;
  unitCode: string | null;
  orderedQty: number;
  rate: number;
  amount: number;
  receivedQty: number;
  remarks: string | null;
}

export interface GrnSummaryRow {
  id: number;
  grnDate: string;
  invoiceNo: string | null;
  isPosted: boolean;
  itemCount: number;
  amount: number | null;
}

export interface PoDetail {
  header: PoRow;
  items: PoItemRow[];
  grns: GrnSummaryRow[];
}

export interface PoSummary {
  count: number;
  draftCount: number;
  sentCount: number;
  partialCount: number;
  fullyReceivedCount: number;
  closedCount: number;
  cancelledCount: number;
  totalValue: number;
  receivedValue: number;
}

export interface PoUpdatePayload {
  id: number;
  expectedDeliveryDate?: string | null;
  paymentTerms?: string;
  deliveryTerms?: string;
  remarks?: string;
  items?: PoLineUpdatePayload[];
}
export interface PoLineUpdatePayload {
  id: number;
  orderedQty: number;
  rate: number;
  remarks?: string;
}
