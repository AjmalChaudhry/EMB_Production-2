
/* -------------------------------------------------------
   LIST ROW VIEW MODEL
--------------------------------------------------------*/
export interface WorkOrderRowViewModel {
  id: number;
  workOrderCode: string;

  orderType: 'Customer' | 'CompanyStock';
  isCompanyStockOrder: boolean;

  jobOrderId: number | null;
  jobOrderCode: string | null;

  customerId: number | null;
  customerName: string | null;

  productId: number | null;
  productCode: string | null;
  productName: string | null;
 
  designId: number | null;
  designCode: string | null;
  designName: string | null;

  branchId: number | null;
  branchName: string | null;

  orderQty: number | null;
  orderDate: string;
  dueDate: string | null;

  referenceNo: string | null;
  remarks: string | null;

  status: string | null;
  active: boolean;
  deleted: boolean | null;

  isPosted: boolean;
  postedBy: number | null;
  postedByEmployeeId: number | null;
  postedByName: string | null;
  postedDate: string | null;

  recordTimeStamp: string | null;
  createdDate: string | null;
  updatedDate: string | null;

  // Sprint-1: production progress (sum of posted DailyProduction.AcceptedQty
  // across this WO's WorkOrderItems; completionPercent capped at 100).
  producedQty?: number;
  completionPercent?: number;

  // Sprint-1: linked Production Plan when this WO was generated from one.
  // null for ad-hoc WOs created directly.
  planId?: number | null;
  planCode?: string | null;

  // Sprint-1: quick-jump counters powering the row badges.
  // Daily-Production count is per-WO via WorkOrderItems.
  // QC count is per-WO directly.
  // Dispatch count is per-JobOrder (Dispatch.JobOrderId), so it shows
  // the parent JO's dispatches.
  dailyProductionCount?: number;
  qcCount?: number;
  dispatchCount?: number;

  // Sprint-1: ISO yyyy-MM-dd date strings so Angular's date pipe and
  // `new Date(...)` don't choke on the legacy /Date(ms)/ format. Prefer
  // these for display and dueBadge() over the raw orderDate / dueDate.
  orderDateText?: string | null;
  dueDateText?: string | null;
}

/* -------------------------------------------------------
   DETAIL VIEW MODEL
--------------------------------------------------------*/
export interface WorkOrderDetailViewModel {
  id: number;

  workOrderCode: string;

  orderType: 'Customer' | 'CompanyStock';
  isCompanyStockOrder: boolean;

  jobOrderId: number | null;
  jobOrderCode: string | null;

  customerId: number | null;
  customerName: string | null;
  
  productId: number | null;
  productName: string | null;
  
  designId: number | null;
  designCode: string | null;
  designName: string | null;

  branchId: number | null;
  branchName: string | null;

  orderQty: number | null;
  orderDate: string;
  dueDate: string | null;

  referenceNo: string | null;
  remarks: string | null;

  status: string | null;
  active: boolean;
  deleted: boolean | null;

  isPosted: boolean;
  postedBy: number | null;
  postedByEmployeeId: number | null;
  postedByName: string | null;
  postedDate: string | null;

  recordTimeStamp: string | null;

  createdBy: number | null;
  updatedBy: number | null;

  createdByEmployeeId: number | null;
  updatedByEmployeeId: number | null;

  createdDate: string | null;
  updatedDate: string | null;
}


/* -------------------------------------------------------
   DTO (CREATE / UPDATE)
--------------------------------------------------------*/
export interface WorkOrderDto {
  id: number;
  workOrderCode: string;

  orderType: 'Customer' | 'CompanyStock';
  isCompanyStockOrder: boolean;

  jobOrderId: number | null;
  branchId: number | null;
  designId: number | null;
  orderQty: number | null;

  productId: number | null;
  customerId: number | null;

  orderDate: string;
  dueDate: string | null;

  referenceNo: string;
  remarks: string;

  status: string;
  active: boolean;

  isPosted?: boolean;

  // Production Plan gate override (Admin / Manager only on the wire)
  bypassPlan?: boolean;
  bypassReason?: string;
}

/* -------------------------------------------------------
   FORM MODEL (UI)
--------------------------------------------------------*/
export interface WorkOrderFormModel {
  id: number;
  workOrderCode: string;

  orderType: 'Customer' | 'CompanyStock';
  isCompanyStockOrder: boolean;

