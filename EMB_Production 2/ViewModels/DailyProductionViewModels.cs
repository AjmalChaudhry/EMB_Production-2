using System;

namespace EMB_Production.ViewModels
{
    public class DailyProductionRowViewModel
    {
        public int Id { get; set; }

        // Sprint-1: upstream WO id so the WO column can deep-link to
        // the WorkOrders module. WorkOrderItemId points at the line,
        // WorkOrderId points at the header — both are useful so the
        // frontend can decide which to navigate to.
        public int? WorkOrderItemId { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderLabel { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int? DesignId { get; set; }
        public string DesignName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public int? OperatorId { get; set; }
        public string OperatorCode { get; set; }
        public string OperatorName { get; set; }
        public string OperatorLabel { get; set; }

        public string Shift { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }

        public int? Stitches { get; set; }

        public int? PlannedQty { get; set; }
        public int? ActualQty { get; set; }
        public int? RejectedQty { get; set; }
        public int? AcceptedQty { get; set; }

        public string Status { get; set; }

        public bool IsPosted { get; set; }
        public bool IsSentToQC { get; set; }
        public bool HasQc { get; set; }
        public bool QcCompleted { get; set; }
        public bool IsReversed { get; set; }

        // Sprint-1: rework signal so the row chip can show
        // "Rework (QC #N)" without the frontend having to parse the
        // Remarks string for the bracketed tag.
        public bool IsRework { get; set; }
        public int? SourceQcInspectionId { get; set; }

        // Sprint-1: "posted within last 24h" badge driver. Useful for
        // shift managers scanning the list for fresh entries.
        public bool IsRecent { get; set; }

        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPost { get; set; }
        public bool CanReverse { get; set; }
    }

    public class DailyProductionDetailViewModel
    {
        public int Id { get; set; }

        public int? WorkOrderItemId { get; set; }
        public string WorkOrderLabel { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int? DesignId { get; set; }
        public string DesignName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public int? OperatorId { get; set; }
        public string OperatorName { get; set; }
        public string OperatorCode { get; set; }
        public string OperatorLabel { get; set; }

        public string Shift { get; set; }

        public string StartTime { get; set; }
        public string EndTime { get; set; }

        public int? Stitches { get; set; }

        public int? PlannedQty { get; set; }
        public int? ActualQty { get; set; }
        public int? RejectedQty { get; set; }
        public int? AcceptedQty { get; set; }

        public string Status { get; set; }
        public string Remarks { get; set; }
        public bool? Deleted { get; set; }

        public bool? IsPosted { get; set; }
        public bool? IsReversed { get; set; }

        public bool? IsSentToQC { get; set; }
        public string SentToQCDate { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public string UpdatedDate { get; set; }

        public string RecordTimeStamp { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public int? UpdatedByEmployeeId { get; set; }

        public bool HasQc { get; set; }
        public bool QcCompleted { get; set; }
        public int? QcId { get; set; }

        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPost { get; set; }
        public bool CanReverse { get; set; }

        // Sprint-2: workflow stepper data. The canonical pipeline is
        // DRAFT → POSTED → SENT_TO_QC → COMPLETED, with REVERSED
        // sitting as a red sibling at the same slot as POSTED.
        public string[] WorkflowStages { get; set; }
        public int WorkflowStageIndex { get; set; }

        // Sprint-1: upstream WorkOrder header id so the detail header
        // can render a clickable "WO-2026-0001" pill alongside the
        // row's existing WorkOrderItemId.
        public int? WorkOrderId { get; set; }

        public double? DurationMinutes
        {
            get
            {
                DateTime s, e;
                if (DateTime.TryParse(StartTime, out s) && DateTime.TryParse(EndTime, out e))
                    return (e - s).TotalMinutes;

                return null;
            }
        }
    }

    public class DailyProductionInputViewModel
    {
        public int? WorkOrderItemId { get; set; }
        public int? MachineId { get; set; }
        public int? OperatorId { get; set; }

        public string Shift { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public int? Stitches { get; set; }
        public int? ActualQty { get; set; }
        public int? RejectedQty { get; set; }

        public string Remarks { get; set; }

        // Rework loop: when set, this DP entry is the operator
        // re-doing pieces that QC marked for rework. On Post the
        // controller updates the parent QC row's status instead
        // of accruing fresh CompletedQty on the WO Item.
        public int? SourceQcInspectionId { get; set; }
    }

    public class ReverseDailyProductionDto
    {
        public string Reason { get; set; }
    }
}