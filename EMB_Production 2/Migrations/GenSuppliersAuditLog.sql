-- Sprint-2 — Suppliers master-data audit timeline.
-- Reads via GET /emb-products/suppliers/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_SuppliersAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_SuppliersAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SupplierId  INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenSuppliersAuditLog_SupplierId
        ON dbo.Gen_SuppliersAuditLog (SupplierId, Id DESC);
END
