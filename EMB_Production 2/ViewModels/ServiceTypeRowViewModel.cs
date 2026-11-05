using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class ServiceTypeRowViewModel
    {
        public int Id { get; set; }

        public string Service_Type { get; set; }
        public string Service_TypeDescription { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public DateTime? RecordTimeStamp { get; set; }

        public string CreatedByEmployeeId { get; set; }
        public string UpdatedByEmployeeId { get; set; }
        // Extra
        public int RelatedServicesCount { get; set; }
        

    }
}
