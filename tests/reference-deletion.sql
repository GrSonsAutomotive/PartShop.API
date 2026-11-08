-- Run only through the guarded --sql test runner, after fixture schema + migration.
IF DB_NAME() NOT LIKE N'ReferenceDeletionTests[_]%' OR NOT EXISTS
 (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'ReferenceDeletionTestOwner')
 THROW 50300, 'Runner-owned disposable test database required.', 1;
BEGIN TRANSACTION;
INSERT dbo.AdminDiscountCodes VALUES(1,N'Active',NULL),(2,N'Expired','20000101'),(3,N'Deactivated',NULL);
INSERT dbo.Catagory VALUES (1,N'Brakes',999),(2,N'Cooling',999),(3,N'Unused',999),(4,N'Rule only',999);
INSERT dbo.Model VALUES (1,N'Shared'),(2,N'Unused'),(3,N'Rule only');
INSERT dbo.Make VALUES (10,N'Zeta',1,10,20),(11,N'Zeta',2,10,10),(20,N'Alpha',1,20,10),(30,N'Unused',2,30,10),(31,N'Unused',1,30,20);
INSERT dbo.Parts VALUES (1,1,10,7);
INSERT dbo.PartSales VALUES(1,1,2);
INSERT dbo.PartCategories VALUES (1,1,2);
INSERT dbo.PartFitments VALUES (1,1,11);
SELECT * INTO #InventoryBefore FROM dbo.Parts;
SELECT * INTO #CategoriesBefore FROM dbo.PartCategories;
SELECT * INTO #FitmentsBefore FROM dbo.PartFitments;
SELECT * INTO #SalesBefore FROM dbo.PartSales;
SELECT * INTO #CategoryOrdering FROM dbo.Catagory;
SELECT * INTO #MakeOrdering FROM dbo.Make;
CREATE TABLE #Result (Present bit, PrimaryParts int, Categories int, Fitments int, Makes int, DiscountRules int, TargetRows int, Deleted bit);

INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',1,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND PrimaryParts=1) THROW 50301,'Primary category protection failed',1;
PRINT N'PASS SQL: Primary category protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',2,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND Categories=1) THROW 50301,'Additional category protection failed',1;
PRINT N'PASS SQL: Additional category protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Make',11,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND Fitments=1) THROW 50301,'Fitment protection failed',1;
PRINT N'PASS SQL: Fitment protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Manufacturer',NULL,N'Zeta',1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND TargetRows=2) OR (SELECT COUNT(*) FROM dbo.Make WHERE company=N'Zeta')<>2
 THROW 50301,'Manufacturer atomic blocking failed',1;
PRINT N'PASS SQL: Manufacturer atomic blocking';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',1,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND Makes=3) THROW 50301,'Shared model protection failed',1;
PRINT N'PASS SQL: Shared model protection';

-- Distinct categories whose punctuation normalizes to the same managed tag.
INSERT dbo.Catagory VALUES(101,N'Air-Con',10),(102,N'Air Con',20),(103,N'Air Conditioning',30);
INSERT dbo.Model VALUES(101,N'GT-R'),(102,N'GT R'),(103,N'Shared Name'),(104,N'Shared Name');
INSERT dbo.Make VALUES(101,N'One',103,80,20),(102,N'Two',104,90,30);
INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag,AdminDiscountCodeId)
 VALUES(101,N'Category',101,N'Air-Con',N'Category_Air_Con',2),
       (102,N'Model',101,N'GT-R',N'Model_GT_R',3),
       (103,N'Model',103,N'Shared Name',N'Model_Shared_Name',2);
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',102,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Category name collision protection failed',1;
PRINT N'PASS SQL: Category name collision protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',102,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Model punctuation collision protection failed',1;
PRINT N'PASS SQL: Model punctuation collision protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Make',102,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Shared model names across manufacturers protection failed',1;
PRINT N'PASS SQL: Shared model names across manufacturers protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',101,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Deactivated historical rule protection failed',1;
PRINT N'PASS SQL: Deactivated historical rule protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',101,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Expired historical rule protection failed',1;
PRINT N'PASS SQL: Expired historical rule protection';

