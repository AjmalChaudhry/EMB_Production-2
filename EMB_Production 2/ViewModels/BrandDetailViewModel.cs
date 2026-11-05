using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class BranchDetailViewModel
    {
        public int BranchId { get; set; }
        public string Name { get; set; }

        public string Location { get; set; }
        public string Contect { get; set; }

        public int CompanyId { get; set; }
        public string CompanyName { get; set; }

        public bool IsActive { get; set; }
        public bool Deleted { get; set; }

        // ===== AUDIT (RAW, NOT FORMATTED) =====
        public int? CreatedByEmployeeId { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedByEmployeeId { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
