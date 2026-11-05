using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
   
    public class ServiceDto
    {
            public int id { get; set; }

            public int? serviceTypeId { get; set; }
            public string serviceName { get; set; }
            public int? serviceRateCharge { get; set; }

            public int? machineDevice { get; set; }

            public string maxRange { get; set; }
            public decimal? maxCharges { get; set; }
            public decimal? minCharges { get; set; }

            public decimal? field1 { get; set; }
            public decimal? field2 { get; set; }

            public int? branchId { get; set; }

            public bool? active { get; set; }
    }    
}