  jobOrderId: number | null;
  jobOrderCode: string;

  branchId: number | null;
  designId: number | null;
  orderQty: number;

  customerId: number | null;
  customerLabel: string;

  productId: number | null;
  productLabel: string;

  orderDate: string;
  dueDate: string;

  referenceNo: string;
  remarks: string;
  status: string;
  active: boolean;

  isPosted?: boolean;

  // Production Plan gate override — only honored by the backend when
  // the current user's role is Administrator or Manager. The reason
  // is required (non-empty) whenever bypassPlan is true; backend will
  // reject the request otherwise.
  bypassPlan?: boolean;
  bypassReason?: string;
}

/* -------------------------------------------------------
   DROPDOWNS
--------------------------------------------------------*/

export interface JobOrderDrop {
  id: number;
  code: string;
  name: string;
}

export interface BranchDrop {
  id: number;
  name: string;
}

export interface DesignDrop {
  id: number;
  code: string;
  name: string;
}

export interface ProductDrop {
  id: number;
  name: string;
}

export interface CustomerDrop {
  id: number;
  display: string;
}

/* -------------------------------------------------------
   🔥 MOST IMPORTANT (USED IN WORK ORDER ITEM AUTO-FILL)
--------------------------------------------------------*/
export interface WorkOrderDrop {
  id: number;
  code: string;
  name: string;

  orderType?: 'Customer' | 'CompanyStock';
  isCompanyStockOrder?: boolean;

  jobOrderId: number | null;
  jobOrderCode?: string | null;

  designId: number | null;
  designCode?: string | null;
  designName?: string | null;

  productId: number | null;
  productName: string | null;

  branchId: number | null;
  branchName?: string | null;

  orderQty?: number | null;
}

/* -------------------------------------------------------
   RAW API MODEL (PascalCase + camelCase support)
--------------------------------------------------------*/
export interface WorkOrderApiModel {
  Id?: number;
  id?: number;

  WorkOrderCode?: string;
  workOrderCode?: string;

  OrderType?: 'Customer' | 'CompanyStock';
  orderType?: 'Customer' | 'CompanyStock';
  IsCompanyStockOrder?: boolean;
  isCompanyStockOrder?: boolean;

  JobOrderId?: number | null;
  jobOrderId?: number | null;
  JobOrderCode?: string | null;
  jobOrderCode?: string | null;

  CustomerId?: number | null;
  customerId?: number | null;
  CustomerName?: string | null;
  customerName?: string | null;

  ProductId?: number | null;
  productId?: number | null;
  ProductName?: string | null;
  productName?: string | null;
  
  DesignId?: number | null;
  designId?: number | null;
  DesignCode?: string | null;
  designCode?: string | null;
  DesignName?: string | null;
  designName?: string | null;

  BranchId?: number | null;
  branchId?: number | null;
  BranchName?: string | null;
  branchName?: string | null;

  OrderQty?: number | null;
  orderQty?: number | null;
  OrderDate?: string;
  orderDate?: string;
  DueDate?: string | null;
  dueDate?: string | null;

  ReferenceNo?: string | null;
  referenceNo?: string | null;
  Remarks?: string | null;
  remarks?: string | null;

  Status?: string | null;
  status?: string | null;
  Active?: boolean;
  active?: boolean;
  Deleted?: boolean | null;
  deleted?: boolean | null;

  IsPosted?: boolean;
  isPosted?: boolean;

  PostedBy?: number | null;
  postedBy?: number | null;
  PostedByEmployeeId?: number | null;
  postedByEmployeeId?: number | null;
  PostedByName?: string | null;
  postedByName?: string | null;
  PostedDate?: string | null;
  postedDate?: string | null;

  RecordTimeStamp?: string | null;
  recordTimeStamp?: string | null;

  CreatedBy?: number | null;
  createdBy?: number | null;
  UpdatedBy?: number | null;
  updatedBy?: number | null;

  CreatedByEmployeeId?: number | null;
  createdByEmployeeId?: number | null;
  UpdatedByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;

  CreatedByName?: string | null;
  createdByName?: string | null;
  UpdatedByName?: string | null;
  updatedByName?: string | null;

  CreatedDate?: string | null;
  createdDate?: string | null;
  UpdatedDate?: string | null;
  updatedDate?: string | null;
}
