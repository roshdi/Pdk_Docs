# Bulk Commands

PDK bulk commands perform one secured database query, validate the complete
request, optionally validate every matched entity, apply changes, and call
`SaveChangesAsync` once. Bulk operations use POST endpoints because their
requests contain arrays and may use composite business keys.

## Bulk insert

`HasBulkInsertCommand` generates a POST command with an `Items` array,
per-item metadata validation, one transaction, and row-level results.

```csharp
HasBulkInsertCommand("InsertBatch")
    .MaximumBatchSize(5000)
    .ItemValidator<ProductImportValidator>()
    .OnValidationFailure(BulkFailureBehavior.RollbackBatch);
```

## Bulk upsert

`HasBulkUpsertCommand` creates missing rows and updates existing rows. `MatchBy`
is required and should correspond to a database unique key. Duplicate keys in a
request are rejected by the generated validator.

```csharp
HasBulkUpsertCommand("Synchronize")
    .MatchBy(product => new { product.OwnerId, product.ProductCode })
    .MaximumBatchSize(5000)
    .OnValidationFailure(BulkFailureBehavior.SkipInvalidItems);
```

Both commands exclude generated keys, concurrency tokens, audit fields,
soft-delete fields, and security-owned fields from item DTOs by default.

## Bulk update modes

`SharedUpdate` applies the same values to every matched row:

```csharp
HasBulkUpdateCommand("Activate")
    .SharedUpdate(entity => new { entity.IsActive, entity.PartnerId })
    .MaximumBatchSize(5000);
```

With a single primary key, the update properties are flattened beside the key
array:

```json
{
  "productBarcodeIds": [178559, 178560],
  "isActive": true,
  "partnerId": null
}
```

`PerItemUpdate` allows different values for different rows:

```csharp
HasBulkUpdateCommand("Reclassify")
    .MatchBy(entity => new { entity.WarehouseId, entity.Code })
    .PerItemUpdate(entity => new { entity.CategoryId, entity.IsActive })
    .MaximumBatchSize(1000);
```

Its request contains an `items` array. Every item contains its match fields and
update fields:

```json
{
  "items": [
    {
      "warehouseId": 10,
      "code": "A-100",
      "categoryId": 4,
      "isActive": true
    }
  ]
}
```

For `PerItemUpdate`, duplicate match keys fail request validation. Batches larger
than `MaximumBatchSize` fail request validation in every mode before the handler
modifies data. Shared key arrays are de-duplicated when PDK calculates the
requested row count.

## Matching rows

Without `MatchBy`, PDK uses the entity primary key. Use `MatchBy` for a unique
business key or a composite key:

```csharp
HasBulkUpdateCommand("Assign")
    .MatchBy(entity => new { entity.WarehouseId, entity.Code })
    .SharedUpdate(entity => entity.OwnerId);
```

The database should enforce the same business key with a unique constraint or
unique index. `MatchBy` cannot contain an unmapped, computed, or concurrency
property.

Without concurrency, missing or security-inaccessible rows are reported through
`notFoundCount`; other matched and valid rows may still be processed.

## Optimistic concurrency

Add `WithConcurrency()` when every selected row must carry its expected version:

```csharp
HasBulkUpdateCommand("Activate")
    .WithConcurrency()
    .SharedUpdate(entity => entity.IsActive);

HasBulkDeleteCommand("Remove")
    .WithConcurrency();
```

Generation fails at the feature-profile source line unless the entity implements
`IOptimisticConcurrency` and has exactly one mapped concurrency token.

Concurrency changes a primary-key array into a `matches` array so each key has a
version:

```json
{
  "matches": [
    { "productBarcodeId": 178559, "version": "AAAAAAAAB9E=" },
    { "productBarcodeId": 178560, "version": "AAAAAAAAB9F=" }
  ],
  "isActive": true
}
```

If any requested row is missing, inaccessible, or stale, PDK rejects the entire
operation with HTTP 409 before applying mutations. Concurrency is independent of
item-validation failure policy.

## Per-row FluentValidation

An item validator receives both the loaded entity and the complete command:

```csharp
public sealed class ActivateProductBarcodeItemValidator
    : AbstractValidator<
        BulkItemValidationContext<ProductBarcodeEntity,
            ActivateProductBarcodeCommand>>
{
    public ActivateProductBarcodeItemValidator()
    {
        RuleFor(row => row.Entity.IsActive)
            .Equal(false)
            .WithErrorCode("AlreadyActive")
            .WithMessage("Barcode is already active.");
    }
}
```

