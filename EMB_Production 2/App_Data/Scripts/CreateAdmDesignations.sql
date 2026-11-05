-- ============================================================
-- Adm_Designations master
-- ------------------------------------------------------------
-- Creates the designations table referenced by Adm_Users.DesignationId
-- and seeds a sensible default list. Idempotent — re-running is safe.
--
-- After running:
--   1) (Optional) Refresh the EDMX in Visual Studio so the entity is
--      available via db.Adm_Designations. The new endpoint reads via
--      raw SQL so the build is NOT blocked on the refresh.
--   2) Rebuild MVC + hard-refresh the SPA.
-- ============================================================

USE [EMB_Production];
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Adm_Designations')
BEGIN
    CREATE TABLE dbo.Adm_Designations
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Adm_Designations PRIMARY KEY,
        Name         NVARCHAR(100)    NOT NULL,
        Code         NVARCHAR(30)     NULL,
        Description  NVARCHAR(255)    NULL,
        Active       BIT              NULL CONSTRAINT DF_Adm_Designations_Active  DEFAULT(1),
        Deleted      BIT              NULL CONSTRAINT DF_Adm_Designations_Deleted DEFAULT(0),
        CreatedBy    INT              NULL,
        CreatedDate  DATETIME         NULL,
        UpdatedBy    INT              NULL,
        UpdatedDate  DATETIME         NULL
    );

    -- Plain unique index on Name. Soft-deleted rows keep their slot;
    -- if you want to re-add a name later, hard-delete the soft row first.
    CREATE UNIQUE INDEX UX_Adm_Designations_Name
        ON dbo.Adm_Designations (Name);
END
GO

-- Seed a reasonable default list. Skip rows that already exist (by Name).
DECLARE @Seeds TABLE (Name NVARCHAR(100), Code NVARCHAR(30));

INSERT INTO @Seeds (Name, Code) VALUES
    (N'Chief Executive Officer',    N'CEO'),
    (N'General Manager',            N'GM'),
    (N'Production Manager',         N'PRD-MGR'),
    (N'Branch Manager',             N'BR-MGR'),
    (N'Floor Supervisor',           N'SUP'),
    (N'Quality Inspector',          N'QC-INSP'),
    (N'Machine Operator',           N'OP'),
    (N'Store Keeper',               N'STORE'),
    (N'Accountant',                 N'ACC'),
    (N'Cashier',                    N'CASH'),
    (N'Salesman',                   N'SLS'),
    (N'HR Officer',                 N'HR'),
    (N'IT Officer',                 N'IT'),
    (N'Receptionist',               N'RCP');

INSERT INTO dbo.Adm_Designations (Name, Code, Active, Deleted, CreatedDate)
SELECT s.Name, s.Code, 1, 0, SYSUTCDATETIME()
FROM   @Seeds s
WHERE  NOT EXISTS (
    SELECT 1 FROM dbo.Adm_Designations d
    WHERE  d.Name = s.Name AND (d.Deleted IS NULL OR d.Deleted = 0)
);
GO

SELECT TOP 50 Id, Name, Code, Active
FROM   dbo.Adm_Designations
ORDER BY Name;
GO
