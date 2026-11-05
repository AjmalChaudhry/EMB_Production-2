-- ============================================================
-- Extra Material Request (EMR) module
-- ------------------------------------------------------------
-- Two tables: header + line items. Idempotent — re-running is safe.
--
-- Workflow:
--   DRAFT     → operator builds the request
--   SUBMITTED → operator hands it off for approval
--   APPROVED  → store keeper / production manager approves
--               (controller code immediately writes Gen_Store OUT
--                entries with ReferenceType='EMR_ISSUE' and rolls
--                the cost up to the parent Work Order)
--   REJECTED  → approver rejected with a reason; no stock movement
--
-- After running:
--   1) (Optional) Refresh the EDMX in Visual Studio so the entities
--      are available via db.ExtraMaterialRequest /
--      db.ExtraMaterialRequestItems. The controller reads via raw
--      SQL so the build is NOT blocked on the refresh.
--   2) Rebuild MVC + hard-refresh the SPA.
-- ============================================================

USE [EMB_Production];
GO

-- -----------------------------------------------------------
-- 1. Header
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ExtraMaterialRequest')
BEGIN
    CREATE TABLE dbo.ExtraMaterialRequest
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EMR PRIMARY KEY,
        EmrCode           NVARCHAR(50)     NOT NULL,
        WorkOrderId       INT              NOT NULL,
        BranchId          INT              NULL,
        RequestDate       DATETIME         NOT NULL CONSTRAINT DF_EMR_RequestDate DEFAULT(SYSUTCDATETIME()),
        Reason            NVARCHAR(50)     NOT NULL,   -- WASTE / REWORK / DAMAGE / DESIGN_CHANGE / SAMPLE / OTHER
        Remarks           NVARCHAR(500)    NULL,
        Status            NVARCHAR(20)     NOT NULL CONSTRAINT DF_EMR_Status DEFAULT('DRAFT'),
                                                         -- DRAFT / SUBMITTED / APPROVED / REJECTED

        RequestedBy       INT              NULL,
        SubmittedDate     DATETIME         NULL,

        ApprovedBy        INT              NULL,
        ApprovedDate      DATETIME         NULL,
        RejectionReason   NVARCHAR(500)    NULL,

        IssueReferenceNo  NVARCHAR(50)     NULL,        -- Gen_Store.ReferenceNo written at approval

        Active            BIT              NULL CONSTRAINT DF_EMR_Active  DEFAULT(1),
        Deleted           BIT              NULL CONSTRAINT DF_EMR_Deleted DEFAULT(0),
        CreatedBy         INT              NULL,
        CreatedDate       DATETIME         NULL,
        UpdatedBy         INT              NULL,
        UpdatedDate       DATETIME         NULL,
        RecordTimeStamp   DATETIME         NULL,

        CONSTRAINT FK_EMR_WorkOrder FOREIGN KEY (WorkOrderId)
            REFERENCES dbo.WorkOrders(Id),
        CONSTRAINT FK_EMR_Branch    FOREIGN KEY (BranchId)
            REFERENCES dbo.Branches(Id)
    );

    CREATE UNIQUE INDEX UX_EMR_EmrCode ON dbo.ExtraMaterialRequest (EmrCode);
    CREATE INDEX IX_EMR_WorkOrderId    ON dbo.ExtraMaterialRequest (WorkOrderId);
    CREATE INDEX IX_EMR_Status         ON dbo.ExtraMaterialRequest (Status);
END
GO

-- -----------------------------------------------------------
-- 2. Line items
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ExtraMaterialRequestItems')
BEGIN
    CREATE TABLE dbo.ExtraMaterialRequestItems
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EMRI PRIMARY KEY,
        EmrId        INT              NOT NULL,
        ItemType     NVARCHAR(20)     NOT NULL,   -- 'RAW' or 'THREAD'
        RawItemId    INT              NULL,        -- Gen_RawItems.Id (for ItemType='RAW')
        ThreadId     INT              NULL,        -- Gen_Threads.Id   (for ItemType='THREAD')
        UnitOfMeasure NVARCHAR(20)    NULL,
        Qty          DECIMAL(18,4)    NOT NULL,
        Rate         DECIMAL(18,4)    NULL,
        Amount       DECIMAL(18,4)    NULL,
        Remarks      NVARCHAR(200)    NULL,

        Active       BIT              NULL CONSTRAINT DF_EMRI_Active  DEFAULT(1),
        Deleted      BIT              NULL CONSTRAINT DF_EMRI_Deleted DEFAULT(0),
        CreatedBy    INT              NULL,
        CreatedDate  DATETIME         NULL,
        UpdatedBy    INT              NULL,
        UpdatedDate  DATETIME         NULL,

        CONSTRAINT FK_EMRI_Header FOREIGN KEY (EmrId)
            REFERENCES dbo.ExtraMaterialRequest(Id),
        CONSTRAINT FK_EMRI_RawItem FOREIGN KEY (RawItemId)
            REFERENCES dbo.Gen_RawItems(Id),
        CONSTRAINT FK_EMRI_Thread  FOREIGN KEY (ThreadId)
            REFERENCES dbo.Gen_Threads(Id)
    );

    CREATE INDEX IX_EMRI_EmrId    ON dbo.ExtraMaterialRequestItems (EmrId);
    CREATE INDEX IX_EMRI_RawItem  ON dbo.ExtraMaterialRequestItems (RawItemId);
    CREATE INDEX IX_EMRI_Thread   ON dbo.ExtraMaterialRequestItems (ThreadId);
END
GO

-- -----------------------------------------------------------
-- 3. Smoke test
-- -----------------------------------------------------------
SELECT TOP 5
    e.Id, e.EmrCode, e.WorkOrderId, e.Status, e.RequestDate,
    (SELECT COUNT(*) FROM dbo.ExtraMaterialRequestItems i WHERE i.EmrId = e.Id) AS ItemCount
FROM   dbo.ExtraMaterialRequest e
ORDER  BY e.Id DESC;
GO
