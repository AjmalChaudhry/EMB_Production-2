using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class DailyProductionStopRowViewModel
    {
        public int Id { get; set; }

        public int ProductionId { get; set; }

        // Display only
        public string ProductionLabel { get; set; } = "";

        public string StopStart { get; set; }
        public string StopEnd { get; set; }

        public string Reason { get; set; } = "";

        public string CreatedDate { get; set; }

        // Computed in controller (AFTER ToList)
        public int DurationMinutes { get; set; }
    }

    public class DailyProductionStopDetailViewModel
        : DailyProductionStopRowViewModel
    {
        public bool IsOpenStop { get; set; }
    }
    public class DailyProductionStopInputViewModel
    {
        public int ProductionId { get; set; }

        public DateTime StopStart { get; set; }
        public DateTime? StopEnd { get; set; }

        public string Reason { get; set; } = "";
    }

    public class DowntimeReasonStatVm
    {
        public string Reason { get; set; }
        public int Minutes { get; set; }
        public double Percent { get; set; }
        public int Count { get; set; }
    }
}