-- Short tags must not match a longer source name, even across rule kinds.
INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag)
 VALUES(104,N'CustomTag',NULL,N'anything',N'Cat'),(105,N'CustomTag',NULL,N'anything',N'Category_Air'),
       (106,N'CustomTag',NULL,N'anything',N'Mod'),(107,N'CustomTag',NULL,N'anything',N'Model_Un');
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',103,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1 AND DiscountRules=0) THROW 50301,'Short category prefixes blocked unrelated deletion',1;
PRINT N'PASS SQL: Short category prefixes allow unrelated deletion';
INSERT dbo.Model VALUES(105,N'Unrelated');
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',105,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1 AND DiscountRules=0) THROW 50301,'Short model prefixes blocked unrelated deletion',1;
PRINT N'PASS SQL: Short model prefixes allow unrelated deletion';
IF dbo.ReferenceData_TagMatches(N'Category_Air_Con',N'Category',N'Air-Con')<>1
 OR dbo.ReferenceData_TagMatches(N'Category_Air',N'Category',N'Air-Con')<>0
 OR dbo.ReferenceData_Normalize(N'A.i-r / Con')<>N'aircon'
 THROW 50301,'Exact tag and punctuation semantics failed',1;
PRINT N'PASS SQL: Exact tag and punctuation semantics';
DELETE dbo.AdminDiscountCodeRules WHERE Id BETWEEN 101 AND 107;
DELETE dbo.Make WHERE id IN(101,102);
DELETE dbo.Model WHERE id BETWEEN 101 AND 105;
DELETE dbo.Catagory WHERE id BETWEEN 101 AND 103;

INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES (1,N'Category',4,N'different',N'different'),(2,N'Model',NULL,N'Rule-only',N'Model_Rule_only'),
 (3,N'Make',NULL,N'Unused',N'Make_Unused');
INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES (4,N'CustomTag',NULL,N'arbitrary',N'Category_Unused');
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',3,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Custom tag protection failed',1;
PRINT N'PASS SQL: Custom tag protection';
DELETE dbo.AdminDiscountCodeRules WHERE Id=4;
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',4,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Source ID protection failed',1;
PRINT N'PASS SQL: Source ID protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',3,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Normalized rule protection failed',1;
PRINT N'PASS SQL: Normalized rule protection';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Manufacturer',NULL,N'Unused',1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND DiscountRules=1) THROW 50301,'Manufacturer rule protection failed',1;
PRINT N'PASS SQL: Manufacturer rule protection';
DELETE dbo.AdminDiscountCodeRules WHERE Id=3; -- Fixture cleanup, never application behavior.
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Manufacturer',NULL,N'Unused',0;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=0 AND TargetRows=2) THROW 50301,'Preview wrote data',1;
PRINT N'PASS SQL: Preview does not delete';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Manufacturer',NULL,N'Unused',1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1 AND TargetRows=2) OR EXISTS(SELECT 1 FROM dbo.Make WHERE company=N'Unused')
 THROW 50301,'Manufacturer deletion failed',1;
PRINT N'PASS SQL: Manufacturer deletion';
IF NOT EXISTS(SELECT 1 FROM dbo.Model WHERE id=1) THROW 50301,'Shared model was removed',1;
PRINT N'PASS SQL: Shared model preservation';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Make',20,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1) THROW 50301,'Association deletion failed',1;
PRINT N'PASS SQL: Association deletion';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',3,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1) THROW 50301,'Category deletion failed',1;
PRINT N'PASS SQL: Category deletion';
INSERT dbo.Model VALUES (9,N'Unreferenced');
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',9,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1) THROW 50301,'Global model deletion failed',1;
PRINT N'PASS SQL: Global model deletion';
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Model',999,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Present=0 AND Deleted=0) THROW 50301,'Missing record handling failed',1;
PRINT N'PASS SQL: Missing record handling';

