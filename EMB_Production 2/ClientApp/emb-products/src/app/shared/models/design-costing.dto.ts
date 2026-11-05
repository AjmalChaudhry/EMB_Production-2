// =============================================================
// CREATE / UPDATE DTO
// =============================================================
export interface DesignCostingDto {
  id: number;
  designId: number | null;
  productId?: number | null;

  batchQty: number;
  threadCost: number;
  machineCost: number;
  laborCost: number;
  electricityCost: number;
  otherCost: number;

  totalCost: number;
  costPerUnit: number;
  marginPercent: number;
  sellingPricePerUnit: number;
  totalSellingPrice?: number;

  materialCostPerUnit?: number;
  productionCostPerUnit?: number;
  serviceCostPerUnit?: number;
  productCostPerUnit?: number;
  totalProductCost?: number;

  threadMetersPerPiece?: number;
  totalThreadMeters?: number;
  costPer1000Stitches?: number;

  productionMinutes?: number;
  productionHours?: number;
  piecesPerHour?: number;

  piecesPerShift?: number;
  dailyCapacity?: number;
  productionDays?: number;

  active: boolean;
}

// =============================================================
// LIST ROW VIEWMODEL
// =============================================================
export interface DesignCostingListRow {
  id: number;

  designId: number | null;
  designName: string;

  productId?: number | null;
  productCode?: string | null;
  productName?: string | null;

  batchQty: number;
  marginPercent: number | null;

  threadCost: number | null;
  machineCost: number | null;
  laborCost: number | null;
  electricityCost: number | null;
  otherCost: number | null;

  totalCost: number | null;
  costPerUnit: number | null;

  threadPerUnit?: number | null;
  stocksMaterialUnit?: number | null;
  materialCostPerUnit?: number | null;
  productionCostPerUnit?: number | null;
  serviceCostPerUnit?: number | null;
  productCostPerUnit?: number | null;
  totalProductCost?: number | null;

  sellingPricePerUnit: number | null;
  totalSellingPrice: number | null;

  active: boolean | null;
  createdByEmployeeId?: string | number | null;
  updatedByEmployeeId?: string | number | null;

  createdDate?: string | null;
  updatedDate?: string | null;
  createdDateText?: string | null;
  updatedDateText?: string | null;
}

// =============================================================
// DETAIL VIEWMODEL
// =============================================================
export interface DesignCostingDetail {
  id: number;

  designId: number | null;
  designName: string;

  productId?: number | null;
  productCode?: string | null;
  productName?: string | null;

  threadPerUnit?: number | null;
  stocksMaterialUnit?: number | null;
  materialCostPerUnit?: number | null;
  productionCostPerUnit?: number | null;
  serviceCostPerUnit?: number | null;
  productCostPerUnit?: number | null;
  totalProductCost?: number | null;

  batchQty: number;
  marginPercent: number | null;

  threadCost: number | null;
  machineCost: number | null;
  laborCost: number | null;
  electricityCost: number | null;
  otherCost: number | null;

  totalCost: number | null;
  costPerUnit: number | null;

  sellingPricePerUnit: number | null;
  totalSellingPrice: number | null;

  threadMetersPerPiece: number | null;
  totalThreadMeters: number | null;
  costPer1000Stitches: number | null;

  productionMinutes: number | null;
  productionHours: number | null;
  piecesPerHour: number | null;

  piecesPerShift: number | null;
  dailyCapacity: number | null;
  productionDays: number | null;

  active: boolean | null;

  createdByEmployeeId?: string | number | null;
  updatedByEmployeeId?: string | number | null;
  createdDate?: string | null;
  updatedDate?: string | null;
  createdDateText?: string | null;
  updatedDateText?: string | null;
}

// =============================================================
// RECALCULATION RESULT
// =============================================================
export interface DesignCostCalculationResult {
  threadCost: number;
  machineCost: number;
  laborCost: number;
  electricityCost: number;
  otherCost: number;

  totalCost: number;
  costPerUnit: number;

  sellingPricePerUnit: number;
  totalSellingPrice: number;

  marginPercent: number;

  threadMetersPerPiece: number;
  totalThreadMeters: number;
  costPer1000Stitches: number;

  productionMinutes: number;
  productionHours: number;
  piecesPerHour: number;

  piecesPerShift: number;
  dailyCapacity: number;
  productionDays: number;
}
