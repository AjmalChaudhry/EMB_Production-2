export interface DropdownItem {
  id: number;
  name: string;
  code?: string;
}

export interface ComponentMaterialRowViewModel {
  id: number;

  compId: number;
  componentCode: string;
  componentName: string;

  rawItemId: number;
  rawItemName: string;

  qtyPerUnit: number;
  unitId?: number | null;
  unitCode?: string;
  unitName?: string;

  remarks?: string;
  active: boolean;
  deleted?: boolean;

  /** Sprint-1: created within last 24h → drives the "NEW" badge. */
  isRecent?: boolean;
}

export interface ComponentMaterialDetailViewModel {
  id: number;

  compId: number;
  componentCode: string;
  componentName: string;

  rawItemId: number;
  rawItemName: string;

  qtyPerUnit: number;
  unitId?: number | null;
  unitCode?: string;
  unitName?: string;

  remarks?: string;
  active: boolean;
  deleted?: boolean;

  createdByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;

  createdDateText?: string;
  updatedDateText?: string;
}

export interface ComponentMaterialForm {
  id: number;
  compId: number;
  rawItemId: number;
  qtyPerUnit: number;
  unitId?: number | null;
  remarks: string;
  active: boolean;
}
