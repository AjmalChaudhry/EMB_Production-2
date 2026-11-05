using System;

namespace EMB_Production.ViewModels
{
    public class WorkOrderItemDto
    {
        public int Id { get; set; }

        public int WorkOrderId { get; set; }
        public int? JobOrderId { get; set; }

        public int? DesignId { get; set; }
        public int? CompId { get; set; }

        // Optional finished/product link
        public int? ProductId { get; set; }

        // Planned quantity
        public int Quantity { get; set; }

        public decimal? UnitPrice { get; set; }

        public int? BranchId { get; set; }
        public int? MachineId { get; set; }

        public string Remarks { get; set; }
        public string Status { get; set; }

        public bool? Active { get; set; }
    }
}