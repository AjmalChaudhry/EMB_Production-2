-- Sprint-2 — Daily Production audit timeline.
-- Append-only log; one row per state-changing event on a
-- DailyProduction row: CREATED, EDITED, POSTED, SENT_TO_QC,
-- REVERSED, DELETED.
-- The detail-drawer timeline reads via
--   GET /emb-products/daily-production/audit-log/{dpId}
-- ORDERed by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database. Idempotent — re-running
-- is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'DailyProductionAuditLog'
)
BEGIN
    CREATE TABLE dbo.DailyProductionAuditLog
    (
        Id                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        DailyProductionId INT NOT NULL,
        Action            NVARCHAR(40) NOT NULL,
        ChangedBy         INT NULL,
        ChangedDate       DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue          NVARCHAR(200) NULL,
        NewValue          NVARCHAR(200) NULL,
        Detail            NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_DailyProductionAuditLog_DpId
        ON dbo.DailyProductionAuditLog (DailyProductionId, Id DESC);
END
