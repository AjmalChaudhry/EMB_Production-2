-- Sprint-2 — Dispatch audit timeline.
-- Append-only log; one row per state-changing event on a Dispatch:
-- CREATED, EDITED, POSTED, REVERSED, DELETED.
-- The detail-drawer timeline reads via
--   GET /emb-products/dispatch/audit-log/{dispatchId}
-- ORDERed by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database. Idempotent — re-running
-- is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'DispatchAuditLog'
)
BEGIN
    CREATE TABLE dbo.DispatchAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        DispatchId  INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_DispatchAuditLog_DispatchId
        ON dbo.DispatchAuditLog (DispatchId, Id DESC);
END
