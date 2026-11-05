-- ============================================================
-- Purchase Requisition (PR) → Purchase Order (PO) → Goods
-- Receipt Note (GRN) workflow
-- ------------------------------------------------------------
-- Four new tables + one ALTER on the existing Purchases table.
-- Idempotent — re-running is safe.
--
-- Flow:
--   PurchaseRequisition (PR-YYYY-NNNN)
--       DRAFT → SUBMITTED → APPROVED → CONVERTED_TO_PO
--                                   ↘ REJECTED
--       │ (1:1 on Approve)
--       ▼
--   PurchaseOrder (PO-YYYY-NNNN)
--       DRAFT → SENT → PARTIALLY_RECEIVED → FULLY_RECEIVED → CLOSED
--                                                          ↘ CANCELLED
--       │ (1 PO → N GRNs, partial deliveries supported)
--       ▼
--   Purchases (existing table — now doubles as GRN layer)
--       New nullable column PurchaseOrderId links each receipt
--       back to its PO. Old purchases keep PurchaseOrderId = NULL
--       (ad-hoc, pre-PO era).
--
-- After running:
--   1) (Optional) Refresh the EDMX so the new entities are
--      first-class. Controllers read/write via raw SQL so the
--      build is NOT blocked on this.
--   2) Rebuild MVC + hard-refresh the SPA.
-- ============================================================

USE [EMB_Production];
GO

-- -----------------------------------------------------------
-- 1. PurchaseRequisition (header)
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PurchaseRequisition')
BEGIN
    CREATE TABLE dbo.PurchaseRequisition
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PR PRIMARY KEY,
        PrCode            NVARCHAR(50)     NOT NULL,
        BranchId          INT              NULL,

        RequestDate       DATETIME         NOT NULL CONSTRAINT DF_PR_RequestDate DEFAULT(SYSUTCDATETIME()),
        SupplierId        INT              NULL,            -- optional at request time

        -- Source of the requisition (audit + roll-up)
        SourceType        NVARCHAR(30)     NOT NULL CONSTRAINT DF_PR_SourceType DEFAULT('AD_HOC'),
                                                              -- AD_HOC / EMR_SHORTFALL / PLAN_SHORTFALL
        SourceReferenceId INT              NULL,             -- EMR.Id or ProductionPlan.Id

        Status            NVARCHAR(20)     NOT NULL CONSTRAINT DF_PR_Status DEFAULT('DRAFT'),
                                                              -- DRAFT / SUBMITTED / APPROVED / REJECTED / CONVERTED_TO_PO
        Remarks           NVARCHAR(500)    NULL,

        RequestedBy       INT              NULL,
        SubmittedDate     DATETIME         NULL,
        ApprovedBy        INT              NULL,
        ApprovedDate      DATETIME         NULL,
        RejectionReason   NVARCHAR(500)    NULL,

        ConvertedToPoId   INT              NULL,             -- FK back to PurchaseOrder.Id after conversion

        Active            BIT              NULL CONSTRAINT DF_PR_Active  DEFAULT(1),
        Deleted           BIT              NULL CONSTRAINT DF_PR_Deleted DEFAULT(0),
        CreatedBy         INT              NULL,
        CreatedDate       DATETIME         NULL,
        UpdatedBy         INT              NULL,
        UpdatedDate       DATETIME         NULL,
        RecordTimeStamp   DATETIME         NULL,

        CONSTRAINT FK_PR_Branch   FOREIGN KEY (BranchId)
            REFERENCES dbo.Branches(Id),
        CONSTRAINT FK_PR_Supplier FOREIGN KEY (SupplierId)
            REFERENCES dbo.Gen_Suppliers(Id)
    );

    CREATE UNIQUE INDEX UX_PR_PrCode ON dbo.PurchaseRequisition (PrCode);
    CREATE INDEX IX_PR_Status        ON dbo.PurchaseRequisition (Status);
    CREATE INDEX IX_PR_SupplierId    ON dbo.PurchaseRequisition (SupplierId);
END
GO

