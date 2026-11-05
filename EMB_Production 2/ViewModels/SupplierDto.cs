using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class SupplierDto
    {
        public int Id { get; set; }   // 0 = create

        public string SupplierName { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }

        public int? BranchId { get; set; }
        public bool Active { get; set; }
    }
}