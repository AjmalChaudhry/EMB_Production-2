using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class JobOrderItemDetailViewModel
    {
        public int Id { get; set; }

        public int CompId { get; set; }

        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int? ReqRepeats { get; set; }

        public int? ReqHead { get; set; }

        public int? Panni { get; set; }

        public int? Solving { get; set; }
    }
}