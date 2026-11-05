export type EmrStatus = 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'REJECTED';
export type EmrReason = 'WASTE' | 'REWORK' | 'DAMAGE' | 'DESIGN_CHANGE' | 'SAMPLE' | 'OTHER';
export type EmrItemType = 'RAW' | 'THREAD';

export interface EmrRow {
  id: number;
  emrCode: string;
  workOrderId: number;
  workOrderCode: string;
  branchId: number | null;
  branchName: string | null;
  requestDate: string;
  reason: EmrReason | string;
  remarks: string | null;
  status: EmrStatus;
  requestedBy: number | null;
  requestedByName: string | null;
  submittedDate: string | null;
  approvedBy: number | null;
  approvedByName: string | null;
  approvedDate: string | null;
  rejectionReason: string | null;
  issueReferenceNo: string | null;
  active: boolean | null;
  createdDate: string | null;
  updatedDate: string | null;
}

export interface EmrItemRow {
  id: number;
  emrId: number;
  itemType: EmrItemType;
  rawItemId: number | null;
  threadId: number | null;
  itemName: string;
  itemCode: string;
  unitOfMeasure: string;
  qty: number;
  rate: number | null;
  amount: number | null;
  remarks: string | null;
}

export interface EmrDetail {
  header: EmrRow;
  items: EmrItemRow[];
}

export interface EmrSummary {
  count: number;
  draftCount: number;
  pendingCount: number;
  approvedCount: number;
  rejectedCount: number;
}

export interface EmrSavePayload {
  id?: number;
  emrCode?: string;
  workOrderId: number;
  reason: string;
  remarks?: string;
  items: EmrItemSavePayload[];
}

export interface EmrItemSavePayload {
  id?: number;
  itemType: EmrItemType;
  rawItemId?: number | null;
  threadId?: number | null;
  unitOfMeasure?: string;
  qty: number;
  rate?: number | null;
  remarks?: string;
}

export interface WorkOrderDropdownItem {
  id: number;
  code: string;
  productName: string;
  designCode: string;
  branchId: number | null;
  display: string;
}

export interface RawItemDropdown {
  id: number;
  name: string;
  type: string;
  unitId: number | null;
}

export interface ThreadDropdown {
  id: number;
  code: string;
  name: string;
  color: string;
  unit: string;
  rate: number | null;
  rawItemId: number | null;
}

export const EMR_REASONS: { value: EmrReason; label: string }[] = [
  { value: 'WASTE',          label: 'Waste' },
  { value: 'REWORK',         label: 'Rework' },
  { value: 'DAMAGE',         label: 'Damage' },
  { value: 'DESIGN_CHANGE',  label: 'Design Change' },
  { value: 'SAMPLE',         label: 'Sample / Trial' },
  { value: 'OTHER',          label: 'Other' },
];
