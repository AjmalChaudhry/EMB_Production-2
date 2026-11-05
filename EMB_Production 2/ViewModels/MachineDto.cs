using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class MachineDto
    {
        public int Id { get; set; }

        public string Code { get; set; }

        public int? BrandId { get; set; }
        public int? ServiceId { get; set; }

        public string HeadtoHead { get; set; }
        public string FrameHeight { get; set; }

        public int? HeadsNum { get; set; }
        public int? NeedlesNum { get; set; }

        public string SerialNumber { get; set; }
        public string CallingNumber { get; set; }

        // dates come from Angular as "yyyy-MM-dd" strings
        public string PurchaseDate { get; set; }
        public int? Warranty { get; set; }
        public string ServiceEnd { get; set; }
        public string ServiceNext { get; set; }

        public int? BranchId { get; set; }

        public bool Active { get; set; }
    }
}