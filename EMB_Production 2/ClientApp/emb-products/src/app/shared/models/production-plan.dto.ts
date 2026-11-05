export type PlanStatus = 'DRAFT' | 'APPROVED' | 'IN_PROGRESS' | 'CLOSED';

export interface PlanRow {
  id: number;
  planCode: string;
  branchId: number | null;
  branchName: string | null;
  year: number;
  weekNo: number;
  weekStartDate: string;
  weekEndDate: string;
  // Sprint-1: ISO yyyy-MM-dd date strings — the raw weekStartDate/EndDate
  // arrive as the legacy /Date(ms)/ format which Angular's date pipe rejects.
  // Use these for any display or `new Date(...)` usage on the client.
  weekStartDateText?: string | null;
  weekEndDateText?: string | null;
  status: PlanStatus;
  remarks: string | null;
  approvedBy: number | null;
  approvedByName: string | null;
  approvedDate: string | null;
  active: boolean | null;
  createdDate: string | null;
  updatedDate: string | null;
  itemCount: number;
  // Sprint-2 quick-jump counters: number of distinct JOs touched by this
  // plan's items, and number of items that have already produced a Work
  // Order. Both default to 0 server-side.
  joCount?: number;
  woCount?: number;
  // Sprint-1: server-side flag — true when this plan's (year, weekNo)
  // matches today's ISO week. UI highlights the row.
  isCurrentWeek?: boolean;
}

export interface PlanItemRow {
  id: number;
  planId: number;
  jobOrderId: number | null;
  jobOrderCode: string | null;
  isCompanyStockOrder: boolean;
  productId: number | null;
  productName: string | null;
  designId: number | null;
  designCode: string | null;
  designName: string | null;
  plannedQty: number;
  machineId: number | null;
  machineName: string | null;
  plannedStartDate: string | null;
  plannedEndDate: string | null;
  generatedWorkOrderId: number | null;
  generatedWorkOrderCode: string | null;
  status: string;
  remarks: string | null;
}

export interface BomRollupRow {
  threadId: number;
  rawItemId: number | null;
  code: string;
  name: string;
  color: string;
  unit: string;
  requiredQty: number;
  onHand: number;
  shortfall: number;
}

export interface CapacityRow {
  machineId: number | null;
  machineName: string | null;
  itemCount: number;
  totalPlannedQty: number;   // sum of PlannedQty across items on this machine (pieces scheduled)
  earliestStart: string | null;
  latestEnd: string | null;
  hasConflict: boolean;
  conflictDetails: string[];
}

export interface PlanDetail {
  header: PlanRow;
  items: PlanItemRow[];
  bom: BomRollupRow[];
  capacity: CapacityRow[];
}

export interface PlanSummary {
  count: number;
  draftCount: number;
  approvedCount: number;
  inProgressCount: number;
  closedCount: number;
}

export interface EligibleJobOrder {
  id: number;
  code: string;
  /** Draft JOs (isPosted=false) are now included so operators can
   *  schedule a JO they just created. The picker warns before Approve
   *  so unposted JOs still get finalised. */
  isPosted?: boolean;
  productId: number | null;
  productName: string;
  designId: number | null;
  designCode: string;
  orderQty: number;
  branchId: number | null;
  isCompanyStockOrder: boolean;
  customerName: string;
}

export interface MachineDropdownItem {
  id: number;
  code: string;
  name: string;
  display: string;
}

export interface PlanSavePayload {
  id?: number;
  planCode?: string;
  branchId?: number | null;
  year: number;
  weekNo: number;
  remarks?: string;
}

export interface PlanItemSavePayload {
  id?: number;
  planId: number;
  jobOrderId?: number | null;
  isCompanyStockOrder?: boolean;
  productId?: number | null;
  designId?: number | null;
  plannedQty: number;
  machineId?: number | null;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  remarks?: string;
}
