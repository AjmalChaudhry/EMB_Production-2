-- Sprint-2 — QC Inspection audit timeline.
-- Append-only log; one row per state-changing event on a
-- QC_Inspection row: CREATED, EDITED, PROCESSED, POSTED, REVERSED,
-- CANCELLED, REINSPECT.
-- The detail-drawer timeline reads via
--   GET /emb-products/qc-inspection/audit-log/{qcId}
-- ORDERed by Id DESC so most-recent action sits at the top.
--
-- Run once on the EMB_Production database. Idempotent — re-running
-- is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'QC_InspectionAuditLog'
)
BEGIN
    CREATE TABLE dbo.QC_InspectionAuditLog
    (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        QcInspectionId  INT NOT NULL,
        Action          NVARCHAR(40) NOT NULL,
        ChangedBy       INT NULL,
        ChangedDate     DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue        NVARCHAR(200) NULL,
        NewValue        NVARCHAR(200) NULL,
        Detail          NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_QcInspectionAuditLog_QcId
        ON dbo.QC_InspectionAuditLog (QcInspectionId, Id DESC);
END
