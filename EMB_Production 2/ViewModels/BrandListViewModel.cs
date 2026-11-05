using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class BrandListViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string BrandName { get; set; }
        public string Description { get; set; }

        public int? BranchId { get; set; }       // REQUIRED for Edit dropdown
        public string BranchName { get; set; }   // Show in table

        public bool Active { get; set; }
    }
}