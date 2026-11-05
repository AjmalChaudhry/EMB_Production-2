-- Sprint-2 — Service Types master-data audit timeline.
-- Reads via GET /emb-products/service-types/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_ServiceTypeAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_ServiceTypeAuditLog
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ServiceTypeId   INT NOT NULL,
        Action          NVARCHAR(40) NOT NULL,
        ChangedBy       INT NULL,
        ChangedDate     DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue        NVARCHAR(200) NULL,
        NewValue        NVARCHAR(200) NULL,
        Detail          NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenServiceTypeAuditLog_ServiceTypeId
        ON dbo.Gen_ServiceTypeAuditLog (ServiceTypeId, Id DESC);
END
