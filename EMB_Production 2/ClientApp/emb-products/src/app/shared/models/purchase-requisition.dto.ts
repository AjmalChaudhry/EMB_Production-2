export type PrStatus = 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'REJECTED' | 'CONVERTED_TO_PO';
// Sprint-1: widened to include the source types that WorkOrders Post and
// Production Plan Approve emit when they auto-generate a PR. These are
// what the screenshot shows in the Source column.
export type PrSourceType =
  | 'AD_HOC' | 'EMR_SHORTFALL' | 'PLAN_SHORTFALL' | 'PLAN_APPROVE' | 'WO_POST';

export interface PrRow {
  id: number;
  prCode: string;
  branchId: number | null;
  branchName: string | null;
  requestDate: string;
  supplierId: number | null;
  supplierName: string | null;
  sourceType: PrSourceType | string;
  sourceReferenceId: number | null;
  status: PrStatus;
  remarks: string | null;
  requestedBy: number | null;
  requestedByName: string | null;
  submittedDate: string | null;
  approvedBy: number | null;
  approvedByName: string | null;
  approvedDate: string | null;
  rejectionReason: string | null;
  convertedToPoId: number | null;
  convertedToPoCode: string | null;
  active: boolean | null;
  createdDate: string | null;
  updatedDate: string | null;

  // Sprint-1 enrichments — line count + total value per PR (sum of
  // EstimatedAmount, falling back to Qty × EstimatedRate when the row's
  // EstimatedAmount is NULL). Both default to 0 server-side.
  itemCount?: number;
  totalValue?: number;
}

export interface PrItemRow {
  id: number;
  prId: number;
  rawItemId: number;
  rawItemName: string;
  unitId: number | null;
  unitCode: string | null;
  qty: number;
  estimatedRate: number | null;
  estimatedAmount: number | null;
  remarks: string | null;
}

export interface PrDetail { header: PrRow; items: PrItemRow[]; }

export interface PrSummary {
  count: number;
  draftCount: number;
  submittedCount: number;
  approvedCount: number;
  rejectedCount: number;
}

export interface PrSavePayload {
  id?: number;
  prCode?: string;
  branchId?: number | null;
  supplierId?: number | null;
  sourceType?: string;
  sourceReferenceId?: number | null;
  remarks?: string;
  items: PrItemSavePayload[];
}

export interface PrItemSavePayload {
  id?: number;
  rawItemId: number;
  unitId?: number | null;
  qty: number;
  estimatedRate?: number | null;
  remarks?: string;
}

export interface SupplierDropdown { id: number; name: string; }
export interface RawItemDropdown  { id: number; name: string; type: string; unitId: number | null; }

// Row returned by /purchase-requisition/shortfall-suggestions.
// One per raw item whose current Gen_Store balance is below its ReorderLevel.
export interface ShortfallSuggestion {
  rawItemId: number;
  rawItemName: string;
  itemType: string | null;
  unitId: number | null;
  unitCode: string;
  currentBalance: number;
  reorderLevel: number;
  reorderQty: number | null;
  suggestedQty: number;
  estimatedRate: number | null;
  estimatedAmount: number | null;
  shortfall: number;
}

export const PR_SOURCE_TYPES: { value: PrSourceType; label: string }[] = [
  { value: 'AD_HOC',          label: 'Ad-hoc' },
  { value: 'EMR_SHORTFALL',   label: 'EMR Shortfall' },
  { value: 'PLAN_SHORTFALL',  label: 'Production Plan Shortfall' },
];
