export type DispatchType = 'SALES' | 'TRANSFER' | 'RETURN' | 'INTERNAL';
export type SaleType = 'CUSTOMER_DEMAND' | 'DIRECT_SALE';

export interface DispatchRowViewModel {
  id: number;

  dispatchDate?: string | null;
  dispatchDateText: string;

  dispatchType: DispatchType;
  saleType: SaleType;
  saleTypeText?: string | null;

  jobOrderId?: number | null;
  jobOrderCode?: string | null;

  customerId?: number | null;
  customerCode?: string | null;
  customerName?: string | null;

  directCustomerName?: string | null;
  directCustomerMobile?: string | null;
  directCustomerAddress?: string | null;

  displayCustomerName?: string | null;

  branchId?: number | null;
  branchName?: string | null;

  vehicleNo?: string | null;
  driverName?: string | null;
  remarks?: string | null;

  /** Sprint-1: product id for the clickable Product badge in the list. */
  productId?: number | null;
  productName?: string | null;

  itemCount: number;
  dispatchQtyTotal: number;

  baseSalePrice?: number;
  salePrice: number;
  totalAmount: number;

  // Cost vs margin (locked at post time on each DispatchItem.CostPrice)
  totalCost?: number;
  costPerPc?: number;
  totalMargin?: number;
  marginPct?: number;
  isLoss?: boolean;

  stockStatus?: string | null;

  active?: boolean;
  isPosted: boolean;
  isReversed?: boolean | null;
  statusText?: string | null;

  /** Sprint-1: row created within last 24h → lightning bolt badge. */
  isRecent?: boolean;
}

export interface DispatchDetailViewModel {
  id: number;

  dispatchDate?: string | null;
  dispatchDateText?: string | null;
  dispatchDateInput?: string | null;

  dispatchType: DispatchType;
  saleType: SaleType;
  saleTypeText?: string | null;

  jobOrderId?: number | null;
  jobOrderCode?: string | null;

  customerId?: number | null;
  customerCode?: string | null;
  customerName?: string | null;

  directCustomerName?: string | null;
  directCustomerMobile?: string | null;
  directCustomerAddress?: string | null;

  displayCustomerName?: string | null;

  branchId?: number | null;
  branchName?: string | null;

  vehicleNo?: string | null;
  driverName?: string | null;
  remarks?: string | null;

  active?: boolean;
  deleted?: boolean;
  isPosted?: boolean;
  isReversed?: boolean;
  statusText?: string | null;

  createdBy?: number | string | null;
  createdByName?: string | null;
  createdDate?: string | null;

  updatedBy?: number | string | null;
  updatedByName?: string | null;
  updatedDate?: string | null;

  postedBy?: number | string | null;
  postedByName?: string | null;
  postedDate?: string | null;

  recordTimeStamp?: string | null;

  items?: DispatchItemViewModel[];
}

export interface DispatchDto {
  id: number;
  dispatchDate: string;

  dispatchType: DispatchType;
  saleType: SaleType;

  jobOrderId: number | null;
  customerId: number | null;
  branchId: number | null;

  directCustomerName: string | null;
  directCustomerMobile: string | null;
  directCustomerAddress: string | null;

  vehicleNo: string;
  driverName: string;
  remarks: string;

  active: boolean;

  items: DispatchItemDto[];
}

export interface DispatchItemDto {
  id: number;
  dispatchId: number;

  productId: number;
  dispatchQty: number | null;

  costPrice: number | null;
  baseSalePrice: number | null;
  discountPercent: number | null;
  discountAmount: number | null;
  salePrice: number | null;
  amount?: number | null;

  unit?: string | null;
  remarks?: string | null;

  active: boolean;
}

export interface DispatchItemViewModel {
  id: number;
  dispatchId: number;

  productId: number;
  productName?: string | null;

  branchId?: number | null;
  branchName?: string | null;

  availableQtySnapshot: number;

  customerReservedStock: number;
  freeCompanyStock: number;
  totalFinishedStock: number;
  availableQty: number;

  dispatchQty: number;

  costPrice: number;
  baseSalePrice: number;
  discountPercent: number;
  discountAmount: number;
  salePrice: number;
  amount: number;

  unit?: string | null;
  remarks?: string | null;

  active?: boolean;
  deleted?: boolean;
  isPosted?: boolean;
}

export interface DispatchStockRow {
  productId?: number;
  stockId?: number;

  productName: string;

  branchId?: number | null;
  branchName?: string | null;

  customerReservedStock: number;
  freeCompanyStock: number;
  totalFinishedStock: number;

  availableQty: number;
  dispatchQty: number;

  costPrice: number;
  baseSalePrice: number;
  discountPercent: number;
  discountAmount: number;
  salePrice: number;
  amount: number;

  unit: string;
}

export interface DispatchFormModel extends DispatchDto { }
