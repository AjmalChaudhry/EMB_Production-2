using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class ServiceRowViewModel
    {
        public int Id { get; set; }
        public int? ServiceTypeId { get; set; }
        public string ServiceTypeName { get; set; }
        public string ServiceName { get; set; }
        public int? ServiceRateCharge { get; set; }
        public int? MachineDevice { get; set; }
        public string MaxRange { get; set; }
        public decimal? MaxCharges { get; set; }
        public decimal? MinCharges { get; set; }
        public decimal? Field1 { get; set; }
        public decimal? Field2 { get; set; }
        public int? BranchId { get; set; }
        public string BranchName { get; set; }
        public DateTime? RecordTimeStamp { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}