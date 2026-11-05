using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class ServiceTypeDto
    {
        public int Id { get; set; }            // Required for update
        public string Service_Type { get; set; }
        public string Service_TypeDescription { get; set; }
        public int? BranchId { get; set; }
        public bool Active { get; set; }
    }
}