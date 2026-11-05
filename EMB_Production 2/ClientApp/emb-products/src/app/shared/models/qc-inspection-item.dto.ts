// ======================================================
// COMMON DROPDOWN
// ======================================================
export interface DropdownItem {
  id: number;
  name: string;
}

export interface QcItemAutoFillVm {
  qcInspectionId: number;
  workOrderId: number | null;
  designId: number | null;
  productId: number | null;
  workOrderItems: DropdownItem[];
  productions: DropdownItem[];
  components: DropdownItem[];
}
// ======================================================
// LIST ROW VIEW MODEL
// ======================================================
export interface QCInspectionItemRowViewModel {
  id: number;

  qcInspectionId: number;
  qcInspectionNo: string | null;

  workOrderItemId: number | null;
  workOrderCode: string | null;

  productionId: number | null;
  productionNo: string | null;

  compId: number | null;
  componentName: string | null;

  qty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  result: string | null;
  remarks: string | null;

  active: boolean;
  deleted: boolean;

  createdBy: number | null;
  createdByEmployeeId: string | null;
  createdDate: string | null;
  createdDateText: string | null;
}

// ======================================================
// DETAIL VIEW MODEL
// ======================================================
export interface QCInspectionItemDetailViewModel {
  id: number;

  qcInspectionId: number;
  qcInspectionNo: string | null;

  workOrderItemId: number | null;
  workOrderCode: string | null;

  productionId: number | null;
  productionNo: string | null;

  compId: number | null;
  componentName: string | null;

  qty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  result: string | null;
  remarks: string | null;

  active: boolean;
  deleted: boolean;

  createdBy: number | null;
  createdByEmployeeId: string | null;
  createdDate: string | null;
  createdDateText: string | null;
}

// ======================================================
// FORM MODEL (CREATE / UPDATE)
// ======================================================
export interface QCInspectionItemForm {
  id: number;

  qcInspectionId: number | null;
  workOrderItemId: number | null;
  productionId: number | null;
  compId: number | null;

  qty: number | null;
  passedQty: number | null;
  failedQty: number | null;
  reworkQty: number | null;

  result: string | null;
  remarks: string | null;

  active: boolean;
}
