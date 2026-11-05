// ============ Production Summary ============
export interface ProductionKpi {
  daysCovered: number;
  totalProduced: number;
  totalAccepted: number;
  totalRejected: number;
  qcPassRate: number;
}
export interface ProductionDay     { date: string; produced: number; accepted: number; rejected: number; }
export interface ProductionMachine { machineName: string; produced: number; accepted: number; }
export interface ProductionProduct { productName: string; produced: number; accepted: number; }
export interface ProductionReport {
  filters: { from: string; to: string };
  summary: ProductionKpi;
  byDay: ProductionDay[];
  byMachine: ProductionMachine[];
  byProduct: ProductionProduct[];
}

// ============ Sales & Dispatch ============
export interface SalesKpi {
  dispatchCount: number;
  totalQty: number;
  totalValue: number;
  totalCost: number;
  grossProfit: number;
  marginPercent: number;
}
export interface SalesCustomer { customerName: string; dispatchCount: number; qty: number; value: number; }
export interface SalesProduct  { productName: string;  qty: number; value: number; }
export interface SalesDay      { date: string; dispatches: number; qty: number; value: number; }
export interface SalesReport {
  filters: { from: string; to: string };
  summary: SalesKpi;
  byCustomer: SalesCustomer[];
  byProduct:  SalesProduct[];
  byDay:      SalesDay[];
}

// ============ Inventory Valuation ============
export interface InventoryKpi {
  rawItemCount: number;
  rawValue: number;
  finishedItemCount: number;
  finishedValue: number;
  grandTotal: number;
}
export interface InventoryRaw      { name: string; type: string; balance: number; lastRate: number; value: number; }
export interface InventoryFinished { productName: string; reserved: number; free: number; balance: number; unitCost: number; value: number; }
export interface InventoryReport {
  summary: InventoryKpi;
  rawItems: InventoryRaw[];
  finishedStock: InventoryFinished[];
}

// ============ Customer Order Tracking ============
export interface CustomerTrackingKpi {
  totalOrders: number;
  openCount: number;
  fullyDispatched: number;
  partialCount: number;
  outstandingQty: number;
}
export interface CustomerTrackingRow {
  jobOrderId: number;
  jobOrderCode: string;
  customerName: string;
  productName: string;
  designCode: string;
  orderQty: number;
  orderDate: string;
  dueDate: string | null;
  workOrderCount: number;
  finishedQty: number;
  dispatchedQty: number;
  outstandingQty: number;
  status: 'OPEN' | 'READY' | 'PARTIAL' | 'DISPATCHED' | string;
}
export interface CustomerTrackingReport {
  summary: CustomerTrackingKpi;
  rows: CustomerTrackingRow[];
}

export const DATE_PRESETS = [
  { value: 'TODAY',       label: 'Today' },
  { value: 'YESTERDAY',   label: 'Yesterday' },
  { value: 'THIS_WEEK',   label: 'This Week' },
  { value: 'THIS_MONTH',  label: 'This Month' },
  { value: 'LAST_30',     label: 'Last 30 Days' },
  { value: 'CUSTOM',      label: 'Custom Range' },
];
