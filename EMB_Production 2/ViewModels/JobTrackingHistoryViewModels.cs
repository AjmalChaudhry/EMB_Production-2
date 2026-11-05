using System;

namespace EMB_Production.ViewModels
{
    public class JobTrackingHistoryRowViewModel
    {
        public int Id { get; set; }

        public int? WorkOrderItemId { get; set; }
        public string WorkOrderItemLabel { get; set; }

        public string Status { get; set; }
        public DateTime StatusDate { get; set; }
        public string Remarks { get; set; }

        public bool? Active { get; set; }
        public bool? Deleted { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedByName { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public string UpdatedByName { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class JobTrackingHistoryDetailViewModel : JobTrackingHistoryRowViewModel
    {
        public string StatusDateText { get; set; }
        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }
    }

    public class JobTrackingHistoryDto
    {
        public int Id { get; set; } // 0 for create
        public int? WorkOrderItemId { get; set; }
        public int? BranchId { get; set; }

        public string Status { get; set; }
        public DateTime StatusDate { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
    }
}
