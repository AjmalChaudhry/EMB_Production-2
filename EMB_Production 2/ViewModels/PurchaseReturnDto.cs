using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class PurchaseReturnDto
    {
        public string Reason { get; set; }
        public List<PurchaseReturnLineDto> Lines { get; set; } = new List<PurchaseReturnLineDto>();
    }

    public class PurchaseReturnLineDto
    {
        public int PurchaseItemId { get; set; }
        public decimal ReturnQty { get; set; }
        public string Remarks { get; set; }
    }
}
