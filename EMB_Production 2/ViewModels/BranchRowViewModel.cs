using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class BranchRowViewModel
    {
        public int Id { get; set; }
        public int? BranchCode { get; set; }

        public string Name { get; set; }
        public string Location { get; set; }
        public string Contect { get; set; }

        public string CompanyName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}