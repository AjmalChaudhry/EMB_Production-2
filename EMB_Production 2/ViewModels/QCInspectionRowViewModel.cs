using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class QcInspectionRowViewModel
    {
        public int Id { get; set; }
        public string QCNo { get; set; }

        public int? WorkOrderId { get; set; }
        public int? DesignId { get; set; }
        public int? ProductId { get; set; }
        public int? WorkOrderItemId { get; set; }
        public int? ProductionId { get; set; }

        public string WorkOrderLabel { get; set; }
        public string ProductName { get; set; }
        public string DesignName { get; set; }
        public string BranchName { get; set; }

        public decimal BatchQty { get; set; }
        public decimal InspectedQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public decimal PendingQty { get; set; }
        public decimal AvailableForQcQty { get; set; }
        public decimal QcCompletionPercent { get; set; }

        public string Status { get; set; }
        public string QcStatus { get; set; }

        public bool IsPosted { get; set; }
        public bool IsReversed { get; set; }

        public bool ItemsPending { get; set; }
        public bool ItemsCompleted { get; set; }

        public string ItemsStatusText { get; set; }
        public string InspectionProgressText { get; set; }

        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPost { get; set; }
        public bool CanReverse { get; set; }
        public bool CanReopen { get; set; }

        public bool CanCompleteItems { get; set; }
        public bool CanContinueQc { get; set; }
        public bool CanFinalize { get; set; }
        public bool CanReinspect { get; set; }

        public bool CanReinspectRework { get; set; }
        public bool HasReworkReference { get; set; }
        public string InspectionType { get; set; }
        public string SourceType { get; set; }

        public string InspectionDate { get; set; }
        public string CreatedDate { get; set; }

        public string InspectionDateText { get; set; }
        public string CreatedDateText { get; set; }

        // Sprint-1: Process QC quick-action gate. True when the QC has
        // not yet been processed (Pending) and isn't reversed/cancelled
        // — i.e. the operator can hit the dedicated Process button to
        // auto-fill defaults and post in a single click.
        public bool CanProcess { get; set; }

        // Sprint-1: created within last 24h badge driver.
        public bool IsRecent { get; set; }
    }

    public class QCInspectionDetailViewModel
    {
        public int Id { get; set; }
        public string QCNo { get; set; }

        public int? WorkOrderId { get; set; }
        public int? DesignId { get; set; }
        public int? ProductId { get; set; }
        public int? WorkOrderItemId { get; set; }
        public int? ProductionId { get; set; }

        public string WorkOrderLabel { get; set; }
        public string DesignName { get; set; }
        public string ProductName { get; set; }
        public string BranchName { get; set; }
        public string ProductionStatus { get; set; }

        public decimal BatchQty { get; set; }
        public decimal InspectedQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public decimal PendingQty { get; set; }
        public decimal AvailableForQcQty { get; set; }
        public decimal PostedQty { get; set; }

        public string IssuesFound { get; set; }
        public string Remarks { get; set; }
        public string Status { get; set; }
        public string QcStatus { get; set; }

        public int? InspectorId { get; set; }
        public string InspectorName { get; set; }

        public string InspectionType { get; set; }
        public string SourceType { get; set; }

        public bool IsPosted { get; set; }
        public bool IsReversed { get; set; }

        public bool ItemsPending { get; set; }
        public bool ItemsCompleted { get; set; }

        public string ItemsStatusText { get; set; }
        public string InspectionProgressText { get; set; }

        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPost { get; set; }
        public bool CanReverse { get; set; }
        public bool CanReopen { get; set; }

        public bool CanCompleteItems { get; set; }
        public bool CanContinueQc { get; set; }
        public bool CanFinalize { get; set; }

        public string PostedDate { get; set; }
        public string InspectionDate { get; set; }
        public string CreatedDate { get; set; }
        public string UpdatedDate { get; set; }

        // Sprint-2: workflow stepper data — the row drawer shows the
        // five-stage Pending → InProgress → AwaitingRework →
        // AwaitingReinspection → Completed pipeline with REVERSED /
        // CANCELLED as red siblings.
        public string[] WorkflowStages { get; set; }
        public int WorkflowStageIndex { get; set; }

        public ICollection<QCInspectionItemRowViewModel> Items { get; set; }
            = new List<QCInspectionItemRowViewModel>();
    }

    public class QCInspectionInputViewModel
    {
        public int? WorkOrderId { get; set; }
        public int? DesignId { get; set; }
        public int? ProductId { get; set; }
        public int? WorkOrderItemId { get; set; }
        public int? ProductionId { get; set; }

        public decimal BatchQty { get; set; }
        public decimal InspectedQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public int? BranchId { get; set; }

        public string Remarks { get; set; }
        public string IssuesFound { get; set; }

        public int? InspectorId { get; set; }
        public DateTime? InspectionDate { get; set; }

        public string InspectionType { get; set; }
        public string SourceType { get; set; }
    }

    public class QCInspectionUpdateViewModel
    {
        public int Id { get; set; }

        public decimal InspectedQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public string Remarks { get; set; }
        public string IssuesFound { get; set; }
        public string Status { get; set; }

        public int? InspectorId { get; set; }
        public DateTime? InspectionDate { get; set; }

        public string InspectionType { get; set; }
        public string SourceType { get; set; }
    }

    public class QCInspectionPostViewModel
    {
        public int Id { get; set; }
        public string Remarks { get; set; }
    }

    public class QcContinueInputViewModel
    {
        public decimal InspectedQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public string Remarks { get; set; }
        public string IssuesFound { get; set; }
        public DateTime? InspectionDate { get; set; }
    }

    public class QcFinalizeInputViewModel
    {
        public string Remarks { get; set; }
    }

    public class QCInspectionItemRowViewModel
    {
        public int Id { get; set; }

        public int QCInspectionId { get; set; }
        public string QCInspectionNo { get; set; }

        public int? WorkOrderItemId { get; set; }
        public string WorkOrderCode { get; set; }

        public int? ProductionId { get; set; }
        public string ProductionNo { get; set; }

        public int? CompId { get; set; }
        public string ComponentName { get; set; }

        public decimal Qty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }
        public decimal PendingQty { get; set; }

        public string Result { get; set; }
        public string Remarks { get; set; }

        public bool IsCompleted { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedByEmployeeId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedDateText { get; set; }
    }
}