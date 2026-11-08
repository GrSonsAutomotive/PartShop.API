-- Apply separately after review. No database selection, seed data, or inventory writes.
-- Conservative comparison: punctuation and non-ASCII characters are removed on BOTH
-- sides. Collisions can block deletion, never permit an otherwise matching rule.
CREATE OR ALTER FUNCTION dbo.ReferenceData_Normalize(@value nvarchar(4000))
RETURNS nvarchar(4000)
AS
BEGIN
    DECLARE @result nvarchar(4000) = N'', @i int = 1, @c nchar(1);
    WHILE @i <= LEN(@value)
    BEGIN
        SET @c = SUBSTRING(@value, @i, 1);
        IF @c COLLATE Latin1_General_100_BIN2 LIKE N'[A-Za-z0-9]'
            SET @result += LOWER(@c COLLATE Latin1_General_100_BIN2);
        SET @i += 1;
    END;
    RETURN @result;
END;
GO
CREATE OR ALTER FUNCTION dbo.ReferenceData_TagMatches(@tag nvarchar(4000), @kind nvarchar(20), @name nvarchar(128))
RETURNS bit
AS
BEGIN
    DECLARE @actual nvarchar(4000) = dbo.ReferenceData_Normalize(@tag),
        @expected nvarchar(4000) = dbo.ReferenceData_Normalize(@kind + N'_' + @name);
    -- Managed source names are at most 128 characters, so their prefixed tags
    -- cannot reach Shopify's 255-character limit. Short prefixes are not rules
    -- for the full name. Compare the entire conservatively normalized tag.
    RETURN CASE WHEN LEN(@actual) > 0 AND @expected = @actual THEN 1 ELSE 0 END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ReferenceData_DeleteSafe
    @Kind varchar(20), @Id int = NULL, @Company nvarchar(128) = NULL,
    @Delete bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Kind IS NULL OR @Kind NOT IN ('Category', 'Make', 'Model', 'Manufacturer')
        THROW 50200, 'Invalid reference kind.', 1;
    DECLARE @targets TABLE (Id int PRIMARY KEY, Company nvarchar(128), ModelName nvarchar(128));
    DECLARE @name nvarchar(128), @exists bit = 0, @primary int = 0,
        @categories int = 0, @fitments int = 0, @makes int = 0,
        @rules int = 0, @rows int = 0, @deleted bit = 0;
    BEGIN TRY
        -- SERIALIZABLE forces locking reads even with READ_COMMITTED_SNAPSHOT.
        -- SQL Server holds predicate/range locks until the enclosing transaction
        -- ends. Scans may still lock broadly or escalate; never use ROWLOCK or
        -- READPAST to trade away protection. These SET options are procedure scoped.
        IF @Delete = 1
        BEGIN
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            SET DEADLOCK_PRIORITY LOW;
        END;
        BEGIN TRANSACTION;
        -- Update locks on targets avoid two deleters both acquiring shared locks
        -- before upgrading. Manufacturer predicates also protect company phantoms.
        IF @Kind = 'Category'
        BEGIN
            SELECT @name = [name], @exists = 1 FROM dbo.Catagory WITH (UPDLOCK) WHERE id = @Id;
            SELECT @primary = COUNT(*) FROM dbo.Parts WHERE catagoryId = @Id;
            SELECT @categories = COUNT(*) FROM dbo.PartCategories WHERE CatagoryId = @Id;
        END;
        IF @Kind = 'Model'
        BEGIN
            SELECT @name = RTRIM([name]), @exists = 1 FROM dbo.Model WITH (UPDLOCK) WHERE id = @Id;
            SELECT @makes = COUNT(*) FROM dbo.Make WHERE modelId = @Id;
        END;
        IF @Kind IN ('Make', 'Manufacturer')
        BEGIN
            INSERT @targets
            SELECT m.id, m.company, RTRIM(md.[name])
            FROM dbo.Make m WITH (UPDLOCK) JOIN dbo.Model md ON md.id = m.modelId
            WHERE (@Kind = 'Make' AND m.id = @Id)
               OR (@Kind = 'Manufacturer' AND LTRIM(RTRIM(m.company)) = LTRIM(RTRIM(@Company)));
            SELECT @rows = COUNT(*) FROM @targets;
            SET @exists = CASE WHEN @rows > 0 THEN 1 ELSE 0 END;
            SELECT @primary = COUNT(*) FROM dbo.Parts p JOIN @targets t ON t.Id = p.makeId;
            SELECT @fitments = COUNT(*) FROM dbo.PartFitments p JOIN @targets t ON t.Id = p.MakeId;
        END
        ELSE SET @rows = CONVERT(int, @exists);

        -- Includes historical rules. No status/date filter and no dependency on
        -- a representative MIN(Make.id). Model rules also protect associations.
        SELECT @rules = COUNT(*) FROM dbo.AdminDiscountCodeRules r
        WHERE
          (@Kind IN ('Category','Model') AND r.RuleType = @Kind AND
            (r.SourceId = @Id OR dbo.ReferenceData_Normalize(r.RuleValue) = dbo.ReferenceData_Normalize(@name)
             OR dbo.ReferenceData_TagMatches(r.ShopifyTag, @Kind, @name) = 1))
          OR (@Kind IN ('Category','Model') AND dbo.ReferenceData_TagMatches(r.ShopifyTag, @Kind, @name) = 1)
          OR (@Kind IN ('Make','Manufacturer') AND EXISTS
            (SELECT 1 FROM @targets t JOIN dbo.Make m ON m.id = t.Id
             WHERE
               (r.RuleType = 'Make' AND
                 (r.SourceId = t.Id OR EXISTS (SELECT 1 FROM dbo.Make sibling WHERE sibling.id = r.SourceId
                    AND LTRIM(RTRIM(sibling.company)) = LTRIM(RTRIM(t.Company)))
                  OR dbo.ReferenceData_Normalize(r.RuleValue) = dbo.ReferenceData_Normalize(t.Company)
                  OR dbo.ReferenceData_TagMatches(r.ShopifyTag, N'Make', t.Company) = 1))
               OR (r.RuleType = 'Model' AND
                 (r.SourceId = m.modelId OR dbo.ReferenceData_Normalize(r.RuleValue) = dbo.ReferenceData_Normalize(t.ModelName)
                  OR dbo.ReferenceData_TagMatches(r.ShopifyTag, N'Model', t.ModelName) = 1))
               OR dbo.ReferenceData_TagMatches(r.ShopifyTag, N'Make', t.Company) = 1
               OR dbo.ReferenceData_TagMatches(r.ShopifyTag, N'Model', t.ModelName) = 1));
        IF @Delete = 1 AND @exists = 1 AND @primary + @categories + @fitments + @makes + @rules = 0
        BEGIN
            IF @Kind = 'Category' DELETE dbo.Catagory WHERE id = @Id;
            IF @Kind = 'Model' DELETE dbo.Model WHERE id = @Id;
            IF @Kind IN ('Make','Manufacturer') DELETE m FROM dbo.Make m JOIN @targets t ON t.Id = m.id;
            SET @deleted = 1;
        END;
        COMMIT;
        SELECT @exists AS [Exists], @primary AS PrimaryParts, @categories AS Categories,
            @fitments AS Fitments, @makes AS Makes, @rules AS DiscountRules,
            @rows AS TargetRows, @deleted AS Deleted;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
-- Harden legacy procedure entry points as well. Preserve their parameter contract.
CREATE OR ALTER PROCEDURE dbo.Catagory_Delete @Id int AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.ReferenceData_DeleteSafe @Kind='Category', @Id=@Id, @Delete=1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.Make_Delete @Id int AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.ReferenceData_DeleteSafe @Kind='Make', @Id=@Id, @Delete=1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.Model_Delete @Id int AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.ReferenceData_DeleteSafe @Kind='Model', @Id=@Id, @Delete=1;
END;
GO
