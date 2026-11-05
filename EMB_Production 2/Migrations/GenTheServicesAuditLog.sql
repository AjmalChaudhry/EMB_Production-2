-- Sprint-2 — Services master-data audit timeline.
-- Append-only log; one row per state-changing event on a Gen_TheServices
-- row: CREATED, EDITED, ACTIVATED, DEACTIVATED, DELETED.
-- Reads via GET /emb-products/services/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_TheServicesAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_TheServicesAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ServiceId   INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenTheServicesAuditLog_ServiceId
        ON dbo.Gen_TheServicesAuditLog (ServiceId, Id DESC);
END
