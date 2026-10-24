# Excel mapping profiles

`ExcelMappingProfile<TEntity>` maps normalized Excel row values to an entity. A mapping can convert a cell directly or resolve a foreign-key value by querying another entity.

```csharp
public sealed class ProductExcelProfile : ExcelMappingProfile<ProductEntity>
{
    public override void Configure()
    {
        Map(x => x.ProductCode, "ProductCode")
            .ConvertUsing(value => value?.Trim() ?? string.Empty);

        Map(x => x.Price, "Price")
            .ConvertUsing(value => decimal.Parse(
                value!,
                CultureInfo.InvariantCulture));

        Map(x => x.BrandId, "Brand")
            .ResolveFrom<BrandEntity, string>(
                brand => brand.BrandName,
                brand => brand.BrandId);
    }
}
```

Execute the profile with an `IDbContextBase`:

```csharp
var mapper = new ExcelEntityMapper<ProductEntity>(profile, dbContext);
var row = new ExcelRow(2, new Dictionary<string, string?>
{
    ["ProductCode"] = "1001",
    ["Price"] = "12.50",
    ["Brand"] = "Acme"
});

var result = await mapper.MapAsync(row, cancellationToken);
```

Column names are case-insensitive. Default conversions use invariant culture and support nullable values, primitive types, enums, and GUIDs. `ConvertUsing` overrides conversion for a property.

`ResolveFrom` converts the cell to the lookup key type, queries the lookup entity with `AsNoTracking`, and assigns the selected value to the target property. Lookup results are cached by mapping and cell value for the lifetime of the mapper. Missing and ambiguous matches are returned as FluentValidation failures on the target property.

## Generate an import endpoint

Declare the command in the entity feature profile:

```csharp
HasImportFromExcelCommand("Import")
    .MatchBy(product => new { product.OwnerId, product.ProductCode })
    .OnDuplicate(ImportDuplicateBehavior.Update)
    .MaximumBatchSize(5000)
    .ItemValidator<ProductImportValidator>()
    .OnValidationFailure(BulkFailureBehavior.RollbackBatch);
```

`pdk generate api` creates the multipart endpoint, command, result, validator,
transactional handler, and a paired Excel mapping profile. Put custom mappings
in the preserved `ImportProductExcelMappingProfile.cs` partial class:

```csharp
public partial class ImportProductExcelMappingProfile
{
    partial void ConfigureMappings()
    {
        Map(product => product.ProductCode, "Product Code");

        Map(product => product.Weight, "Weight")
            .ConvertUsing(value => decimal.Parse(
                value!, CultureInfo.InvariantCulture));

        Map(product => product.BrandId, "Brand")
            .ResolveFrom<BrandEntity, string>(
                brand => brand.BrandName,
                brand => brand.BrandId);
    }
}
```

The request includes `File` and `ValidateOnly`. `ValidateOnly=true` maps and
validates without database writes. `RollbackBatch` makes the import atomic;
`SkipInvalidItems` inserts valid rows and reports invalid Excel rows with
property-level FluentValidation errors. Only `.xlsx` files are accepted, and
the configured maximum batch size is also the maximum worksheet data-row count.

`MatchBy` is required and should map to a unique database key. `OnDuplicate`
selects one explicit behavior:

- `ImportDuplicateBehavior.Update`: update only the properties configured in the Excel mapping profile;
- `ImportDuplicateBehavior.Skip`: leave the database row unchanged and report the row as skipped;
- `ImportDuplicateBehavior.Fail`: add a duplicate validation error (the default).

Repeated `MatchBy` values inside the same workbook are validation failures,
independent of the database duplicate policy.
