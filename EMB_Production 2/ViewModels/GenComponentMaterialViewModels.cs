using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class ComponentMaterialRowViewModel
    {
        public int Id { get; set; }

        public int CompId { get; set; }
        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int RawItemId { get; set; }
        public string RawItemName { get; set; }

        public decimal QtyPerUnit { get; set; }
        public int? UnitId { get; set; }
        public string UnitCode { get; set; }
        public string UnitName { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        // Sprint-1: row created within last 24h → drives the "NEW" badge.
        public bool IsRecent { get; set; }
    }

    public class ComponentMaterialDetailViewModel
    {
        public int Id { get; set; }

        public int CompId { get; set; }
        public string ComponentCode { get; set; }
        public string ComponentName { get; set; }

        public int RawItemId { get; set; }
        public string RawItemName { get; set; }

        public decimal QtyPerUnit { get; set; }
        public int? UnitId { get; set; }
        public string UnitCode { get; set; }
        public string UnitName { get; set; }

        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedByEmployeeId { get; set; }
        public int? UpdatedByEmployeeId { get; set; }

        public string CreatedDateText { get; set; }
        public string UpdatedDateText { get; set; }
    }

    public class ComponentMaterialCreateUpdateDto
    {
        public int Id { get; set; }

        public int CompId { get; set; }
        public int RawItemId { get; set; }
        public decimal QtyPerUnit { get; set; }
        public int? UnitId { get; set; }
        public string Remarks { get; set; }
        public bool Active { get; set; }
    }

    public class DropdownItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
    }
}