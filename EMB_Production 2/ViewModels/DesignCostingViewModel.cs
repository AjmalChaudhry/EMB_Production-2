using System;

namespace EMB_Production.ViewModels
{
    public class DesignCostingListRowViewModel
    {
        public int Id { get; set; }

        public int DesignId { get; set; }
        public string DesignName { get; set; } = "";

        public int? ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int BatchQty { get; set; }

        public decimal? ThreadCost { get; set; }
        public decimal? MachineCost { get; set; }
        public decimal? LaborCost { get; set; }
        public decimal? ElectricityCost { get; set; }
        public decimal? OtherCost { get; set; }

        public decimal? TotalCost { get; set; }
        public decimal? CostPerUnit { get; set; }

        public decimal? ThreadPerUnit { get; set; }
        public decimal? StocksMaterialUnit { get; set; }
        public decimal? MaterialCostPerUnit { get; set; }
        public decimal? ProductionCostPerUnit { get; set; }
        public decimal? ServiceCostPerUnit { get; set; }
        public decimal? ProductCostPerUnit { get; set; }
        public decimal? TotalProductCost { get; set; }

        public decimal? MarginPercent { get; set; }
        public decimal? SellingPricePerUnit { get; set; }
        public decimal? TotalSellingPrice { get; set; }

        public bool? Active { get; set; }

        public string CreatedDateText { get; set; } = "";
        public string UpdatedDateText { get; set; } = "";
    }

    public class DesignCostingDetailViewModel
    {
        public int Id { get; set; }

        public int DesignId { get; set; }
        public string DesignName { get; set; } = "";

        public int? ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int BatchQty { get; set; }

        public decimal? ThreadCost { get; set; }
        public decimal? MachineCost { get; set; }
        public decimal? LaborCost { get; set; }
        public decimal? ElectricityCost { get; set; }
        public decimal? OtherCost { get; set; }

        public decimal? TotalCost { get; set; }
        public decimal? CostPerUnit { get; set; }

        public decimal? ThreadPerUnit { get; set; }
        public decimal? StocksMaterialUnit { get; set; }
        public decimal? MaterialCostPerUnit { get; set; }
        public decimal? ProductionCostPerUnit { get; set; }
        public decimal? ServiceCostPerUnit { get; set; }
        public decimal? ProductCostPerUnit { get; set; }
        public decimal? TotalProductCost { get; set; }

        public decimal? MarginPercent { get; set; }
        public decimal? SellingPricePerUnit { get; set; }
        public decimal? TotalSellingPrice { get; set; }

        public decimal? ThreadMetersPerPiece { get; set; }
        public decimal? TotalThreadMeters { get; set; }
        public decimal? CostPer1000Stitches { get; set; }

        public decimal? ProductionMinutes { get; set; }
        public decimal? ProductionHours { get; set; }
        public decimal? PiecesPerHour { get; set; }

        public decimal? PiecesPerShift { get; set; }
        public decimal? DailyCapacity { get; set; }
        public decimal? ProductionDays { get; set; }

        public bool? Active { get; set; }

        public string CreatedByEmployeeId { get; set; } = "";
        public string UpdatedByEmployeeId { get; set; } = "";
        public string CreatedDateText { get; set; } = "";
        public string UpdatedDateText { get; set; } = "";
    }

    public class DesignCostCalculationResult
    {
        public decimal ThreadCost { get; set; }
        public decimal MachineCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal ElectricityCost { get; set; }
        public decimal OtherCost { get; set; }

        public decimal TotalCost { get; set; }
        public decimal CostPerUnit { get; set; }

        public decimal ThreadPerUnit { get; set; }
        public decimal StocksMaterialUnit { get; set; }
        public decimal MaterialCostPerUnit { get; set; }
        public decimal ProductionCostPerUnit { get; set; }
        public decimal ServiceCostPerUnit { get; set; }
        public decimal ProductCostPerUnit { get; set; }
        public decimal TotalProductCost { get; set; }

        public decimal MarginPercent { get; set; }
        public decimal SellingPricePerUnit { get; set; }
        public decimal TotalSellingPrice { get; set; }

        public decimal CostPer1000Stitches { get; set; }

        public decimal ThreadMetersPerPiece { get; set; }
        public decimal TotalThreadMeters { get; set; }

        public decimal ProductionMinutes { get; set; }
        public decimal ProductionHours { get; set; }

        public decimal PiecesPerHour { get; set; }
        public decimal PiecesPerShift { get; set; }
        public decimal DailyCapacity { get; set; }
        public decimal ProductionDays { get; set; }

        public string ErrorMessage { get; set; } = "";
    }

    public class RecalculateCostingVm
    {
        public int DesignId { get; set; }
        public int BatchQty { get; set; }
        public decimal? MarginPercent { get; set; }
        public int? ProductId { get; set; }
    }
}