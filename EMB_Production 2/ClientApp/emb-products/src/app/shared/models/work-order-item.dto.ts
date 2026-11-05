
export interface WorkOrderItemRowViewModel {
  id: number;

  workOrderId: number | null;
  workOrderCode: string | null;

  jobOrderId: number | null;
  jobOrderCode: string | null;

  designId: number | null;
  designCode: string | null;
  designName: string | null;

  compId: number | null;
  componentCode: string | null;
  componentName: string | null;

  productId: number | null;
  productName: string | null;

  quantity: number;

  unitPrice: number | null;
  totalPrice: number | null;

  branchId: number | null;
  branchName: string | null;

  machineId: number | null;
  machineName: string | null;

  status: string | null;
  active: boolean;
  completedQty: number;
  rejectedQty: number;

  remarks: string | null;
  deleted: boolean;

  createdDate: string | null;
  updatedDate: string | null;
  createdDateText: string | null;
  updatedDateText: string | null;

  threadCost: number | null;
  machineCost: number | null;
  laborCost: number | null;
  electricityCost: number | null;
  otherCost: number | null;
  totalCost: number | null;
  costPerUnit: number | null;
}

export interface WorkOrderItemDetailViewModel extends WorkOrderItemRowViewModel {
  workOrderLabel: string | null;

  createdBy: number | null;
  updatedBy: number | null;
  recordTimeStamp: string | null;

  createdByEmployeeId: number | null;
  updatedByEmployeeId: number | null;
}

export interface WorkOrderItemDto {
  id: number | null;

  workOrderId: number;
  jobOrderId: number | null;

  designId: number | null;
  compId: number | null;
  productId: number | null;

  quantity: number;

  unitPrice: number | null;
  totalPrice: number | null;

  costPerUnit: number | null;
  totalCost: number | null;

  branchId: number | null;
  machineId: number | null;

  status: string | null;
  active: boolean;

  remarks: string | null;
}

export interface WorkOrderItemFormModel {
  id: number | null;

  workOrderId: number | null;
  jobOrderId: number | null;

  designId: number | null;
  compId: number | null;
  productId: number | null;

  quantity: number;

  unitPrice: number | null;
  totalPrice: number | null;

  costPerUnit: number | null;
  totalCost: number | null;

  branchId: number | null;
  machineId: number | null;

  status: string | null;
  active: boolean;

  remarks: string | null;
}

export interface WorkOrderItemCostResult {
  costPerUnit: number;
  totalCost: number;
  sellingPricePerUnit: number;
  totalPrice: number;

  threadCost: number;
  machineCost: number;
  laborCost: number;
  electricityCost: number;
  otherCost: number;
}

export interface DropdownItem {
  id: number;
  name: string;
}

export interface DropdownItem {
  id: number;
  name: string;
}
