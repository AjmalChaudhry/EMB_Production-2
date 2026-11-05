using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class BomMaterialViewModel
    {
        public int? RawItemId { get; set; }
        public string RawItemName { get; set; }
        public decimal QtyPerUnit { get; set; }
        public string UnitCode { get; set; }
        public decimal TotalQty { get; set; }
    }

    public class GenBomRowViewModel
    {
        public int Id { get; set; }

        public int DesignId { get; set; }
        public int CompId { get; set; }

        // Display values
        public string DesignName { get; set; }
        public string DesignCode { get; set; }

        public string ComponentName { get; set; }
        public string ComponentCode { get; set; }

        public int? ReqRepeats { get; set; }
        public int? ReqHead { get; set; }
        public int? Panni { get; set; }
        public int? Solving { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }
    }

    // =============================
    // DETAIL VIEW
    // =============================
    public class BomDetailViewModel
    {
        public int Id { get; set; }

        public int DesignId { get; set; }
        public string DesignName { get; set; }
        public string DesignCode { get; set; }

        public int CompId { get; set; }
        public string ComponentName { get; set; }
        public string ComponentCode { get; set; }

        public int? ReqRepeats { get; set; }
        public int? ReqHead { get; set; }
        public int? Panni { get; set; }
        public int? Solving { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public int? UpdatedByEmployeeId { get; set; }

        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }

        public List<BomMaterialViewModel> Materials { get; set; }

        public decimal MaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal MachineCost { get; set; }
        public decimal TotalCost { get; set; }
    }

    // =============================
    // CREATE / EDIT FORM
    // =============================
    public class BomFormViewModel
    {
        public int Id { get; set; }

        public int DesignId { get; set; }
        public int CompId { get; set; }

        public int? ReqRepeats { get; set; }
        public int? ReqHead { get; set; }
        public int? Panni { get; set; }
        public int? Solving { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
    }
}