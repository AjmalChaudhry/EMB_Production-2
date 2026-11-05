export interface DropdownItem {
  id: number;
  text: string;
}

/* =======================================================
   STORE LIST / TRANSACTION ROW
======================================================= */

export interface StoreRowViewModel {
  id: number;

  itemId: number;
  itemName: string;

  branchId: number | null;
  branchName: string;

  warehouseId: number | null;
  warehouseName: string;

  transactionDate: string | null;

  transactionType: string;
  referenceType: string;
  referenceId: number | null;
  referenceNo: string;

  openingQty: number;
  inQty: number;
  outQty: number;
  currentQty: number;

  unit: string;
  rate: number;
  amount: number;

  remarks: string;

  active: boolean;
  deleted: boolean;

  createdDate: string | null;
  updatedDate: string | null;

  /** Sprint-1: Angular route slug for the source module ("purchase",
   *  "issue-materials", "dispatch", "qc-inspection", "store",
   *  "extra-material-requests"). Null when the ref-type is an
   *  internal-only marker (OPENING, OPENING_IN). Drives the clickable
   *  Reference badge in the ledger row. */
  referenceModule?: string | null;

  /** Sprint-1: row was created within the last 24h — drives the
   *  "received recently" lightning bolt badge on the ledger row. */
  isRecent?: boolean;
}

/* =======================================================
   STORE ITEM BALANCE SUMMARY
======================================================= */

export interface StoreItemBalanceViewModel {
  itemId: number;
  itemName: string;

  branchId: number | null;
  branchName: string;

  openingQty: number;
  totalIn: number;
  totalOut: number;
  currentBalance: number;

  unit: string;
  status: 'Available' | 'Empty' | 'Negative' | string;

  // Phase G.1 reorder thresholds (nullable when item has no levels set)
  reorderLevel?: number | null;
  maxLevel?: number | null;
  reorderQty?: number | null;
  stockStatus?: 'BELOW_REORDER' | 'OK' | 'ABOVE_MAX' | 'NO_LEVELS' | string;
}

/* =======================================================
   STORE DETAIL
======================================================= */

export interface StoreDetailViewModel extends StoreRowViewModel {
  createdBy: number | null;
  updatedBy: number | null;
}

/* =======================================================
   DASHBOARD / SUMMARY
======================================================= */

export interface StoreDashboardViewModel {
  openingQty: number;
  totalIn: number;
  totalOut: number;
  currentBalance: number;

  distinctItems: number;
  branches: number;

  totalStockValue?: number;
}

/* =======================================================
   STORE PAGE RESPONSE
======================================================= */

export interface StorePageViewModel {
  summary: StoreDashboardViewModel;
  itemBalances: StoreItemBalanceViewModel[];
  transactions: StoreRowViewModel[];

  items: StoreRowViewModel[];

  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

/* =======================================================
   OPENING IN / MANUAL ISSUE DTO
======================================================= */

export interface StoreEntryDto {
  itemId: number;

  branchId: number | null;
  warehouseId: number | null;

  transactionDate: string | null;

  transactionType: string;
  referenceType: string;
  referenceId: number | null;
  referenceNo: string;

  openingQty: number | null;
  inQty: number | null;
  outQty: number | null;

  unit: string;
  rate: number | null;

  remarks: string;

  active: boolean;
}

/* =======================================================
   WORK ORDER MATERIAL ISSUE
======================================================= */

export interface WorkOrderMaterialRequirementViewModel {
  itemId: number;
  itemType: string;
  itemName: string;

  requiredQty: number;
  availableQty: number;
  alreadyIssuedQty: number;
  pendingQty: number;

  issueQty: number;

  department: string;
  unit: string;

  rate: number;
  remarks: string;
}

export interface MaterialIssueDto {
  workOrderId: number;
  branchId: number;
  warehouseId: number | null;

  transactionDate: string | null;

  referenceNo: string;
  remarks: string;

  items: MaterialIssueItemDto[];
}

export interface MaterialIssueItemDto {
  itemId: number;
  itemType: string;

  issueQty: number;

  department: string;
  unit: string;

  rate: number;
  remarks: string;
}
