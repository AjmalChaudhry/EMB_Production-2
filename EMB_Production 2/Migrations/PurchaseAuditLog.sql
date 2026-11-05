-- Sprint-2 — GRN/Purchase audit timeline.
-- Append-only log; one row per state transition (CREATED, UPDATED,
-- POSTED, REVERSED, DELETED, CLONED, RETURNED). The detail-drawer
-- timeline reads via /emb-products/purchase/audit-log/{id} ORDERed
-- by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database (Web.config connection
-- strings EMB_ProductionEntities + EMB_ProductionEntities1 both
-- point at this DB). Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'PurchaseAuditLog'
)
BEGIN
    CREATE TABLE dbo.PurchaseAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PurchaseId  INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_PurchaseAuditLog_PurchaseId
        ON dbo.PurchaseAuditLog (PurchaseId, Id DESC);
END
