using System;
using EMB_Production.Models;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    // ========================================================
    // ROW VIEWMODEL (List + Detail)
    // ========================================================
    public class ServiceRateRowViewModel
    {
        public int Id { get; set; }

        // FK — Product
        public int? ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductCode { get; set; }

        // FK — Service
        public int? ServiceId { get; set; }
        public string ServiceName { get; set; }

        // Fields
        public string ChargeFactor { get; set; }
        public string Remarks { get; set; }

        // FK — Branch
        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public DateTime? RecordTimeStamp { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }

        public ServiceRateRowViewModel() { }
    }


    public class ServiceRatesDto
    {
        public int Id { get; set; }

        public int? ProductId { get; set; }
        public int? ServiceId { get; set; }

        public string ChargeFactor { get; set; }
        public string Remarks { get; set; }

        public int? BranchId { get; set; }

        public bool Active { get; set; }
    }
}
