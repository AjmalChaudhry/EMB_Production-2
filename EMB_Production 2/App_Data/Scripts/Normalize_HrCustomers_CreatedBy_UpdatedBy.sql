-- ============================================================
-- Normalize Hr_Customers.CreatedBy / UpdatedBy to a single user
-- ------------------------------------------------------------
-- Sets CreatedBy = UpdatedBy = @TargetUserId for every row in
-- Hr_Customers. Atomic batch (SET XACT_ABORT ON + TRY/CATCH +
-- COMMIT inside the batch) — F5 once, either all rows update
-- and persist or nothing changes.
--
-- Why both columns in one shot:
--   Many existing rows have CreatedBy = NULL (never set during
--   data import) and UpdatedBy = 6 / 2075 / NULL inconsistently.
--   Single normalize pass brings every row to a consistent
--   audit-trail state.
--
-- What this does NOT touch:
--   CreatedDate / UpdatedDate are preserved. The "by whom" gets
--   normalized; the "when it happened" timeline stays intact.
--
-- !!! READ BEFORE RUNNING !!!
--
-- 1. BACK UP THE DATABASE FIRST (recommended).
-- 2. Highlight the whole TRY/CATCH block + verification SELECT
--    or Ctrl+A then F5.
-- 3. Read the Messages tab:
--      "COMMITTED. Rows normalized: <N>" → success
--      "ROLLED BACK. Reason: ..."        → nothing changed
-- 4. @TargetUserId at the top picks the audit-trail user.
--    Default 6. Change if you need a different audit identity.
-- ============================================================

USE [EMB_Production];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @TargetUserId INT = 6;
DECLARE @UserExists   BIT = 0;
DECLARE @RowsUpdated  INT = 0;
DECLARE @ErrMsg       NVARCHAR(2000);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Verify the target user exists and is not soft-deleted
    IF EXISTS (
        SELECT 1 FROM dbo.Adm_Users
        WHERE  Id = @TargetUserId
          AND  (Deleted = 0 OR Deleted IS NULL)
    )
        SET @UserExists = 1;

    IF @UserExists = 0
    BEGIN
        SET @ErrMsg = N'Adm_Users.Id = '
                    + CAST(@TargetUserId AS NVARCHAR(10))
                    + N' does not exist (or is soft-deleted). Rolling back.';
        THROW 50020, @ErrMsg, 1;
    END;

    -- 2) Normalize every Hr_Customers row to use the target user
    --    for both CreatedBy and UpdatedBy
    UPDATE  dbo.Hr_Customers
    SET     CreatedBy = @TargetUserId,
            UpdatedBy = @TargetUserId;
    SET @RowsUpdated = @@ROWCOUNT;

    -- 3) Sanity check — must have updated at least one row.
    --    Hr_Customers should never be empty at this point; if it
    --    is, something is wrong (wrong DB? table renamed?).
    IF @RowsUpdated = 0
    BEGIN
        SET @ErrMsg = N'Hr_Customers contained zero rows — nothing was updated. '
                    + N'Verify the database context. Rolling back.';
        THROW 50021, @ErrMsg, 1;
    END;

    COMMIT TRANSACTION;

    PRINT N'==========================================';
    PRINT N'COMMITTED. Hr_Customers normalized.';
    PRINT N'  Target user (CreatedBy + UpdatedBy): '
        + CAST(@TargetUserId AS NVARCHAR(10));
    PRINT N'  Rows updated: '
        + CAST(@RowsUpdated AS NVARCHAR(10));
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

-- ============================================================
-- Post-commit verification (out of transaction)
-- ============================================================
-- Every row should now show CreatedBy = UpdatedBy = 6
SELECT  Id,
        Customer_Id,
        Customer_Name,
        CreatedBy,
        CreatedDate,
        UpdatedBy,
        UpdatedDate
FROM    dbo.Hr_Customers
ORDER BY Id;

-- Quick aggregate — should show exactly one group with both
-- columns = 6 and a count matching the table size.
-- Note: `RowCount` (no brackets) is a SQL Server reserved word
-- (the `SET ROWCOUNT` option) — use `RowsInGroup` instead.
SELECT  CreatedBy,
        UpdatedBy,
        COUNT(*) AS RowsInGroup
FROM    dbo.Hr_Customers
GROUP BY CreatedBy, UpdatedBy
ORDER BY CreatedBy, UpdatedBy;
