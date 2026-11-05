using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class IssueMaterialDto
    {
        public int WorkOrderId { get; set; }
        public string Remarks { get; set; }
        public List<IssueMaterialItemDto> Items { get; set; } = new List<IssueMaterialItemDto>();
    }

    public class IssueMaterialItemDto
    {
        public int ThreadId { get; set; }
        public decimal Qty { get; set; }
        public decimal? Rate { get; set; }
        public string Remarks { get; set; }
    }
}