IF EXISTS(SELECT * FROM dbo.Parts EXCEPT SELECT * FROM #InventoryBefore)
 OR EXISTS(SELECT * FROM #InventoryBefore EXCEPT SELECT * FROM dbo.Parts)
 OR EXISTS(SELECT * FROM dbo.PartCategories EXCEPT SELECT * FROM #CategoriesBefore)
 OR EXISTS(SELECT * FROM #CategoriesBefore EXCEPT SELECT * FROM dbo.PartCategories)
 OR EXISTS(SELECT * FROM dbo.PartFitments EXCEPT SELECT * FROM #FitmentsBefore)
 OR EXISTS(SELECT * FROM #FitmentsBefore EXCEPT SELECT * FROM dbo.PartFitments)
 THROW 50301,'Inventory or associations changed',1;
PRINT N'PASS SQL: Inventory or associations preserved';
IF EXISTS(SELECT * FROM dbo.PartSales EXCEPT SELECT * FROM #SalesBefore)
 OR EXISTS(SELECT * FROM #SalesBefore EXCEPT SELECT * FROM dbo.PartSales)
 THROW 50301,'Sales history changed',1;
PRINT N'PASS SQL: Sales history preserved';
IF EXISTS(SELECT 1 FROM dbo.Make m JOIN #MakeOrdering b ON b.id=m.id
 WHERE m.CompanyDisplayOrder<>b.CompanyDisplayOrder OR m.ModelDisplayOrder<>b.ModelDisplayOrder OR m.company<>b.company OR m.modelId<>b.modelId)
 THROW 50301,'Custom ordering changed',1;
PRINT N'PASS SQL: Custom ordering preserved';
IF EXISTS(SELECT 1 FROM dbo.Catagory c JOIN #CategoryOrdering b ON b.id=c.id WHERE c.name<>b.name OR c.DisplayOrder<>b.DisplayOrder)
 THROW 50301,'Surviving category ordering changed',1;
PRINT N'PASS SQL: Surviving category ordering preserved';
IF (SELECT COUNT(*) FROM dbo.AdminDiscountCodeRules)<>2 THROW 50301,'Historical rules changed',1;
PRINT N'PASS SQL: Historical rules preserved';
-- Category reads are alphabetical even when DisplayOrder disagrees. Make/model
-- reads retain custom priorities even after deleting the old representative ID.
INSERT dbo.Catagory VALUES(404,N'Zulu',1),(405,N'Alpha',999),(406,N'Middle',20);
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Category',406,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1)
 OR (SELECT STRING_AGG(CONVERT(varchar(max),id),',') WITHIN GROUP(ORDER BY [name]) FROM dbo.Catagory)<>'405,1,2,4,404'
 THROW 50301,'Alphabetical category read order failed',1;
PRINT N'PASS SQL: Alphabetical category read order ignores DisplayOrder';
INSERT dbo.Make VALUES(400,N'Ordering',1,20,30),(401,N'Ordering',2,20,10),(402,N'Ordering',1,20,20),(403,N'OrderB',2,5,50);
TRUNCATE TABLE #Result;
INSERT #Result EXEC dbo.ReferenceData_DeleteSafe 'Make',400,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #Result WHERE Deleted=1)
 OR (SELECT STRING_AGG(CONVERT(varchar(max),id),',') WITHIN GROUP(ORDER BY ModelDisplayOrder,id) FROM dbo.Make WHERE company=N'Ordering')<>'401,402'
 THROW 50301,'Custom model ordering after representative deletion failed',1;
PRINT N'PASS SQL: Custom model order and surviving IDs after representative deletion';
IF (SELECT STRING_AGG(CONVERT(nvarchar(max),company),N',') WITHIN GROUP(ORDER BY Priority,company)
 FROM(SELECT company,MIN(CompanyDisplayOrder) AS Priority FROM dbo.Make WHERE id>=400 GROUP BY company) x)<>N'OrderB,Ordering'
 THROW 50301,'Custom manufacturer ordering failed',1;
PRINT N'PASS SQL: Custom manufacturer ordering retained';
ROLLBACK;
PRINT 'PASS: SQL assertions complete; migration repetition checked separately by runner';
