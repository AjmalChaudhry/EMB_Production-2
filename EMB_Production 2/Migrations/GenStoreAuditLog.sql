-- Sprint-2 — Gen_Store (inventory ledger) audit timeline.
-- Append-only log; one row per state-changing event on a Gen_Store
-- row: CREATED (a fresh ledger write), REVERSED (a MATERIAL_ISSUE
-- reversed via the per-row or bulk-reverse action), EDITED (qty/rate
-- correction), SOFT_DELETED.
-- The detail-drawer timeline reads via
--   GET /emb-products/gen-store/audit-log/{storeId}
-- ORDERed by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database. Idempotent — re-running
-- is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Gen_StoreAuditLog'
)
BEGIN
    CREATE TABLE dbo.Gen_StoreAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StoreId     INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_GenStoreAuditLog_StoreId
        ON dbo.Gen_StoreAuditLog (StoreId, Id DESC);
END
