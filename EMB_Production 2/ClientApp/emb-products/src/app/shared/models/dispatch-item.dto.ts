export interface DispatchItemRowViewModel {
  id: number;
  dispatchId: number;

  productId: number | null;
  productName: string | null;

  branchId?: number | null;
  branchName?: string | null;

  customerReservedStock?: number;
  reservedStock?: number;
  freeCompanyStock?: number;
  totalFinishedStock?: number;

  availableQty?: number;
  availableQtySnapshot: number;

  dispatchQty: number;

  costPrice: number;
  baseSalePrice: number;
  discountPercent: number;
  discountAmount: number;
  salePrice: number;
  amount: number;

  unit?: string | null;
  remarks?: string | null;

  active: boolean;
  deleted: boolean;
  isPosted: boolean;
}

export interface DispatchItemDetailViewModel {
  id: number;
  dispatchId: number;

  productId: number | null;
  productName: string | null;

  branchId?: number | null;
  branchName?: string | null;

  customerReservedStock?: number;
  reservedStock?: number;
  freeCompanyStock?: number;
  totalFinishedStock?: number;

  availableQty?: number;
  availableQtySnapshot?: number | null;

  dispatchQty?: number | null;

  costPrice?: number | null;
  baseSalePrice?: number | null;
  discountPercent?: number | null;
  discountAmount?: number | null;
  salePrice?: number | null;
  amount?: number | null;

  unit?: string | null;
  remarks?: string | null;

  active?: boolean;
  deleted?: boolean;
  isPosted?: boolean;

  createdDate?: string | null;
  updatedDate?: string | null;
  recordTimeStamp?: string | null;

  createdDateText?: string | null;
  updatedDateText?: string | null;
  recordTimeStampText?: string | null;

  createdByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;
}


export interface DispatchStockLookupViewModel {
  productId: number;
  productName: string;

  branchId?: number | null;
  branchName?: string | null;

  customerReservedStock: number;
  reservedStock?: number;
  freeCompanyStock: number;
  totalFinishedStock: number;

  availableQty: number;
  dispatchQty?: number;

  costPrice: number;
  baseSalePrice: number;
  discountPercent: number;
  discountAmount: number;
  salePrice: number;
  amount: number;

  unit: string;
}
