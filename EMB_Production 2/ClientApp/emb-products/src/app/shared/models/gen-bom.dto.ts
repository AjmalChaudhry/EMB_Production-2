// ================= API RESPONSE =================
export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data: T;
}

export interface ApiResponsePaged<T> {
  success: boolean;
  message?: string;
  data: {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  };
}

// ================= DROPDOWN =================
export interface DropdownItem {
  id: number;
  name: string;
  code?: string;
}

export interface BomMaterialViewModel {
  rawItemId?: number;
  rawItemName: string;
  qtyPerUnit: number;
  unitCode?: string | null;
  totalQty: number;
}

export interface BomRowViewModel {
  id: number;

  designId: number;
  compId: number;

  designName: string;
  designCode?: string;

  componentName: string;
  componentCode?: string;

  reqRepeats?: number | null;
  reqHead?: number | null;
  panni?: number | null;
  solving?: number | null;

  remarks?: string | null;

  active: boolean;
  deleted?: boolean;

  /** Sprint-1: created within last 24h → drives the "NEW" badge. */
  isRecent?: boolean;
}

export interface BomDetailViewModel extends BomRowViewModel {
  createdByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;

  createdDateText?: string | null;
  updatedDateText?: string | null;

  materials?: BomMaterialViewModel[];

  materialCost?: number;
  laborCost?: number;
  machineCost?: number;
  totalCost?: number;
}

// ================= CREATE / EDIT FORM =================
export interface BomForm {
  id: number;

  designId: number;
  compId: number;

  reqRepeats?: number | null;
  reqHead?: number | null;
  panni?: number | null;
  solving?: number | null;

  remarks?: string;
  active: boolean;
}
