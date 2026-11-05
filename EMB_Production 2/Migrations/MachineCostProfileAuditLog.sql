-- Sprint-2 — Machine Cost Profile audit timeline.
-- Reads via GET /emb-products/machine-cost-profile/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'MachineCostProfileAuditLog'
)
BEGIN
    CREATE TABLE dbo.MachineCostProfileAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        McpId       INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_MachineCostProfileAuditLog_McpId
        ON dbo.MachineCostProfileAuditLog (McpId, Id DESC);
END
