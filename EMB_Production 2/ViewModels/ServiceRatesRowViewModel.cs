using EMB_Production.Models;
using EMB_Production_2.Models;
using System;

public class ServiceRatesRowViewModel
{
    public int Id { get; set; }

    public int? ProductId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }

    public int? ServiceId { get; set; }
    public string ServiceName { get; set; }

    public string ChargeFactor { get; set; }
    public string Remarks { get; set; }

    public int? BranchId { get; set; }
    public string BranchName { get; set; }

    public bool Active { get; set; }
    public bool Deleted { get; set; }

    public DateTime? RecordTimeStamp { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public ServiceRatesRowViewModel() { }
}
