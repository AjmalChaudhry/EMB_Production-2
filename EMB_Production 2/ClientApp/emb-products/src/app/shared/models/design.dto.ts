export interface DesignListViewModel {

  id: number;

  d_Code: string | null;
  dNo: string | null;
  collectionName: string | null;

  productId: number | null;
  productName: string | null;
  productCode?: string | null;

  machineId: number | null;
  machineName: string | null;

  customerId: number | null;
  customerName: string | null;

  branchId: number | null;
  branchName: string | null;

  active: boolean;

  // Usage / costing summary (from list endpoint)
  jobOrderCount?: number;
  componentCount?: number;
  hasCosting?: boolean;
  productCostPerUnit?: number | null;
  sellingPricePerUnit?: number | null;
  inUse?: boolean;
}


export interface DesignDetailViewModel {

  id: number;

  d_Code: string | null;
  dNo: string | null;
  collectionName: string | null;

  productId: number | null;
  productName: string | null;

  machineId: number | null;
  machineName: string | null;

  customerId: number | null;
  customerName: string | null;

  branchId: number | null;
  branchName: string | null;

  active: boolean;
  deleted: boolean;

  createdBy: number | null;
  createdByName?: string | null;
  createdDate: string | null;

  updatedBy: number | null;
  updatedByName?: string | null;
  updatedDate: string | null;
}


export interface DesignCreateModel {

  dNo: string;

  collectionName: string;

  productId: number | null;

  machineId: number | null;

  customerId: number | null;

  active: boolean;
}


export interface DesignUpdateModel {

  id: number;

  dNo: string;

  collectionName: string;

  productId: number | null;

  machineId: number | null;

  customerId: number | null;

  active: boolean;
}
