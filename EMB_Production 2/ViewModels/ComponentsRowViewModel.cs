using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class ComponentsRowViewModel
    {
        public int Id { get; set; }

        public int? Code { get; set; }
        public int? D_Code { get; set; }

        public string Name { get; set; }

        public int? HeadToHead { get; set; }
        public int? Stitch { get; set; }
        public int? Tilla { get; set; }
        public int? Seq_A { get; set; }
        public int? Seq_B { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? BranchId { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    // =====================================================
    // DTO (SAFE & MINIMAL)
    // =====================================================
    public class ComponentCreateUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public int DesignId { get; set; }

        public int HeadToHead { get; set; }
        public int Stitch { get; set; }
        public int Tilla { get; set; }
        public int SeqA { get; set; }
        public int SeqB { get; set; }

        // Phase 1 — embroidery spec fields
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public int? Colors { get; set; }
        public int? MachineHead { get; set; }
        public int? Repeat { get; set; }
        public decimal? HSpace { get; set; }
        public int? UPS { get; set; }

        public int BranchId { get; set; }
        public bool Active { get; set; }
    }

    public class ComponentIdDto
    {
        public int Id { get; set; }
    }
}




