using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class MachineParameterRowViewModel
    {
        public int Id { get; set; }
        public string MachineCode { get; set; }
        public string HeadToHead { get; set; }
        public decimal? FrameHeight { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }

    }
}