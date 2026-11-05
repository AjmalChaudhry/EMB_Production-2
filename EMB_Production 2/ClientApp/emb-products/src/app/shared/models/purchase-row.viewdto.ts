export interface PurchaseRowViewModel {
  id: number;

  purchaseDateText: string;
  /** Sprint-1: ISO date used for client-side "received recently" badge
   *  and consistent sorting. */
  purchaseDate?: string;
  invoiceNo: string;
  /** Sprint-1: distinguishes "no invoice on file" from a typed `-`. */
  hasInvoice?: boolean;

  branchId: number;
  branchName: string;

  supplierId?: number | null;
  supplierName: string;

  /** Sprint-1: PO source — populated when this GRN was created via
   *  "Receive Against PO". Drives the clickable badge in the list. */
  purchaseOrderId?: number | null;
  poCode?: string | null;

  itemDisplay: string;
  itemTooltip: string;
  itemCount: number;

  totalQty: number;
  totalAmount: number;

  isPosted: boolean;
  active: boolean;

  /** Sprint-1: canonical status string (DRAFT / POSTED / REVERSED).
   *  REVERSED is the derived sibling of DRAFT — both have IsPosted=false
   *  but REVERSED came back from POSTED. */
  status?: string;
  isReversed?: boolean;
}

export interface PurchaseDetailItemViewModel {
  id: number;
  itemType: string;

  itemId: number;
  itemName: string;

  qty: number;
  rate: number;
  amount: number;

  isPosted: boolean;
  active: boolean;

  remarks?: string | null;
}

export interface PurchaseLedgerEntryViewModel {
  id: number;
  transactionDateText: string;

  itemId: number;
  itemName: string;
  unit?: string | null;

  transactionType: string;
  referenceType: string;
  referenceNo?: string | null;

  openingQty: number;
  inQty: number;
  outQty: number;
  currentQty: number;

  rate: number;
  amount: number;

  remarks?: string | null;
  active: boolean;
}

export interface PurchaseDetailViewModel {
  id: number;

  purchaseDate: string;
  purchaseDateText: string;

  invoiceNo: string;
  remarks?: string | null;
  hasInvoice?: boolean;

  supplierId?: number | null;
  supplierName: string;

  /** Sprint-1: clickable PO-source badge in the detail drawer. */
  purchaseOrderId?: number | null;
  poCode?: string | null;

  branchId: number;
  branchName: string;

  totalQty: number;
  totalAmount: number;

  isPosted: boolean;
  active: boolean;

  /** Sprint-1: canonical status string + reversal-derived flag. */
  status?: string;
  isReversed?: boolean;

  createdByUserId?: number | null;
  createdByEmployeeCode?: string | null;
  createdDate?: string | null;

  updatedByUserId?: number | null;
  updatedByEmployeeCode?: string | null;
  updatedDate?: string | null;

  /** Sprint-2: posted-by audit fields shown in the workflow stepper
   *  tooltip and in the bottom-of-modal "Created / Updated / Posted by"
   *  audit panel. */
  postedByUserId?: number | null;
  postedByEmployeeCode?: string | null;
  postedDate?: string | null;

  /** Sprint-2: workflow stepper data. `workflowStages` is the canonical
   *  pipeline and `workflowStageIndex` says which stage is current. */
  workflowStages?: string[];
  workflowStageIndex?: number;

  items: PurchaseDetailItemViewModel[];
  ledger?: PurchaseLedgerEntryViewModel[];
}
