using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class MachineParameterDto
    {
        public int Id { get; set; }
        public int? MachineId { get; set; }
        public string HeadToHead { get; set; }
        public decimal? FrameHeight { get; set; }
        public bool Active { get; set; }
    }
}