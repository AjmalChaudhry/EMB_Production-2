using System;

namespace EMB_Production.ViewModels
{
    public class ThreadFormViewModel
    {
        public int? Id { get; set; }     

        public string Code { get; set; } 
        public string Name { get; set; }

        public string Type { get; set; }
        public string Material { get; set; }
        public string YarnCount { get; set; }
        public string Color { get; set; }
        public string ColorCode { get; set; }

        public decimal? UnitLength { get; set; }
        public string UnitOfMeasure { get; set; }
        public decimal? UnitCost { get; set; }

        public int? SupplierId { get; set; }
        public int? BranchId { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }
    }
}

