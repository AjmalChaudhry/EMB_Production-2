using System;
using System.Linq;
using EMB_Production.Models;
using EMB_Production.ViewModels;

public partial class DesignCostingService
{
    private readonly EMB_ProductionEntities1 db;

    public DesignCostingService(EMB_ProductionEntities1 context)
    {
        db = context;
    }

    public DesignCostCalculationResult CalculateDesignCost(int designId, int batchQty, decimal marginPercent = 0)
    {
        if (designId <= 0)
            throw new Exception("Invalid design.");

        if (batchQty <= 0)
            throw new Exception("Batch quantity must be greater than zero.");

        var design = db.Gen_Design
            .FirstOrDefault(x => x.Id == designId && x.Deleted != true);

        if (design == null)
            throw new Exception("Design not found.");

        if (!design.MachineId.HasValue || design.MachineId.Value <= 0)
            throw new Exception("Machine not assigned to design.");

        int machineId = design.MachineId.Value;

        // =====================================================
        // STITCH CALCULATION (FROM BOM COMPONENTS)
        // IMPORTANT:
        // Use Gen_BOM -> Gen_Components
        // NOT Gen_Components.D_Code == designId
        // =====================================================
           // Phase 3.2: stitch/tilla now live on Gen_ComponentSpecs (latest PRODUCTION)
           decimal stitchesPerPiece = (
                from bom in db.Gen_BOM
                join comp in db.Gen_Components on bom.CompId equals comp.Id
                join spec in db.Gen_ComponentSpecs on comp.Id equals spec.ComponentId
                where bom.DesignId == designId
                      && (bom.Deleted == false || bom.Deleted == null)
                      && (comp.Deleted == false || comp.Deleted == null)
                      && (comp.Active == true || comp.Active == null)
                      && spec.Variant == "PRODUCTION"
                      && spec.IsLatest
                      && !spec.Deleted
                select (decimal?)(
                    (spec.Stitch ?? 0)
                    + (spec.Tilla ?? 0)
                )
            ).DefaultIfEmpty(0m).Sum() ?? 0m;

        if (stitchesPerPiece <= 0)
        {
            return new DesignCostCalculationResult
            {
                ThreadCost = 0,
                MachineCost = 0,
                LaborCost = 0,
                ElectricityCost = 0,
                OtherCost = 0,
                TotalCost = 0,
                CostPerUnit = 0,
                CostPer1000Stitches = 0,
                ThreadMetersPerPiece = 0,
                TotalThreadMeters = 0,
                ProductionMinutes = 0,
                ProductionHours = 0,
                PiecesPerHour = 0,
                PiecesPerShift = 0,
                DailyCapacity = 0,
                ProductionDays = 0,
                MarginPercent = marginPercent,
                SellingPricePerUnit = 0,
                TotalSellingPrice = 0,
                ErrorMessage = "Design stitch count not configured. Please add stitches in BOM components."
            };
        }

        decimal totalStitches = stitchesPerPiece * batchQty;

        // =====================================================
        // MACHINE
        // =====================================================
        var machine = db.Gen_Machines
            .FirstOrDefault(x => x.Id == machineId && x.Deleted != true);

        if (machine == null)
            throw new Exception("Machine not found.");

        int heads = machine.HeadsNum ?? 1;
        if (heads <= 0) heads = 1;

        // =====================================================
        // MACHINE COST PROFILE
        // =====================================================
        var profile = db.MachineCostProfile
            .Where(x => x.MachineId == machineId && x.Active == true && x.Deleted != true)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefault();

        if (profile == null)
            throw new Exception("Machine cost profile not configured.");

        if (!profile.StitchPerMinute.HasValue || profile.StitchPerMinute.Value <= 0)
            throw new Exception("Machine stitch speed not configured.");

        decimal spm = profile.StitchPerMinute.Value;

        // =====================================================
        // FACTORY CONSTANTS
        // =====================================================
        decimal complexityFactor = 0.12m;   // 12%
        decimal setupMinutes = 12m;         // batch setup
        decimal efficiency = 0.85m;         // 85%
        decimal overheadPercent = 5m;       // 5%
        decimal shiftHours = 8m;
        decimal shiftsPerDay = 2m;

        // =====================================================
        // THREAD METERS + THREAD COST
        // Source: Gen_BOM × Gen_ComponentMaterials (unified BOM path).
        // Filter to thread-backed rows by joining Gen_Threads on
        // RawItemId — that join also yields the UnitCost we need for
        // the per-piece thread cost.
        // (Was previously Gen_ThreadUsage; retired because its Usage
        // values diverged from production reality.)
        // =====================================================
        decimal threadMetersPerPiece = (
            from bom in db.Gen_BOM
            join cm in db.Gen_ComponentMaterials on bom.CompId equals cm.CompId
            join t in db.Gen_Threads on cm.RawItemId equals t.RawItemId
            where bom.DesignId == designId
                  && (bom.Deleted == false || bom.Deleted == null)
                  && cm.Deleted == false
                  && cm.Active == true
                  && t.Deleted != true
                  && (t.Active == true || t.Active == null)
            select (decimal?)cm.QtyPerUnit
        ).DefaultIfEmpty(0m).Sum() ?? 0m;

        decimal totalThreadMeters = threadMetersPerPiece * batchQty;

        decimal threadCostPerPiece = (
            from bom in db.Gen_BOM
            join cm in db.Gen_ComponentMaterials on bom.CompId equals cm.CompId
            join t in db.Gen_Threads on cm.RawItemId equals t.RawItemId
            where bom.DesignId == designId
                  && (bom.Deleted == false || bom.Deleted == null)
                  && cm.Deleted == false
                  && cm.Active == true
                  && t.Deleted != true
                  && (t.Active == true || t.Active == null)
            select (decimal?)(cm.QtyPerUnit * (t.UnitCost ?? 0m))
        ).DefaultIfEmpty(0m).Sum() ?? 0m;

        decimal threadCost = threadCostPerPiece * batchQty;

        // =====================================================
        // INDUSTRY / PRODUCTION FORMULA
        // =====================================================
        decimal runningCostPerHour =
              (profile.HourlyRate ?? 0m)
            + (profile.OperatorHourlyRate ?? 0m)
            + ((profile.PowerKW ?? 0m) * (profile.ElectricityUnitRate ?? 0m));

        decimal costPerStitch = runningCostPerHour / (spm * 60m);
        decimal costPer1000Stitches = costPerStitch * 1000m;

        decimal effectiveStitches = totalStitches / heads;
        decimal stitchMinutes = effectiveStitches / spm;
        decimal runMinutes = stitchMinutes * (1 + complexityFactor);
        decimal totalMinutes = (runMinutes + setupMinutes) / efficiency;
        decimal totalHours = totalMinutes / 60m;

        // =====================================================
        // PRODUCTION CAPACITY
        // =====================================================
        decimal piecesPerHour = 0m;
        if (stitchesPerPiece > 0)
        {
            piecesPerHour = ((spm * 60m) * heads * efficiency) / stitchesPerPiece;
        }

        decimal piecesPerShift = piecesPerHour * shiftHours;
        decimal dailyCapacity = piecesPerShift * shiftsPerDay;

        decimal productionDays = 0m;
        if (batchQty > 0 && dailyCapacity > 0)
        {
            productionDays = batchQty / dailyCapacity;
        }

        // =====================================================
        // COST BREAKDOWN
        // =====================================================
        decimal machineRate = profile.HourlyRate ?? 0m;
        decimal laborRate = profile.OperatorHourlyRate ?? 0m;
        decimal electricRatePerHour = (profile.PowerKW ?? 0m) * (profile.ElectricityUnitRate ?? 0m);

        decimal machineCost = totalHours * (machineRate / heads);
        decimal laborCost = totalHours * laborRate;
        decimal electricityCost = totalHours * electricRatePerHour;

        decimal otherCost =
            (threadCost + machineCost + laborCost + electricityCost)
            * (overheadPercent / 100m);

        // =====================================================
        // TOTAL + SELLING
        // =====================================================
        decimal totalCost =
              threadCost
            + machineCost
            + laborCost
            + electricityCost
            + otherCost;

        decimal costPerUnit = totalCost / batchQty;

        decimal appliedMargin = marginPercent > 0 ? marginPercent : 20m;

        decimal sellingPricePerUnit =
            costPerUnit + (costPerUnit * appliedMargin / 100m);

        decimal totalSellingPrice = sellingPricePerUnit * batchQty;

        // =====================================================
        // RESULT
        // =====================================================
        return new DesignCostCalculationResult
        {
            ThreadCost = Math.Round(threadCost, 2),
            MachineCost = Math.Round(machineCost, 2),
            LaborCost = Math.Round(laborCost, 2),
            ElectricityCost = Math.Round(electricityCost, 2),
            OtherCost = Math.Round(otherCost, 2),

            TotalCost = Math.Round(totalCost, 2),
            CostPerUnit = Math.Round(costPerUnit, 4),

            CostPer1000Stitches = Math.Round(costPer1000Stitches, 2),

            ThreadMetersPerPiece = Math.Round(threadMetersPerPiece, 2),
            TotalThreadMeters = Math.Round(totalThreadMeters, 2),

            ProductionMinutes = Math.Round(totalMinutes, 2),
            ProductionHours = Math.Round(totalHours, 2),

            PiecesPerHour = Math.Round(piecesPerHour, 2),
            PiecesPerShift = Math.Round(piecesPerShift, 0),
            DailyCapacity = Math.Round(dailyCapacity, 0),
            ProductionDays = Math.Round(productionDays, 2),

            MarginPercent = appliedMargin,
            SellingPricePerUnit = Math.Round(sellingPricePerUnit, 2),
            TotalSellingPrice = Math.Round(totalSellingPrice, 2),

            ErrorMessage = null
        };
    }
}