-- Sprint-2 — Machine Parameters master-data audit timeline.
-- Reads via GET /emb-products/machine-parameters/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_MachineParametersAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_MachineParametersAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MpId        INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenMachineParametersAuditLog_MpId
        ON dbo.Gen_MachineParametersAuditLog (MpId, Id DESC);
END
