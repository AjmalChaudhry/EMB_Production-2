-- Sprint-2 — Component Materials master-data audit timeline.
-- Append-only log; one row per state-changing event on a
-- Gen_ComponentMaterials row: CREATED, EDITED, ACTIVATED,
-- DEACTIVATED, DELETED.
-- Reads via GET /emb-products/component-materials/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_ComponentMaterialsAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_ComponentMaterialsAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CmId        INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenComponentMaterialsAuditLog_CmId
        ON dbo.Gen_ComponentMaterialsAuditLog (CmId, Id DESC);
END
