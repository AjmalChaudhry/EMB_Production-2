export interface DropdownItem {
  id: number;
  name: string;
}

export interface WorkOrderItemDropdownVm {
  id: number;
  name: string;
  workOrderLabel: string;
  productName: string;
  designName: string;
  componentName: string;
  plannedQty: number;
  completedQty: number;
  remainingQty: number;
  isCompleted: boolean;
  machineId?: number | null;
  stitchesPerPiece?: number | null;
}

export interface DailyProductionInputDto {
  workOrderItemId: number | null;
  machineId: number | null;
  operatorId: number | null;

  shift: string | null;
  startTime: string | null;
  endTime: string | null;

  stitches: number | null;
  actualQty: number | null;
  rejectedQty: number | null;
  acceptedQty?: number | null;

  workingHours?: number | null;
  productionPerHour?: number | null;

  remarks: string;

  // Rework loop: when set, this DP entry is the operator re-doing
  // pieces that QC marked for rework. Server side flips the parent
  // QC row to AwaitingReinspection on PostToWip.
  sourceQcInspectionId?: number | null;
}

export interface ReworkTaskVm {
  qcInspectionId: number;
  qcNo: string;
  workOrderId: number | null;
  workOrderLabel: string;
  workOrderItemId: number | null;
  productId: number | null;
  productName: string;
  designId: number | null;
  designName: string;
  reworkQty: number;
  machineId: number | null;
  stitchesPerPiece: number;
  name: string;
}

export interface DailyProductionRowViewModel {
  id: number;

  workOrderItemId: number | null;
  /** Sprint-1: upstream WO header id — drives the clickable WO badge
   *  in the list (deep-links to /work-order). Without this the
   *  frontend would have to round-trip per row to resolve the parent. */
  workOrderId?: number | null;
  workOrderLabel: string | null;

  productId: number | null;
  productName: string | null;

  designId: number | null;
  designName: string | null;

  machineId: number | null;
  machineName: string | null;

  operatorId: number | null;
  operatorCode: string | null;
  operatorName: string | null;
  operatorLabel: string | null;

  shift: string | null;
  startTime: string | null;
  endTime: string | null;

  workingHours: number | null;
  productionPerHour: number | null;

  stitches: number | null;

  plannedQty: number | null;
  actualQty: number | null;
  rejectedQty: number | null;
  acceptedQty: number | null;

  status: string | null;

  isPosted: boolean;
  isSentToQC: boolean;
  hasQc: boolean;
  qcCompleted: boolean;
  isReversed: boolean;

  canEdit: boolean;
  canDelete: boolean;
  canPost: boolean;
  canReverse: boolean;

  /** Sprint-1: rework signal. When true the row is the operator
   *  re-doing pieces an earlier QC marked for rework; the chip shows
   *  "Rework (QC #N)". */
  isRework?: boolean;
  sourceQcInspectionId?: number | null;

  /** Sprint-1: row created within last 24h — drives the "recent"
   *  lightning bolt badge on the date cell. */
  isRecent?: boolean;
}

export interface DailyProductionDetailViewModel {
  id: number;

  workOrderItemId: number | null;
  workOrderLabel: string | null;

  productId: number | null;
  productName: string | null;

  designId: number | null;
  designName: string | null;

  machineId: number | null;
  machineName: string | null;

  operatorId: number | null;
  operatorCode: string | null;
  operatorName: string | null;
  operatorLabel: string | null;

  shift: string | null;
  startTime: string | null;
  endTime: string | null;

  workingHours: number | null;
  productionPerHour: number | null;

  stitches: number | null;

  plannedQty: number | null;
  actualQty: number | null;
  rejectedQty: number | null;
  acceptedQty: number | null;

  status: string | null;
  remarks: string | null;

  isPosted: boolean;
  isSentToQC: boolean;
  hasQc: boolean;
  qcCompleted: boolean;
  isReversed: boolean;

  qcId: number | null;
  sentToQCDate: string | null;

  recordTimeStamp: string | null;

  createdDate: string | null;
  createdByEmployeeId: number | null;

  updatedDate: string | null;
  updatedByEmployeeId: number | null;

  canEdit: boolean;
  canDelete: boolean;
  canPost: boolean;
  canReverse: boolean;

  /** Sprint-1: upstream WO header id for the clickable detail header. */
  workOrderId?: number | null;

  /** Sprint-2: workflow stepper data. Stages = the canonical pipeline;
   *  stageIndex marks the current dot. The frontend renders dots/lines
   *  and colours REVERSED in red as a sibling of POSTED_TO_WIP. */
  workflowStages?: string[];
  workflowStageIndex?: number;
}

/** Sprint-2 — append-only audit log entry. */
export interface DpAuditEntry {
  id: number;
  dpId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}

/** Sprint-3 — bulk-action response envelope. */
export interface DpBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}
