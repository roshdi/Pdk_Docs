# Preference capability guide

Preferences are authenticated session context that constrain data independently of
permissions. Use permissions to answer *may this user perform the operation?* and
preferences to answer *which currently selected context applies?* Examples include a
selected warehouse, branch, fiscal year, or language.

## Define a preference capability

The interface inherits `IPreferences`. A static method marked with
`[PreferenceFilter]` returns the fail-closed entity predicate. It may also implement
normal entity capabilities such as `ICreated` so the same context is stamped during
creation.

```csharp
public interface IWarehouseContextEntity : IPreferences, ICreated, IEntityCapability
{
    string? ReferenceNumber { get; set; }

    void ICreated.SetCreated(IRequestContext context) =>
        ReferenceNumber = context.Preferences.Warehouse.GetRequiredReferenceNumber();

    [PreferenceFilter]
    static Expression<Func<TEntity, bool>> Filter<TEntity>(IRequestContext context)
        where TEntity : IWarehouseContextEntity
    {
        var reference = context.Preferences.Warehouse.GetRequiredReferenceNumber();
        return entity => entity.ReferenceNumber == reference;
    }
}
```

Declare it independently from security:

```csharp
entities.Entity("Product")
    .HasSecurity<IBasicSecurity>()
    .HasPreferences<IWarehouseContextEntity>();
```

All preference interfaces implemented by an entity are AND-composed. PDK applies
them to list/item queries, update/delete loads, bulk update/delete, and Excel import
duplicate matching. Create capabilities stamp their values before persistence.

## Session storage

`IdentitySession.PreferencesJson` is the durable session snapshot. The host converts
that JSON to an application-specific scoped preferences service and exposes it from
`IRequestContext`. When the user changes a preference, update the session row and
reissue the authentication cookie or token so subsequent requests receive the new
snapshot.

Missing or invalid required preferences must throw or return an authorization-style
client error; they must never remove the predicate and widen access.
