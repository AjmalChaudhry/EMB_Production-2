using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class MachineCostProfileViewModel
    {
        public class MachineCostProfileListRowViewModel
        {
            public int Id { get; set; }
            public int MachineId { get; set; }
            public string MachineCode { get; set; }

            public decimal? HourlyRate { get; set; }
            public decimal? PowerKW { get; set; }
            public int? StitchPerMinute { get; set; }

            public decimal? OperatorHourlyRate { get; set; }
            public decimal? ElectricityUnitRate { get; set; }

            public string EffectiveFromText { get; set; }
            public bool? Active { get; set; }
        }

        public class MachineCostProfileDetailViewModel
        {
            public int Id { get; set; }

            public int MachineId { get; set; }
            public string MachineCode { get; set; } = "";

            public decimal? HourlyRate { get; set; }
            public decimal? PowerKW { get; set; }
            public int? StitchPerMinute { get; set; }

            public decimal? OperatorHourlyRate { get; set; }
            public decimal? ElectricityUnitRate { get; set; }

            public DateTime? EffectiveFrom { get; set; }
            public string EffectiveFromText { get; set; } = "";

            public bool? Active { get; set; }

            // 🔹 Future Audit Fields (optional but recommended)
            public string CreatedDateText { get; set; } = "";
            public string UpdatedDateText { get; set; } = "";
        }
    }
}