-- -----------------------------------------------------------
-- 2. PurchaseRequisitionItems (lines)
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PurchaseRequisitionItems')
BEGIN
    CREATE TABLE dbo.PurchaseRequisitionItems
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PRI PRIMARY KEY,
        PrId            INT              NOT NULL,
        RawItemId       INT              NOT NULL,
        UnitId          INT              NULL,
        Qty             DECIMAL(18,4)    NOT NULL,
        EstimatedRate   DECIMAL(18,4)    NULL,
        EstimatedAmount DECIMAL(18,4)    NULL,
        Remarks         NVARCHAR(200)    NULL,

        Active          BIT              NULL CONSTRAINT DF_PRI_Active  DEFAULT(1),
        Deleted         BIT              NULL CONSTRAINT DF_PRI_Deleted DEFAULT(0),
        CreatedBy       INT              NULL,
        CreatedDate     DATETIME         NULL,
        UpdatedBy       INT              NULL,
        UpdatedDate     DATETIME         NULL,

        CONSTRAINT FK_PRI_Header  FOREIGN KEY (PrId)
            REFERENCES dbo.PurchaseRequisition(Id),
        CONSTRAINT FK_PRI_RawItem FOREIGN KEY (RawItemId)
            REFERENCES dbo.Gen_RawItems(Id),
        CONSTRAINT FK_PRI_Unit    FOREIGN KEY (UnitId)
            REFERENCES dbo.Gen_Units(Id)
    );

    CREATE INDEX IX_PRI_PrId      ON dbo.PurchaseRequisitionItems (PrId);
    CREATE INDEX IX_PRI_RawItem   ON dbo.PurchaseRequisitionItems (RawItemId);
END
GO

-- -----------------------------------------------------------
-- 3. PurchaseOrder (header)
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PurchaseOrder')
BEGIN
    CREATE TABLE dbo.PurchaseOrder
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PO PRIMARY KEY,
        PoCode            NVARCHAR(50)     NOT NULL,
        PrId              INT              NULL,             -- 1:1 link back to PR; nullable for ad-hoc POs later
        BranchId          INT              NULL,
        SupplierId        INT              NOT NULL,

        OrderDate         DATETIME         NOT NULL CONSTRAINT DF_PO_OrderDate DEFAULT(SYSUTCDATETIME()),
        ExpectedDeliveryDate DATE          NULL,

        Status            NVARCHAR(25)     NOT NULL CONSTRAINT DF_PO_Status DEFAULT('DRAFT'),
                                                              -- DRAFT / SENT / PARTIALLY_RECEIVED / FULLY_RECEIVED / CLOSED / CANCELLED

        TotalAmount       DECIMAL(18,2)    NOT NULL CONSTRAINT DF_PO_TotalAmount DEFAULT(0),
        ReceivedAmount    DECIMAL(18,2)    NOT NULL CONSTRAINT DF_PO_ReceivedAmount DEFAULT(0),

        PaymentTerms      NVARCHAR(200)    NULL,
        DeliveryTerms     NVARCHAR(200)    NULL,
        Remarks           NVARCHAR(500)    NULL,

        SentBy            INT              NULL,
        SentDate          DATETIME         NULL,
        ClosedBy          INT              NULL,
        ClosedDate        DATETIME         NULL,
        CancelledBy       INT              NULL,
        CancelledDate     DATETIME         NULL,
        CancellationReason NVARCHAR(500)   NULL,

        Active            BIT              NULL CONSTRAINT DF_PO_Active  DEFAULT(1),
        Deleted           BIT              NULL CONSTRAINT DF_PO_Deleted DEFAULT(0),
        CreatedBy         INT              NULL,
        CreatedDate       DATETIME         NULL,
        UpdatedBy         INT              NULL,
        UpdatedDate       DATETIME         NULL,
        RecordTimeStamp   DATETIME         NULL,

        CONSTRAINT FK_PO_PR       FOREIGN KEY (PrId)
            REFERENCES dbo.PurchaseRequisition(Id),
        CONSTRAINT FK_PO_Branch   FOREIGN KEY (BranchId)
            REFERENCES dbo.Branches(Id),
        CONSTRAINT FK_PO_Supplier FOREIGN KEY (SupplierId)
            REFERENCES dbo.Gen_Suppliers(Id)
    );

    CREATE UNIQUE INDEX UX_PO_PoCode  ON dbo.PurchaseOrder (PoCode);
    CREATE INDEX IX_PO_Status         ON dbo.PurchaseOrder (Status);
    CREATE INDEX IX_PO_SupplierId     ON dbo.PurchaseOrder (SupplierId);
    CREATE INDEX IX_PO_PrId           ON dbo.PurchaseOrder (PrId);
END
GO

