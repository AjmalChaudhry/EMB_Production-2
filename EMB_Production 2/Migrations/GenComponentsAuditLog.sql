-- Sprint-2 — Components master-data audit timeline.
-- Append-only log; one row per state-changing event on a
-- Gen_Components row: CREATED, EDITED, ACTIVATED, DEACTIVATED, DELETED.
-- Reads via GET /emb-products/components/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_ComponentsAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_ComponentsAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ComponentId INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenComponentsAuditLog_ComponentId
        ON dbo.Gen_ComponentsAuditLog (ComponentId, Id DESC);
END
