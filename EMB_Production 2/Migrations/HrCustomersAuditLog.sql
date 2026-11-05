-- Sprint-2 — Customers master-data audit timeline.
-- Reads via GET /emb-products/customers/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Hr_CustomersAuditLog'
)
BEGIN
    CREATE TABLE dbo.Hr_CustomersAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CustomerId  INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_HrCustomersAuditLog_CustomerId
        ON dbo.Hr_CustomersAuditLog (CustomerId, Id DESC);
END