-- -----------------------------------------------------------
-- 4. PurchaseOrderItems (lines, with running ReceivedQty)
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PurchaseOrderItems')
BEGIN
    CREATE TABLE dbo.PurchaseOrderItems
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_POI PRIMARY KEY,
        PoId            INT              NOT NULL,
        PrItemId        INT              NULL,            -- audit trail back to PR line
        RawItemId       INT              NOT NULL,
        UnitId          INT              NULL,
        OrderedQty      DECIMAL(18,4)    NOT NULL,
        Rate            DECIMAL(18,4)    NOT NULL,
        Amount          DECIMAL(18,4)    NOT NULL,

        ReceivedQty     DECIMAL(18,4)    NOT NULL CONSTRAINT DF_POI_ReceivedQty DEFAULT(0),
                                                              -- summed across GRNs as receipts roll in

        Remarks         NVARCHAR(200)    NULL,

        Active          BIT              NULL CONSTRAINT DF_POI_Active  DEFAULT(1),
        Deleted         BIT              NULL CONSTRAINT DF_POI_Deleted DEFAULT(0),
        CreatedBy       INT              NULL,
        CreatedDate     DATETIME         NULL,
        UpdatedBy       INT              NULL,
        UpdatedDate     DATETIME         NULL,

        CONSTRAINT FK_POI_Header  FOREIGN KEY (PoId)
            REFERENCES dbo.PurchaseOrder(Id),
        CONSTRAINT FK_POI_PRItem  FOREIGN KEY (PrItemId)
            REFERENCES dbo.PurchaseRequisitionItems(Id),
        CONSTRAINT FK_POI_RawItem FOREIGN KEY (RawItemId)
            REFERENCES dbo.Gen_RawItems(Id),
        CONSTRAINT FK_POI_Unit    FOREIGN KEY (UnitId)
            REFERENCES dbo.Gen_Units(Id)
    );

    CREATE INDEX IX_POI_PoId    ON dbo.PurchaseOrderItems (PoId);
    CREATE INDEX IX_POI_RawItem ON dbo.PurchaseOrderItems (RawItemId);
END
GO

-- -----------------------------------------------------------
-- 5. ALTER existing Purchases — add PurchaseOrderId link
-- ------------------------------------------------------------
-- Each Purchase (GRN) now optionally points back at its PO.
-- Old purchases stay PurchaseOrderId = NULL (ad-hoc / pre-PO).
-- ------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'Purchase' AND c.name = 'PurchaseOrderId'
)
BEGIN
    ALTER TABLE dbo.Purchase
        ADD PurchaseOrderId INT NULL;

    CREATE INDEX IX_Purchase_PoId ON dbo.Purchase (PurchaseOrderId);
END
GO

-- The FK is added separately so the ALTER stays idempotent even if
-- the index already exists from a prior partial run.
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Purchase_PO'
)
AND EXISTS (
    SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'Purchase' AND c.name = 'PurchaseOrderId'
)
BEGIN
    ALTER TABLE dbo.Purchase
        ADD CONSTRAINT FK_Purchase_PO FOREIGN KEY (PurchaseOrderId)
            REFERENCES dbo.PurchaseOrder(Id);
END
GO

-- -----------------------------------------------------------
-- 6. PurchaseItems gets a PoItemId — links the GRN line to
--    its parent PO line so ReceivedQty can be rolled up.
-- -----------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'PurchaseItems' AND c.name = 'PoItemId'
)
BEGIN
    ALTER TABLE dbo.PurchaseItems
        ADD PoItemId INT NULL;

    CREATE INDEX IX_PurchaseItems_PoItemId ON dbo.PurchaseItems (PoItemId);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PurchaseItems_POItem'
)
AND EXISTS (
    SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'PurchaseItems' AND c.name = 'PoItemId'
)
BEGIN
    ALTER TABLE dbo.PurchaseItems
        ADD CONSTRAINT FK_PurchaseItems_POItem FOREIGN KEY (PoItemId)
            REFERENCES dbo.PurchaseOrderItems(Id);
END
GO

-- -----------------------------------------------------------
-- 7. Smoke test
-- -----------------------------------------------------------
SELECT TOP 5
    pr.Id, pr.PrCode, pr.Status,
    (SELECT COUNT(*) FROM dbo.PurchaseRequisitionItems i WHERE i.PrId = pr.Id) AS ItemCount,
    po.Id AS PoId, po.PoCode, po.Status AS PoStatus
FROM   dbo.PurchaseRequisition pr
LEFT JOIN dbo.PurchaseOrder po ON po.PrId = pr.Id
ORDER  BY pr.Id DESC;
GO
