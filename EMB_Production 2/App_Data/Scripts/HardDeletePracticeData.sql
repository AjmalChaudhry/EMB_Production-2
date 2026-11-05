/* HARD DELETE practice data — corrected FK order.
   Run once in SSMS against EMB_Production on localhost\SQLEXPRESS03. */

USE [EMB_Production];

-- 1. Line / child tables (no other table points at them)
DELETE FROM dbo.DispatchItems;
DELETE FROM dbo.QC_InspectionItems;
DELETE FROM dbo.DailyProductionStops;
DELETE FROM dbo.Gen_JobOrderItems;
DELETE FROM dbo.PurchaseItems;

-- 2. Ledgers
DELETE FROM dbo.Gen_Stocks;
DELETE FROM dbo.Gen_Store;

-- 3. Headers — delete from the leaves of the FK chain inward.
--    QC_Inspection.ProductionId -> DailyProduction      (QC before DP)
--    DailyProduction.WorkOrderItemId -> WorkOrderItems  (DP before WOI)
--    WorkOrderItems.WorkOrderId -> WorkOrders           (WOI before WO)
--    WorkOrders.JobOrderId -> Gen_JobOrders             (WO before JO)
DELETE FROM dbo.Dispatch;
DELETE FROM dbo.QC_Inspection;
DELETE FROM dbo.DailyProduction;
DELETE FROM dbo.WorkOrderItems;
DELETE FROM dbo.WorkOrders;
DELETE FROM dbo.Gen_JobOrders;
DELETE FROM dbo.Purchase;

-- 4. Reset identity so next row in each table starts at Id = 1
DBCC CHECKIDENT ('dbo.DispatchItems',         RESEED, 0);
DBCC CHECKIDENT ('dbo.Dispatch',              RESEED, 0);
DBCC CHECKIDENT ('dbo.QC_InspectionItems',    RESEED, 0);
DBCC CHECKIDENT ('dbo.QC_Inspection',         RESEED, 0);
DBCC CHECKIDENT ('dbo.DailyProductionStops',  RESEED, 0);
DBCC CHECKIDENT ('dbo.DailyProduction',       RESEED, 0);
DBCC CHECKIDENT ('dbo.WorkOrderItems',        RESEED, 0);
DBCC CHECKIDENT ('dbo.WorkOrders',            RESEED, 0);
DBCC CHECKIDENT ('dbo.Gen_JobOrderItems',     RESEED, 0);
DBCC CHECKIDENT ('dbo.Gen_JobOrders',         RESEED, 0);
DBCC CHECKIDENT ('dbo.PurchaseItems',         RESEED, 0);
DBCC CHECKIDENT ('dbo.Purchase',              RESEED, 0);
DBCC CHECKIDENT ('dbo.Gen_Stocks',            RESEED, 0);
DBCC CHECKIDENT ('dbo.Gen_Store',             RESEED, 0);
