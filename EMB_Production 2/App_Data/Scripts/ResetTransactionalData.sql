/* =====================================================================
   RESET — wipe practice data from transactional + ledger tables
   ---------------------------------------------------------------------
   Clears in FK-safe order and reseeds identity columns so the next
   record in each table gets Id = 1.

   WHAT GETS CLEARED
     Headers ............ Purchase, Gen_JobOrders, WorkOrders,
                          DailyProduction, QC_Inspection, Dispatch
     Lines / children ... PurchaseItems, Gen_JobOrderItems,
                          WorkOrderItems, DailyProductionStops,
                          QC_InspectionItems, DispatchItems
     Ledgers ............ Gen_Store, Gen_Stocks

   WHAT STAYS UNTOUCHED
     Masters ............ Gen_Products, Gen_Design, Gen_Brands,
                          Gen_Catagories, Gen_Fabrics, Gen_Threads,
                          Gen_RawItems, Gen_Suppliers, Hr_Customers,
                          Gen_Machines, MachineCostProfile,
                          Gen_TheServices, Gen_ServiceType,
                          Gen_ServiceRates, Gen_BOM, Gen_Components,
                          Gen_ComponentMaterials, DesignCosting,
                          Branches, Adm_Users, Adm_Roles, Adm_Companies
                          (and every other lookup / configuration table)

   HOW TO USE
     1. Connect to:  localhost\SQLEXPRESS03  →  EMB_Production
     2. Open this file in SSMS and run it.
     3. Review the row counts printed at the end.
     4. If everything looks right, run:  COMMIT TRANSACTION;
        Otherwise:                       ROLLBACK TRANSACTION;
   ===================================================================== */

USE [EMB_Production];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

PRINT '=== Phase 1 of 3 : clearing child / line tables ===';

DELETE FROM dbo.DispatchItems;
DELETE FROM dbo.QC_InspectionItems;
DELETE FROM dbo.DailyProductionStops;
DELETE FROM dbo.WorkOrderItems;
DELETE FROM dbo.Gen_JobOrderItems;
DELETE FROM dbo.PurchaseItems;

PRINT '=== Phase 2 of 3 : clearing ledger tables ===';

DELETE FROM dbo.Gen_Stocks;
DELETE FROM dbo.Gen_Store;

PRINT '=== Phase 3 of 3 : clearing header tables ===';

DELETE FROM dbo.Dispatch;
DELETE FROM dbo.QC_Inspection;
DELETE FROM dbo.DailyProduction;
DELETE FROM dbo.WorkOrders;
DELETE FROM dbo.Gen_JobOrders;
DELETE FROM dbo.Purchase;

PRINT '=== Reseeding identity columns ===';

DBCC CHECKIDENT ('dbo.DispatchItems',         RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Dispatch',              RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.QC_InspectionItems',    RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.QC_Inspection',         RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.DailyProductionStops',  RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.DailyProduction',       RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.WorkOrderItems',        RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.WorkOrders',            RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Gen_JobOrderItems',     RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Gen_JobOrders',         RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.PurchaseItems',         RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Purchase',              RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Gen_Stocks',            RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Gen_Store',             RESEED, 0) WITH NO_INFOMSGS;

PRINT '=== Row counts after reset (all should be 0) ===';

SELECT 'DispatchItems'        AS TableName, COUNT(*) AS Rows FROM dbo.DispatchItems
UNION ALL SELECT 'Dispatch',              COUNT(*) FROM dbo.Dispatch
UNION ALL SELECT 'QC_InspectionItems',    COUNT(*) FROM dbo.QC_InspectionItems
UNION ALL SELECT 'QC_Inspection',         COUNT(*) FROM dbo.QC_Inspection
UNION ALL SELECT 'DailyProductionStops',  COUNT(*) FROM dbo.DailyProductionStops
UNION ALL SELECT 'DailyProduction',       COUNT(*) FROM dbo.DailyProduction
UNION ALL SELECT 'WorkOrderItems',        COUNT(*) FROM dbo.WorkOrderItems
UNION ALL SELECT 'WorkOrders',            COUNT(*) FROM dbo.WorkOrders
UNION ALL SELECT 'Gen_JobOrderItems',     COUNT(*) FROM dbo.Gen_JobOrderItems
UNION ALL SELECT 'Gen_JobOrders',         COUNT(*) FROM dbo.Gen_JobOrders
UNION ALL SELECT 'PurchaseItems',         COUNT(*) FROM dbo.PurchaseItems
UNION ALL SELECT 'Purchase',              COUNT(*) FROM dbo.Purchase
UNION ALL SELECT 'Gen_Stocks',            COUNT(*) FROM dbo.Gen_Stocks
UNION ALL SELECT 'Gen_Store',             COUNT(*) FROM dbo.Gen_Store;

PRINT '';
PRINT '------------------------------------------------------------';
PRINT '  All rows above should be 0. The transaction is still OPEN.';
PRINT '  Run  COMMIT TRANSACTION;  to make it permanent';
PRINT '  or   ROLLBACK TRANSACTION;  to undo everything.';
PRINT '------------------------------------------------------------';
