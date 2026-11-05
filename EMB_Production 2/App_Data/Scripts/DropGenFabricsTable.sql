/* =====================================================================
   DROP Gen_Fabrics — final step
   ---------------------------------------------------------------------
   Removes the table + its FK to Gen_RawItems. Run this once in SSMS,
   THEN refresh the EDMX in Visual Studio so the auto-generated
   Models/Gen_Fabrics.cs and the DbSet entries in Model1.Context.cs /
   Model1.Context1.cs disappear (along with the navigation property on
   Gen_RawItems that points at this table).

   After this script + EDMX refresh:
     • Models/Gen_Fabrics.cs ........ auto-removed
     • Models/Model1.Context*.cs .... auto-updated (no DbSet<Gen_Fabrics>)
     • Models/Gen_RawItems.cs ....... auto-updated (no Gen_Fabrics nav)

   NOTHING in the application reads Gen_Fabrics anymore — every
   fabric/raw flow now goes through Gen_RawItems + Gen_Store.
   ===================================================================== */

USE [EMB_Production];
GO

-- 1. Drop the FK constraint to Gen_RawItems (added by BridgeFabricToRawItem.sql).
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Gen_Fabrics_Gen_RawItems')
    ALTER TABLE [dbo].[Gen_Fabrics] DROP CONSTRAINT [FK_Gen_Fabrics_Gen_RawItems];
GO

-- 2. Drop the table.
IF OBJECT_ID('dbo.Gen_Fabrics', 'U') IS NOT NULL
    DROP TABLE [dbo].[Gen_Fabrics];
GO

PRINT '=== Gen_Fabrics dropped successfully ===';
PRINT '';
PRINT 'Next step: open Models/Model1.edmx in Visual Studio,';
PRINT 'right-click the canvas, choose Update Model from Database,';
PRINT 'expand the Refresh tab, locate Tables -> Gen_Fabrics (marked as missing),';
PRINT 'remove it, click Finish. Then rebuild.';
