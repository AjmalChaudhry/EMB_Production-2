-- ============================================================
-- Drop the (Branch, Year, WeekNo) UNIQUE index on dbo.ProductionPlan.
-- ------------------------------------------------------------
-- The original schema enforced one plan per (Branch, Year, Week).
-- Operations need parallel plans per week (different machines /
-- product batches), so the uniqueness is being relaxed to a
-- non-unique index. JO double-allocation is still blocked by
-- UX_PPI_JobOrderActive on ProductionPlanItems.
--
-- Idempotent — re-running is safe.
-- ============================================================

USE [EMB_Production];
GO

-- Filtered indexes require QUOTED_IDENTIFIER ON. sqlcmd defaults
-- this to OFF, which would make the CREATE INDEX below fail with
-- "SET options have incorrect settings: 'QUOTED_IDENTIFIER'".
-- SSMS sets it ON by default, but be explicit so the script works
-- under either caller.
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_PP_BranchWeek'
      AND object_id = OBJECT_ID('dbo.ProductionPlan')
)
BEGIN
    DROP INDEX UX_PP_BranchWeek ON dbo.ProductionPlan;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PP_BranchWeek'
      AND object_id = OBJECT_ID('dbo.ProductionPlan')
)
BEGIN
    CREATE INDEX IX_PP_BranchWeek
        ON dbo.ProductionPlan (BranchId, [Year], WeekNo)
        WHERE Deleted = 0;
END
GO