When the feature profile can reference the validator type, configure it directly.
The validator must also be registered in DI (module validators are discovered
automatically when they are in a scanned module assembly):

```csharp
HasBulkUpdateCommand("Activate")
    .SharedUpdate(entity => entity.IsActive)
    .ItemValidator<ActivateProductBarcodeItemValidator>()
    .OnValidationFailure(BulkFailureBehavior.SkipInvalidItems);
```

In a layered solution where feature profiles are in Core and validators are in
Api, keep the policy in the profile and resolve the validator in the non-generated
partial handler:

```csharp
// Core feature profile
HasBulkUpdateCommand("Activate")
    .SharedUpdate(entity => entity.IsActive)
    .OnValidationFailure(BulkFailureBehavior.SkipInvalidItems);

// Api partial handler
public partial class ActivateProductBarcodeCommandHandler
{
    protected override IValidator<
        BulkItemValidationContext<ProductBarcodeEntity,
            ActivateProductBarcodeCommand>>? GetItemValidator()
        => new ActivateProductBarcodeItemValidator();
}
```

Item validation runs for all matched rows before any mutation. Each failed result
preserves FluentValidation's property name, message, error code, attempted value,
severity, and custom state.

## Failure policies

`OnValidationFailure` supports two explicit policies:

| Policy | Invalid rows | Valid rows | Database write |
| --- | --- | --- | --- |
| `RollbackBatch` | `Failed` | `NotProcessed` | Nothing is saved |
| `SkipInvalidItems` | `Skipped` | `Succeeded` | Only valid rows are saved |

`RollbackBatch` is the default. Select `SkipInvalidItems` only when partial success
is valid for the business operation.

Request-level validation is different from item validation. An invalid batch
shape, empty key collection, duplicate match, or excessive batch size returns
HTTP 400 and does not call the bulk handler. Item validation produces a normal
bulk result so the client can display the outcome for each matched row.

## Bulk delete

Bulk delete uses the same matching, concurrency, item validation, batch-size, and
failure-policy DSL:

```csharp
HasBulkDeleteCommand("RemoveByWarehouseCode")
    .MatchBy(entity => new { entity.WarehouseId, entity.Code })
    .ItemValidator<RemoveProductItemValidator>()
    .OnValidationFailure(BulkFailureBehavior.RollbackBatch)
    .MaximumBatchSize(5000);
```

The unnamed operation generates `DeleteBulk`; a named operation generates
`{Name}Bulk`. PDK invokes `ISoftDelete` when the entity supports it; otherwise it
removes the entity. Add developer-owned behavior in the non-generated partial
handler:

```csharp
protected override Task CustomizeDeleteAsync(
    RemoveProductCommand command,
    ProductEntity entity,
    CancellationToken cancellationToken)
{
    // Domain-side effects or additional state changes.
    return Task.CompletedTask;
}
```

Bulk update provides the equivalent `CustomizeUpdateAsync` hook.

## Generated result

Bulk update and delete results contain aggregate counts plus row-level outcomes:

```json
{
  "requestedCount": 2,
  "matchedCount": 2,
  "updatedCount": 1,
  "notFoundCount": 0,
  "rows": [
    {
      "key": { "productBarcodeId": 178559 },
      "status": 2,
      "errors": [
        {
          "propertyName": "Entity.IsActive",
          "errorMessage": "Barcode is already active.",
          "errorCode": "AlreadyActive",
          "attemptedValue": true,
          "severity": "Error",
          "customState": null
        }
      ]
    },
    {
      "key": { "productBarcodeId": 178560 },
      "status": 1,
      "errors": []
    }
  ],
  "summary": {
    "total": 2,
    "success": 1,
    "failed": 0,
    "skipped": 1,
    "notFound": 0,
    "notProcessed": 0,
    "overallResult": "PartialSuccess"
  }
}
```

`BulkItemStatus` values are `Succeeded = 1`, `Skipped = 2`, `Failed = 3`, and
`NotProcessed = 4`. Delete results use `deletedCount` instead of `updatedCount`.
The updated/deleted count is the number of successfully processed entities, not
the raw value returned by `SaveChangesAsync`.

## Execution order

The generated handler executes these stages in order:

1. Validate the request contract.
2. Apply permission and row-level security filters.
3. Load all matching entities in one query.
4. Validate optimistic concurrency, when enabled.
5. Run the item validator for every matched entity.
6. Apply `RollbackBatch` or `SkipInvalidItems`.
7. Invoke update/delete hooks and entity capabilities.
8. Call `SaveChangesAsync` once when at least one row is processed.
9. Return row results and the aggregate summary.
