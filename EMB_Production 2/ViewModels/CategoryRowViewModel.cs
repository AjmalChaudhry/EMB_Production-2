using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class CategoryRowViewModel
    {
        public int Id { get; set; }
        public string BrandName { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string Quantity { get; set; }
        public string Length { get; set; }
        public string Weight { get; set; }
        public decimal? Price { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public string BranchName { get; set; }
        public DateTime? RecordTimeStamp { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}