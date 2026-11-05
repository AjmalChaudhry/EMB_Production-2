-- Sprint-2 — Companies master-data audit timeline.
-- Reads via GET /emb-products/companies/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Adm_CompanyProfileAuditLog'
)
BEGIN
    CREATE TABLE dbo.Adm_CompanyProfileAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId   INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_AdmCompanyProfileAuditLog_CompanyId
        ON dbo.Adm_CompanyProfileAuditLog (CompanyId, Id DESC);
END
