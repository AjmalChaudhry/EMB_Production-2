using System;

namespace EMB_Production.ViewModels
{
    public class ProductDetailViewModel
    {
        public int Id { get; set; }
        public int? CatId { get; set; }
        public int? BranchId { get; set; }

        public string CategoryName { get; set; }
        public string BranchName { get; set; }

        public string ProdName { get; set; }
        public string ProdCode { get; set; }
        public string ProdDescription { get; set; }

        public int? ProdQty { get; set; }
        public decimal? ProdLength { get; set; }
        public decimal? ProdWeight { get; set; }
        public decimal? Price { get; set; }

        public DateTime? ProdDateIn { get; set; }
        public DateTime? ProdDateOut { get; set; }

        // ✅ UNIT LABELS (FROM CATEGORY)
        public string QtyUnit { get; set; }
        public string LengthUnit { get; set; }
        public string WeightUnit { get; set; }
        public string PriceUnit { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public int? UpdatedByEmployeeId { get; set; }

        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }
        public DateTime? RecordTimeStamp { get; set; }
    }

}
