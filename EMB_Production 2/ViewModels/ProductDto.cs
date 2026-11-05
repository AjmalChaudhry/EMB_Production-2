using Newtonsoft.Json;
using System;

namespace EMB_Production.Models
{
    public class ProductDto
    {
        public int Id { get; set; }
        public int? CatId { get; set; }
        public string Prod_Name { get; set; }
        public string Prod_Code { get; set; }
        public string Prod_Description { get; set; }
        public int? Prod_Qty { get; set; }
        public decimal? Prod_Length { get; set; }
        public decimal? Prod_Weight { get; set; }
        public DateTime? Prod_DateIn { get; set; }
        public DateTime? Prod_DateOut { get; set; }
        public decimal? Price { get; set; }
        public int? BranchId { get; set; }
        public bool Active { get; set; }
    }

}
