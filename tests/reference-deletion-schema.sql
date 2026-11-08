-- Minimal isolated schema with the relevant FK and index shapes from the snapshot.
-- The runner permits this ONLY in its newly created, marked disposable database.
CREATE TABLE dbo.Catagory(id int PRIMARY KEY, name nvarchar(128) NOT NULL, DisplayOrder int NOT NULL);
CREATE TABLE dbo.Model(id int PRIMARY KEY, name nchar(128) NOT NULL);
CREATE TABLE dbo.Make(id int PRIMARY KEY, company nvarchar(128) NOT NULL, modelId int NOT NULL REFERENCES dbo.Model(id), CompanyDisplayOrder int NOT NULL, ModelDisplayOrder int NOT NULL);
CREATE TABLE dbo.Parts(id int PRIMARY KEY, catagoryId int NOT NULL REFERENCES dbo.Catagory(id), makeId int NOT NULL REFERENCES dbo.Make(id), Quantity int NOT NULL);
CREATE TABLE dbo.PartCategories(Id int PRIMARY KEY, PartId int NOT NULL REFERENCES dbo.Parts(id), CatagoryId int NOT NULL REFERENCES dbo.Catagory(id));
CREATE INDEX IX_PartCategories_CatagoryId ON dbo.PartCategories(CatagoryId);
CREATE UNIQUE INDEX IX_PartCategories_PartId_CatagoryId ON dbo.PartCategories(PartId,CatagoryId);
CREATE TABLE dbo.PartFitments(Id int PRIMARY KEY, PartId int NOT NULL REFERENCES dbo.Parts(id), MakeId int NOT NULL REFERENCES dbo.Make(id));
CREATE INDEX IX_PartFitments_MakeId_YearStart_YearEnd ON dbo.PartFitments(MakeId);
CREATE INDEX IX_PartFitments_PartId ON dbo.PartFitments(PartId);
CREATE TABLE dbo.PartSales(Id int PRIMARY KEY, PartId int NOT NULL REFERENCES dbo.Parts(id) ON DELETE CASCADE, Quantity int NOT NULL);
CREATE TABLE dbo.AdminDiscountCodes(Id int PRIMARY KEY, Status nvarchar(30) NOT NULL, EndsAtUtc datetime2 NULL);
CREATE TABLE dbo.AdminDiscountCodeRules(Id int PRIMARY KEY, RuleType nvarchar(50) NOT NULL, SourceId int NULL, RuleValue nvarchar(200) NOT NULL, ShopifyTag nvarchar(255) NOT NULL,
    AdminDiscountCodeId int NOT NULL DEFAULT 1 REFERENCES dbo.AdminDiscountCodes(Id));
CREATE INDEX IX_AdminDiscountCodeRules_Source ON dbo.AdminDiscountCodeRules(RuleType,SourceId) INCLUDE(AdminDiscountCodeId,RuleValue,ShopifyTag);
CREATE UNIQUE INDEX UX_AdminDiscountCodeRules_Discount_Tag ON dbo.AdminDiscountCodeRules(AdminDiscountCodeId,ShopifyTag);
