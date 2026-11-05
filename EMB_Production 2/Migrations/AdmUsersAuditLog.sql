-- Sprint-2 — Users administration audit timeline.
-- High-value: user-management changes are tracked for compliance.
-- Reads via GET /emb-products/users/audit-log/{id}.
-- Idempotent — re-running is a no-op.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'Adm_UsersAuditLog'
)
BEGIN
    CREATE TABLE dbo.Adm_UsersAuditLog
    (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId      INT NOT NULL,
        Action      NVARCHAR(40) NOT NULL,
        ChangedBy   INT NULL,
        ChangedDate DATETIME NOT NULL DEFAULT (GETDATE()),
        OldValue    NVARCHAR(200) NULL,
        NewValue    NVARCHAR(200) NULL,
        Detail      NVARCHAR(1000) NULL
    );

    CREATE INDEX IX_AdmUsersAuditLog_UserId
        ON dbo.Adm_UsersAuditLog (UserId, Id DESC);
END
