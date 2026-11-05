export interface QcInspectionItemRowDto {
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

  isNew?: boolean;
}

export interface QcInspectionRowDto {
  id: number;
  qcNo: string | null;

  workOrderId: number | null;
  designId: number | null;
  productId: number | null;
  workOrderItemId: number | null;

  workOrderLabel: string | null;
  productName: string | null;
  designName: string | null;

  batchQty: number;
  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  status: string | null;
  sourceType: string | null;

  isPosted: boolean;
  isReversed: boolean;

  canEdit: boolean;
  canDelete: boolean;
  canPost: boolean;
  canReverse: boolean;

  canReinspectRework: boolean;
  hasReworkReference: boolean;

  /** Sprint-1: drives the dedicated "Process QC" quick-action button.
   *  True for Pending rows that haven't been touched yet — the button
   *  hides itself once the QC moves to InProgress / AwaitingRework. */
  canProcess?: boolean;

  /** Sprint-1: created within last 24h → lightning badge on the date. */
  isRecent?: boolean;

  inspectionProgressText: string | null;
  itemsLoaded: boolean;
  itemsPending: number;
  itemsCompleted: number;
  itemsStatusText: string | null;

  inspectionDate: string | null;
  inspectionType: string | null;

  isExpanded: boolean;
  isEditingItems: boolean;

  items: QcInspectionItemRowDto[];
}

export interface QcInspectionDetailDto {
  id: number;
  qcNo: string | null;

  workOrderId: number | null;
  designId: number | null;
  productId: number | null;
  workOrderItemId: number | null;
  productionId: number | null;

  workOrderLabel: string | null;
  designName: string | null;
  productName: string | null;

  batchQty: number;
  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  issuesFound: string | null;
  remarks: string | null;
  status: string | null;

  inspectorId: number | null;
  inspectorName: string | null;

  inspectionType: string | null;
  sourceType: string | null;

  isPosted: boolean;
  isReversed: boolean;
  postedDate: string | null;

  inspectionDate: string | null;
  createdDate: string | null;
  updatedDate: string | null;

  itemsPending: number;
  itemsCompleted: number;
  itemsStatusText: string | null;
  inspectionProgressText: string | null;

  canEdit: boolean;
  canDelete: boolean;
  canPost: boolean;
  canReverse: boolean;
  canCompleteItems: boolean;
  canContinueQc: boolean;
  canFinalize: boolean;
  canReinspect: boolean;

  /** Sprint-2: workflow stepper data — five-stage pipeline rendered
   *  at the top of the detail drawer. */
  workflowStages?: string[];
  workflowStageIndex?: number;

  items: QcInspectionItemRowDto[];
}

/** Sprint-1 — Process QC quick-action payload. All fields are optional;
 *  the backend auto-fills sensible defaults (Inspected = BatchQty,
 *  Passed = Inspected, Failed = 0, Rework = 0) for anything left null.
 *  Set `andPost = true` to chain Process and Post into a single request. */
export interface QcProcessDto {
  inspectedQty?: number | null;
  passedQty?: number | null;
  failedQty?: number | null;
  reworkQty?: number | null;
  issuesFound?: string | null;
  remarks?: string | null;
  andPost?: boolean;
}

/** Sprint-2 — one entry in the audit-log timeline. */
export interface QcAuditEntry {
  id: number;
  qcId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}

/** Sprint-3 — standard bulk-action envelope. */
export interface QcBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}

export interface QcInspectionCreateDto {
  workOrderId: number | null;
  designId: number | null;
  productId: number | null;

  workOrderItemId: number | null;
  productionId: number | null;

  batchQty: number;
  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  branchId: number | null;

  remarks: string | null;
  issuesFound: string | null;

  inspectorId: number | null;
  inspectionDate: string | null;

  inspectionType: string | null;
  sourceType: string | null;
}

export interface QcInspectionUpdateDto {
  id: number;

  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  remarks: string | null;
  issuesFound: string | null;

  inspectorId: number | null;
  inspectionDate: string | null;

  inspectionType: string | null;
  sourceType: string | null;

  status?: string | null;
}

export interface QcInspectionContinueDto {
  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;

  remarks: string | null;
  issuesFound: string | null;
  inspectionDate: string | null;
}

export interface QcInspectionPostDto {
  id: number;
}

export interface QcInspectionReverseDto {
  reason: string;
}

export interface QcInspectionCancelPendingDto {
  reason?: string | null;
}

export interface QcInspectionReinspectDto {
  id: number;
}

export interface QcInspectionListSummaryDto {
  batchQty: number;
  inspectedQty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;
  rows?: number;
}

export interface QcInspectionListDataDto {
  items: QcInspectionRowDto[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  summary: QcInspectionListSummaryDto;
}
