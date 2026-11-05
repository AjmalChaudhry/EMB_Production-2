export type JobOrderType = 'Customer' | 'CompanyStock';

// =========================
// ROW / LIST
// =========================
export interface JobOrderRowViewModel {
  id: number;
  jobOrderCode: string;

  orderType: JobOrderType;
  isCompanyStockOrder: boolean;

  customerId: number | null;
  customerName: string | null;
  customerCode: string | null;

  designId: number | null;
  designName: string | null;

  productId: number | null;
  productName: string | null;
  productCode: string | null;

  branchId: number | null;
  branchName: string | null;

  orderQty: number;
  remainingQty: number;

  // Sprint-1: production progress (sum of posted DailyProduction AcceptedQty
  // across this JO's WorkOrders; CompletionPercent capped at 100).
  producedQty?: number;
  completionPercent?: number;

  // Sprint-2: linked-document counts powering the quick-jump badges.
  woCount?: number;
  productionPlanCount?: number;
  dispatchCount?: number;

  orderDate: string | null;
  dueDate: string | null;
  // ISO yyyy-MM-dd strings. Prefer these for display + dueBadge() — the raw
  // orderDate / dueDate fields arrive as the legacy /Date(ms)/ format which
  // Angular's date pipe and `new Date()` cannot parse.
  dueDateText?: string | null;
  orderDateText?: string | null;

  priority: string | null;
  status: string | null;

  isPosted: boolean;
  postedBy: number | null;
  postedDate: string | null;
  postedDateText: string | null;

  active: boolean;
  deleted: boolean;

  createdBy: number | null;
  createdDate: string | null;
  createdDateText?: string | null;

  updatedBy: number | null;
  updatedDate: string | null;
  updatedDateText?: string | null;
}

// =========================
// DETAIL ITEM
// =========================
export interface JobOrderItemViewModel {
  id: number;

  componentId?: number | null;
  componentCode: string | null;
  componentName: string | null;

  reqRepeats: number | null;
  reqHead: number | null;
  panni: number | null;
  solving: number | null;
}

// =========================
// DETAIL
// =========================
export interface JobOrderDetailViewModel {
  id: number;
  jobOrderCode: string;

  orderType: JobOrderType;
  isCompanyStockOrder: boolean;

  customerId: number | null;
  customerName: string | null;
  customerCode: string | null;

  designId: number | null;
  designName: string | null;

  productId: number | null;
  productName: string | null;
  productCode: string | null;

  branchId: number | null;
  branchName: string | null;

  orderQty: number;
  remainingQty: number;

  isPosted: boolean;
  postedBy: number | null;
  postedDate: string | null;
  postedDateText: string | null;

  orderDate: string | null;
  dueDate: string | null;

  priority: string | null;
  status: string | null;

  active: boolean;
  deleted?: boolean;

  createdBy: number | null;
  createdByEmployeeId: number | null;
  createdDate: string | null;
  createdDateText: string | null;

  updatedBy: number | null;
  updatedByEmployeeId: number | null;
  updatedDate: string | null;
  updatedDateText: string | null;

  items: JobOrderItemViewModel[];
}

// =========================
// DTO / CREATE UPDATE
// =========================
export interface JobOrderDto {
  id?: number;
  jobOrderCode?: string | null;

  orderType: JobOrderType;
  isCompanyStockOrder: boolean;

  customerId: number | null;

  productId: number | null;
  designId: number | null;
  branchId: number | null;

  orderQty: number;

  orderDate: string | null;
  dueDate: string | null;

  priority: string | null;
  status: string | null;

  remarks?: string | null;

  active: boolean;
}
