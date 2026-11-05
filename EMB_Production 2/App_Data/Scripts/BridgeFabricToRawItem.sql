/* =====================================================================
   BRIDGE Gen_Fabrics → Gen_RawItems
   ---------------------------------------------------------------------
   Adds a RawItemId column to Gen_Fabrics that points at the matching
   Gen_RawItems row (Type='FABRIC'). After this:
     • Fabric purchases write Gen_Store via the linked RawItemId, the
       same way thread purchases already do via Gen_Threads.RawItemId.
     • Issue Material Against Work Order automatically sees fabric stock.
     • BOM / ComponentMaterials don't need any change — they already
       reference Gen_RawItems.

   APPLY ONCE in SSMS against EMB_Production. Then in Visual Studio:
     Models/Model1.edmx → Update Model from Database → tick Gen_Fabrics.
   ===================================================================== */

USE [EMB_Production];
GO

-- 1. Add the column (nullable so existing rows stay valid).
ALTER TABLE [dbo].[Gen_Fabrics]
ADD [RawItemId] INT NULL;
GO

-- 2. FK to Gen_RawItems (optional but enforces consistency).
ALTER TABLE [dbo].[Gen_Fabrics]
ADD CONSTRAINT [FK_Gen_Fabrics_Gen_RawItems]
    FOREIGN KEY ([RawItemId])
    REFERENCES [dbo].[Gen_RawItems]([Id])
    ON DELETE SET NULL;
GO

-- 3. Auto-link existing fabrics where a Gen_RawItems row exists with the
--    same name AND Type='FABRIC'. Anything that doesn't match by name
--    will stay NULL — you can link those manually in the Fabric form.
UPDATE f
SET    f.RawItemId = ri.Id
FROM   dbo.Gen_Fabrics f
JOIN   dbo.Gen_RawItems ri
       ON LTRIM(RTRIM(LOWER(ri.ItemName))) = LTRIM(RTRIM(LOWER(f.Name)))
      AND UPPER(ri.Type) = 'FABRIC'
WHERE  f.RawItemId IS NULL
   AND (f.Deleted IS NULL OR f.Deleted = 0);
GO

-- 4. Audit — show which fabrics did/didn't get linked
PRINT '=== Bridge results ===';
SELECT
    f.Id           AS FabricId,
    f.Code         AS FabricCode,
    f.Name         AS FabricName,
    f.RawItemId,
    ri.ItemName    AS LinkedRawItem,
    CASE WHEN f.RawItemId IS NULL THEN 'NEEDS MANUAL LINK' ELSE 'LINKED' END AS Status
FROM dbo.Gen_Fabrics f
LEFT JOIN dbo.Gen_RawItems ri ON ri.Id = f.RawItemId
WHERE f.Deleted IS NULL OR f.Deleted = 0
ORDER BY Status, f.Code;
