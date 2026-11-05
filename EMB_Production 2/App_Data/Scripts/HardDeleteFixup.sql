/* Finish the cleanup — run this once.
   DailyProduction, QC_Inspection, Dispatch, lines, and ledgers are
   already empty. Only WorkOrderItems, WorkOrders, and Gen_JobOrders
   still have data. Delete them in the correct chain: child first. */

USE [EMB_Production];

DELETE FROM dbo.WorkOrderItems;   -- DailyProduction is already empty, FK is free now
DELETE FROM dbo.WorkOrders;       -- WorkOrderItems empty above
DELETE FROM dbo.Gen_JobOrders;    -- WorkOrders empty above

DBCC CHECKIDENT ('dbo.WorkOrderItems', RESEED, 0);
DBCC CHECKIDENT ('dbo.WorkOrders',     RESEED, 0);
DBCC CHECKIDENT ('dbo.Gen_JobOrders',  RESEED, 0);
