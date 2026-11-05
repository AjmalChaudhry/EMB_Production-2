-- Sprint-2 — Bill of Materials (BOM) audit timeline.
-- Append-only log; one row per state-changing event on a Gen_BOM
-- row: CREATED, EDITED, ACTIVATED, DEACTIVATED, DELETED, CLONED.
-- The detail-drawer timeline reads via
--   GET /emb-products/gen-bom/audit-log/{bomId}
-- ORDERed by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database. Idempotent — re-running
-- is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_BOMAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_BOMAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BomId       INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenBomAuditLog_BomId
        ON dbo.Gen_BOMAuditLog (BomId, Id DESC);
END
