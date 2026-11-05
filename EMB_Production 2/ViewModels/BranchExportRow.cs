using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class BranchExportRow
    {
        public int Id { get; set; }
        public int? BranchId { get; set; }
        public string Name { get; set; }
        public int? Code { get; set; }
        public string Location { get; set; }
        public string Company { get; set; }
        public string Active { get; set; }
        public string Deleted { get; set; }
    }
}