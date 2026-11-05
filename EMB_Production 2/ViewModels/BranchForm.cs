using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class BranchForm
    {
        public int? BranchId { get; set; }   // null for create
        public string Name { get; set; }
        public string Location { get; set; }
        public string Contect { get; set; }

        public int CompanyId { get; set; }
        public bool IsActive { get; set; }
    }
}