-- ============================================================
-- Hard-delete soft-removed rows from Adm_CompanyProfile
-- ------------------------------------------------------------
-- v3 - 20 Jun 2026
--
-- v2 succeeded inside its transaction but the SSMS session was
-- closed before the manual COMMIT was run, so SQL Server
-- auto-rolled back. v3 fixes this by making the entire
-- reassign + delete + verify + commit one atomic batch wrapped
-- in TRY/CATCH. Press F5 ONCE — either everything is committed
-- and persistent, or everything is rolled back and the DB is
-- exactly as it was. No follow-up COMMIT to forget.
--
-- !!! READ BEFORE RUNNING !!!
--
-- 1. BACK UP THE DATABASE FIRST.
-- 2. Highlight the whole TRY/CATCH block (or Ctrl+A on the
--    file after USE/GO) and press F5 once.
-- 3. Read the final Messages tab output:
--      "COMMITTED. Rows removed: 18" → success, done.
--      "ROLLED BACK. Reason: ..."    → nothing changed, see msg.
-- 4. @KeepCompanyId at the top picks which surviving company
--    orphan users get reassigned to. Default 64 ("A One").
--    Change to 53 ("ABC") if you'd rather.
-- ============================================================

USE [EMB_Production];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @KeepCompanyId     INT = 64;   -- "A One" — change to 53 for "ABC"
DECLARE @ExpectedRemaining INT = 2;    -- after delete, must equal this

DECLARE @UsersReassigned   INT = 0;
DECLARE @CompaniesRemoved  INT = 0;
DECLARE @RemainingCount    INT = 0;
DECLARE @OrphanUsers       INT = 0;
DECLARE @ErrMsg            NVARCHAR(2000);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Reassign Adm_Users orphans to the surviving company
    UPDATE  u
    SET     u.CompanyID   = @KeepCompanyId,
            u.UpdatedDate = GETDATE()
    FROM    dbo.Adm_Users u
    WHERE   u.CompanyID IN
            (SELECT CompanyID FROM dbo.Adm_CompanyProfile WHERE Deleted = 1);
    SET @UsersReassigned = @@ROWCOUNT;

    -- 2) Delete soft-removed company profiles
    DELETE
    FROM    dbo.Adm_CompanyProfile
    WHERE   Deleted = 1;
    SET @CompaniesRemoved = @@ROWCOUNT;

    -- 3) Verify final state INSIDE the transaction
    SELECT @RemainingCount = COUNT(*)
    FROM   dbo.Adm_CompanyProfile;

    SELECT @OrphanUsers = COUNT(*)
    FROM   dbo.Adm_Users
    WHERE  CompanyID NOT IN (SELECT CompanyID FROM dbo.Adm_CompanyProfile);

    -- 4) Guard rails — refuse to commit if anything looks off
    IF @RemainingCount <> @ExpectedRemaining
    BEGIN
        SET @ErrMsg = N'After delete, Adm_CompanyProfile has '
                    + CAST(@RemainingCount AS NVARCHAR(10))
                    + N' rows (expected '
                    + CAST(@ExpectedRemaining AS NVARCHAR(10))
                    + N'). Rolling back.';
        THROW 50001, @ErrMsg, 1;
    END;

    IF @OrphanUsers > 0
    BEGIN
        SET @ErrMsg = N'After delete, '
                    + CAST(@OrphanUsers AS NVARCHAR(10))
                    + N' user(s) still point at a missing CompanyID. Rolling back.';
        THROW 50002, @ErrMsg, 1;
    END;

    -- All checks pass — commit
    COMMIT TRANSACTION;

    PRINT N'==========================================';
    PRINT N'COMMITTED. Cleanup is now permanent.';
    PRINT N'  Adm_Users reassigned to CompanyID '
        + CAST(@KeepCompanyId AS NVARCHAR(10))
        + N': ' + CAST(@UsersReassigned AS NVARCHAR(10));
    PRINT N'  Adm_CompanyProfile rows removed: '
        + CAST(@CompaniesRemoved AS NVARCHAR(10));
    PRINT N'  Adm_CompanyProfile rows remaining: '
        + CAST(@RemainingCount AS NVARCHAR(10));
    PRINT N'==========================================';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    PRINT N'==========================================';
    PRINT N'ROLLED BACK. No changes were saved.';
    PRINT N'Reason: ' + ERROR_MESSAGE();
    PRINT N'==========================================';
END CATCH;
GO

-- Final out-of-transaction sanity selects (run after the batch
-- above completes — these read whatever is now committed).
SELECT  CompanyID, CompanyName, NickName, Deleted, Active
FROM    dbo.Adm_CompanyProfile
ORDER BY CompanyID;

SELECT  CompanyID, COUNT(*) AS UserCount
FROM    dbo.Adm_Users
GROUP BY CompanyID
ORDER BY CompanyID;
