using System;

namespace EMB_Production.ViewModels
{
    // =========================================================
    // LIST ROW
    // =========================================================
    public class QCInspectionItemRowVm
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

        public string Result { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedByEmployeeId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedDateText { get; set; }
    }

    // =========================================================
    // DETAIL
    // =========================================================
    public class QCInspectionItemDetailVm
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

        public string Result { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedByEmployeeId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedDateText { get; set; }
    }

    // =========================================================
    // FORM DTO
    // =========================================================
    public class QCInspectionItemDto
    {
        public int Id { get; set; }
        public int QCInspectionId { get; set; }
        public int? WorkOrderItemId { get; set; }
        public int? ProductionId { get; set; }
        public int? CompId { get; set; }

        public decimal Qty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public decimal ReworkQty { get; set; }

        public string Result { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; } = true;
    }

    // =========================================================
    // DROPDOWN ITEM
    // =========================================================
    public class DropdownItemVm
    {
        public int Id { get; set; }
        public string Text { get; set; }
    }

    public class QcReverseInputViewModel
    {
        public string Reason { get; set; }
    }

    public class QcCancelInputViewModel
    {
        public string Reason { get; set; }
    }
}