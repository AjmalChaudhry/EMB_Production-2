-- ============================================================
-- Production Plan module
-- ------------------------------------------------------------
-- Multiple plans per (Branch, Year, WeekNo) are allowed — one
-- week can host several parallel plans for different machines
-- or product batches. The real safety is JO uniqueness:
-- UX_PPI_JobOrderActive prevents a single Job Order from
-- appearing on two ACTIVE plans simultaneously.
--
-- Aggregates pending Job Orders into a weekly schedule, allocates
-- them to machines, and on Approve auto-generates the
-- corresponding Work Orders.
--
-- Idempotent — re-running is safe.
--
-- After running:
--   1) (Optional) Refresh the EDMX so db.ProductionPlan /
--      db.ProductionPlanItems are available. The controller
--      reads via raw SQL so the build is NOT blocked on this.
--   2) Rebuild MVC + hard-refresh the SPA.
-- ============================================================

USE [EMB_Production];
GO

-- -----------------------------------------------------------
-- 1. Header — one row per Branch x Year x WeekNo
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProductionPlan')
BEGIN
    CREATE TABLE dbo.ProductionPlan
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductionPlan PRIMARY KEY,
        PlanCode          NVARCHAR(50)     NOT NULL,
        BranchId          INT              NULL,

        -- ISO 8601 week of year (1..53) + 4-digit year
        [Year]            INT              NOT NULL,
        WeekNo            INT              NOT NULL,
        WeekStartDate     DATE             NOT NULL,
        WeekEndDate       DATE             NOT NULL,

        Status            NVARCHAR(20)     NOT NULL CONSTRAINT DF_PP_Status DEFAULT('DRAFT'),
                                                       -- DRAFT / APPROVED / IN_PROGRESS / CLOSED

        Remarks           NVARCHAR(500)    NULL,

        ApprovedBy        INT              NULL,
        ApprovedDate      DATETIME         NULL,
        ClosedBy          INT              NULL,
        ClosedDate        DATETIME         NULL,

        Active            BIT              NULL CONSTRAINT DF_PP_Active  DEFAULT(1),
        Deleted           BIT              NULL CONSTRAINT DF_PP_Deleted DEFAULT(0),
        CreatedBy         INT              NULL,
        CreatedDate       DATETIME         NULL,
        UpdatedBy         INT              NULL,
        UpdatedDate       DATETIME         NULL,
        RecordTimeStamp   DATETIME         NULL,

        CONSTRAINT FK_PP_Branch FOREIGN KEY (BranchId)
            REFERENCES dbo.Branches(Id)
    );

    CREATE UNIQUE INDEX UX_PP_PlanCode ON dbo.ProductionPlan (PlanCode);
    -- Non-unique index on (Branch, Year, WeekNo) to support the
    -- duplicate-week warning lookup and weekly list filters.
    -- Uniqueness is NOT enforced here — multiple parallel plans
    -- per week are a supported workflow.
    CREATE INDEX IX_PP_BranchWeek
        ON dbo.ProductionPlan (BranchId, [Year], WeekNo)
        WHERE Deleted = 0;
    CREATE INDEX IX_PP_Status ON dbo.ProductionPlan (Status);
END
GO

-- -----------------------------------------------------------
-- 2. Items — one row per (JO or Stock) allocation to a machine
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProductionPlanItems')
BEGIN
    CREATE TABLE dbo.ProductionPlanItems
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PPI PRIMARY KEY,
        PlanId            INT              NOT NULL,
        JobOrderId        INT              NULL,           -- NULL for Company Stock items
        IsCompanyStockOrder BIT            NOT NULL CONSTRAINT DF_PPI_IsStock DEFAULT(0),

        ProductId         INT              NULL,
        DesignId          INT              NULL,

        PlannedQty        INT              NOT NULL,
        MachineId         INT              NULL,

        PlannedStartDate  DATE             NULL,
        PlannedEndDate    DATE             NULL,

        GeneratedWorkOrderId INT           NULL,           -- set on Plan Approve
        Status            NVARCHAR(20)     NOT NULL CONSTRAINT DF_PPI_Status DEFAULT('PENDING'),
                                                              -- PENDING / GENERATED / CANCELLED

        Remarks           NVARCHAR(300)    NULL,

        Active            BIT              NULL CONSTRAINT DF_PPI_Active  DEFAULT(1),
        Deleted           BIT              NULL CONSTRAINT DF_PPI_Deleted DEFAULT(0),
        CreatedBy         INT              NULL,
        CreatedDate       DATETIME         NULL,
        UpdatedBy         INT              NULL,
        UpdatedDate       DATETIME         NULL,

        CONSTRAINT FK_PPI_Plan      FOREIGN KEY (PlanId)
            REFERENCES dbo.ProductionPlan(Id),
        CONSTRAINT FK_PPI_JobOrder  FOREIGN KEY (JobOrderId)
            REFERENCES dbo.Gen_JobOrders(Id),
        CONSTRAINT FK_PPI_Product   FOREIGN KEY (ProductId)
            REFERENCES dbo.Gen_Products(Id),
        CONSTRAINT FK_PPI_Design    FOREIGN KEY (DesignId)
            REFERENCES dbo.Gen_Design(Id),
        CONSTRAINT FK_PPI_Machine   FOREIGN KEY (MachineId)
            REFERENCES dbo.Gen_Machines(Id),
        CONSTRAINT FK_PPI_WorkOrder FOREIGN KEY (GeneratedWorkOrderId)
            REFERENCES dbo.WorkOrders(Id)
    );

    -- A Job Order can only sit in one ACTIVE plan at a time.
    -- Filtered-index predicate vocabulary is limited (no OR allowed,
    -- see comment on UX_PP_BranchWeek above). Deleted has DEFAULT(0)
    -- so newly-inserted rows always have 0, not NULL — plain `= 0` is
    -- correct here.
    CREATE UNIQUE INDEX UX_PPI_JobOrderActive
        ON dbo.ProductionPlanItems (JobOrderId)
        WHERE JobOrderId IS NOT NULL
              AND Deleted = 0
              AND Status <> 'CANCELLED';

    CREATE INDEX IX_PPI_PlanId    ON dbo.ProductionPlanItems (PlanId);
    CREATE INDEX IX_PPI_MachineId ON dbo.ProductionPlanItems (MachineId);
END
GO

-- -----------------------------------------------------------
-- 3. Smoke test
-- -----------------------------------------------------------
SELECT TOP 5
    p.Id, p.PlanCode, p.[Year], p.WeekNo, p.Status,
    (SELECT COUNT(*) FROM dbo.ProductionPlanItems i
     WHERE i.PlanId = p.Id AND (i.Deleted IS NULL OR i.Deleted = 0)) AS ItemCount
FROM   dbo.ProductionPlan p
ORDER  BY p.Id DESC;
GO
