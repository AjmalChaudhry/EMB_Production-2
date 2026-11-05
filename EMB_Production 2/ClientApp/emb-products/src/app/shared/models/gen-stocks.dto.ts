
export interface StockRowViewModel {
  id: number;

  prodId: number | null;
  productName: string | null;

  itemType: string | null;
  itemId: number | null;
  itemName: string | null;

  branchId: number | null;
  branchName: string | null;

  warehouseId: number | null;
  warehouseName: string | null;

  stockStage: string | null;
  transactionType: string | null;

  openingQty: number;
  inQty: number;
  outQty: number;

  qty: number;
  currentQty: number;

  customerReservedStock: number;
  freeCompanyStock: number;
  totalFinishedStock: number;

  unitCost: number;
  weightedAvgCost: number;

  totalFinishedCost: number;
  totalCost: number;

  reservedValue: number;
  freeStockValue: number;

  sellingPrice: number;
  expectedRevenue: number;
  estimatedProfit: number;
  profitMarginPercent: number;

  stockStatus: string | null;
  stockValueStatus: string | null;
  stockValueClass: string | null;

  qcPostCount: number;
  dispatchCount: number;
  reverseCount: number;
  movementCount: number;

  rate: number | null;
  amount: number | null;
  lastKnownRate: number | null;

  unit: string | null;

  referenceType: string | null;
  referenceId: number | null;
  referenceNo: string | null;

  customerId?: number | null;
  customerCode?: string | null;
  customerName?: string | null;

  stockDescription: string | null;
  remarks: string | null;

  active: boolean;
  deleted: boolean;

  createdBy: number | null;
  createdByName?: string | null;
  updatedBy: number | null;

  recordTimeStamp: string | null;
  createdDate: string | null;
  updatedDate: string | null;
}


export interface StockCustomerReservation {
  customerId: number;
  customerCode: string | null;
  customerName: string | null;
  jobOrders: string | null;
  reservedQty: number;
  dispatchedQty: number;
  remainingQty: number;
}

export interface StockDetailViewModel extends StockRowViewModel {
  lastMovementDate?: string | null;
  firstMovementDate?: string | null;
  pendingDispatchCount?: number;
  pendingDispatchQty?: number;
  customerReservations?: StockCustomerReservation[];
}


export interface StockDashboardViewModel {
  
  customerReservedStock: number;
  freeCompanyStock: number;
  totalFinishedStock: number;
  
  totalInventoryCost: number;
  totalReservedValue: number;
  freeStockValue: number;
  
  totalExpectedRevenue: number;
  totalEstimatedProfit: number;
  overallProfitMarginPercent: number;
  
  totalQcPostCount: number;
  totalDispatchCount: number;
  totalReverseCount: number;
  totalMovementCount: number;
  
  totalProducts: number;
  totalBranches: number;
}


export interface StocksPageViewModel {
  rows: StockRowViewModel[];

  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;

  dashboard?: StockDashboardViewModel | null;

  search?: string | null;
  branchId?: number | null;
}
