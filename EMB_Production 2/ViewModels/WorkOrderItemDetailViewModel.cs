using System;

namespace EMB_Production.ViewModels
{
    // =============================================================
    // PREVIEW / LOOKUP SUPPORT
    // =============================================================
    public class WorkOrderItemPreviewItem
    {
        public int Id { get; set; }

        public int? CompId { get; set; }
        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int Quantity { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }
    }

    // =============================================================
    // DETAIL VIEWMODEL
    // =============================================================
    public class WorkOrderItemDetailViewModel
    {
        public int Id { get; set; }

        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string WorkOrderLabel { get; set; }

        public int? JobOrderId { get; set; }
        public string JobOrderCode { get; set; }

        public int? DesignId { get; set; }
        public string DesignCode { get; set; }
        public string DesignName { get; set; }

        public int? CompId { get; set; }
        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int Quantity { get; set; }

        public decimal? UnitPrice { get; set; }
        public decimal? TotalPrice { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public string Status { get; set; }
        public bool? Active { get; set; }

        public int CompletedQty { get; set; }
        public int RejectedQty { get; set; }

        public string Remarks { get; set; }
        public bool? Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public DateTime? RecordTimeStamp { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public int? UpdatedByEmployeeId { get; set; }

        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }

        public decimal? ThreadCost { get; set; }
        public decimal? MachineCost { get; set; }
        public decimal? LaborCost { get; set; }
        public decimal? ElectricityCost { get; set; }
        public decimal? OtherCost { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? CostPerUnit { get; set; }
    }

    // =============================================================
    // INPUT VIEWMODEL
    // =============================================================
    public class WorkOrderItemInputViewModel
    {
        public int Id { get; set; }

        public int WorkOrderId { get; set; }
        public int? CompId { get; set; }

        public int Quantity { get; set; }
        public int? MachineId { get; set; }

        public string Remarks { get; set; }
        public string Status { get; set; }
        public bool? Active { get; set; }
    }

    // =============================================================
    // LIST ROW VIEWMODEL
    // =============================================================
    public class WorkOrderItemRowViewModel
    {
        public int Id { get; set; }

        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }

        public int? JobOrderId { get; set; }
        public string JobOrderCode { get; set; }

        public int? DesignId { get; set; }
        public string DesignCode { get; set; }
        public string DesignName { get; set; }

        public int? CompId { get; set; }
        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int Quantity { get; set; }

        public decimal? UnitPrice { get; set; }
        public decimal? TotalPrice { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public string Remarks { get; set; }
        public string Status { get; set; }

        public bool? Active { get; set; }
        public bool? Deleted { get; set; }

        public int CompletedQty { get; set; }
        public int RejectedQty { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }

        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }

        public decimal? ThreadCost { get; set; }
        public decimal? MachineCost { get; set; }
        public decimal? LaborCost { get; set; }
        public decimal? ElectricityCost { get; set; }
        public decimal? OtherCost { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? CostPerUnit { get; set; }
    }
}