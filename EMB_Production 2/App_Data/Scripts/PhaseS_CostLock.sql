/* =====================================================================
   Phase S.1 — Cost Lock at Post Time
   ---------------------------------------------------------------------
   Adds 4 columns to DailyProduction so the cost accrued at PostToWip
   time is locked into the row itself. After this, reversal can use the
   stored values instead of recomputing from MachineCostProfile —
   important when profile rates change between post and reverse.

   APPLY ONCE against the EMB_Production database, then refresh the
   EDMX in Visual Studio (Update Model from Database → DailyProduction).
   ===================================================================== */

USE [EMB_Production];
GO

-- Snapshot of cost added to the parent WorkOrderItem when this row was posted.
ALTER TABLE [dbo].[DailyProduction]
ADD
    [PostedMachineCost]     DECIMAL(18, 2) NULL,
    [PostedLaborCost]       DECIMAL(18, 2) NULL,
    [PostedElectricityCost] DECIMAL(18, 2) NULL,
    [PostedTotalCost]       DECIMAL(18, 2) NULL;
GO

/* Optional — back-fill the snapshot for existing posted rows.
   Computes (productive minutes × machine cost profile hourly rates) and
   writes the values. Comment out if you'd rather leave history blank.
*/
;WITH stop_min AS (
    SELECT ProductionId,
           SUM(CASE WHEN StopEnd IS NOT NULL AND StopEnd > StopStart
                    THEN DATEDIFF(MINUTE, StopStart, StopEnd) ELSE 0 END) AS MinutesStopped
    FROM dbo.DailyProductionStops
    GROUP BY ProductionId
),
prof AS (
    SELECT c.MachineId, c.HourlyRate, c.OperatorHourlyRate, c.PowerKW, c.ElectricityUnitRate,
           ROW_NUMBER() OVER (PARTITION BY c.MachineId ORDER BY c.Id DESC) AS rn
    FROM dbo.MachineCostProfile c
    WHERE (c.Deleted IS NULL OR c.Deleted = 0)
      AND (c.Active  IS NULL OR c.Active  = 1)
)
UPDATE dp
SET
    PostedMachineCost     = ROUND( p.HourlyRate          * prodHours, 2 ),
    PostedLaborCost       = ROUND( p.OperatorHourlyRate  * prodHours, 2 ),
    PostedElectricityCost = ROUND( (p.PowerKW * p.ElectricityUnitRate) * prodHours, 2 ),
    PostedTotalCost       = ROUND( (p.HourlyRate + p.OperatorHourlyRate + (p.PowerKW * p.ElectricityUnitRate)) * prodHours, 2 )
FROM dbo.DailyProduction dp
JOIN prof p
  ON p.MachineId = dp.MachineId
 AND p.rn = 1
OUTER APPLY (
    SELECT
        CAST(
            CASE
                WHEN dp.StartTime IS NULL OR dp.EndTime IS NULL OR dp.EndTime <= dp.StartTime THEN 0
                ELSE (
                    DATEDIFF(MINUTE, dp.StartTime, dp.EndTime)
                    - ISNULL((SELECT MinutesStopped FROM stop_min WHERE ProductionId = dp.Id), 0)
                ) / 60.0
            END
        AS DECIMAL(18,4)) AS prodHours
) h
WHERE dp.IsPosted = 1
  AND dp.PostedTotalCost IS NULL
  AND (dp.Deleted IS NULL OR dp.Deleted = 0);
GO
