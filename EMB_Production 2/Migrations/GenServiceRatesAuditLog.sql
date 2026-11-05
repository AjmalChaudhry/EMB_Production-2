-- Sprint-2 — Service Rates master-data audit timeline.
-- Reads via GET /emb-products/service-rates/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_ServiceRatesAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_ServiceRatesAuditLog
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ServiceRateId   INT NOT NULL,
        Action          NVARCHAR(40) NOT NULL,
        ChangedBy       INT NULL,
        ChangedDate     DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue        NVARCHAR(200) NULL,
        NewValue        NVARCHAR(200) NULL,
        Detail          NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenServiceRatesAuditLog_ServiceRateId
        ON dbo.Gen_ServiceRatesAuditLog (ServiceRateId, Id DESC);
END
