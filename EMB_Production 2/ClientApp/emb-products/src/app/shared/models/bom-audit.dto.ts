export type BomDriftKind = 'OK' | 'WRONG_ITEM' | 'WRONG_QTY' | 'MISSING_FROM_BOM';

export interface BomAuditKpis {
  designsAudited: number;
  totalRows: number;
  okRows: number;
  wrongItem: number;
  wrongQty: number;
  missing: number;
  driftRate: number;   // percentage 0-100
}

export interface BomAuditDesignRow {
  designId: number;
  designCode: string;
  designName: string;
  workOrderCount: number;
  itemRowCount: number;
  okCount: number;
  wrongItemCount: number;
  wrongQtyCount: number;
  missingCount: number;
  totalExpected: number;
  totalIssued: number;
}

export interface BomAuditDiagnostics {
  workOrdersInScope: number;
  distinctDesignsInScope: number;
  bomComponentMaterialRows: number;
  materialIssueRows: number;
}

export interface BomAuditSummaryResponse {
  kpis: BomAuditKpis;
  appliedTolerancePct: number;
  diagnostics?: BomAuditDiagnostics;
  items: BomAuditDesignRow[];
}

export interface BomAuditDetailRow {
  workOrderId: number;
  workOrderCode: string;
  designCode: string;
  designName: string;
  rawItemId: number;
  rawItemName: string;
  threadCode: string | null;
  threadName: string | null;
  expectedQty: number;
  issuedQty: number;
  driftQty: number;
  driftPct: number | null;
  kind: BomDriftKind;
  orderDate: string;
}

export interface BomAuditDetailResponse {
  items: BomAuditDetailRow[];
}
