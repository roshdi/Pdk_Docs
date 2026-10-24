# Nested Command Orchestration

Use nested command orchestration when a parent command contains an embedded DTO and
the related entity must be resolved by another command before the parent is saved.
For example, `CreateOrderCommand.Customer` can be sent to
`UpsertCustomerCommandHandler`; the returned `CustomerId` is then assigned to the
order.

Do not call an application endpoint over HTTP and do not instantiate a command
handler directly. Dispatch the child command in process so its normal PDK validation,
authorization, business rules, result handling, and persistence pipeline are reused.

## Define orchestration input

`IncludeNavigation` includes a reference DTO in the generated command contract. Its
`mapToEntity` argument controls whether the generated Mapperly method also maps that
DTO onto the entity navigation:

```csharp
HasCreateCommand()
    .IncludeNavigation(
        order => order.Customer,
        customer => customer.IncludeAll(),
        mapToEntity: false);

HasUpdateCommand()
    .IncludeNavigation(
        order => order.Customer,
        customer => customer.IncludeAll(),
        mapToEntity: false);
```

Use `mapToEntity: false` when the navigation DTO is input for a nested command. PDK
still generates `CustomerDto Customer`, but it adds a Mapperly
`MapperIgnoreTarget` attribute for `OrderEntity.Customer`. This prevents create from
inserting a second customer graph and prevents update from overwriting a tracked
customer navigation.

The default is `mapToEntity: true`, so existing `IncludeNavigation` declarations keep
their original aggregate-mapping behavior. Disabled mapping currently supports
reference navigations only.

## Handler lifecycle

PDK save handlers execute these relevant stages in order:

1. `OnStartingAsync`
2. command validation, when enabled
3. `BeforeGetEntityAsync`
4. `GetEntityAsync`
5. `FillEntityFields`
6. entity validation and state processing
7. save and commit

`BeforeGetEntityAsync` is the safe place to dispatch the child command. The parent
entity has not been loaded, created, mapped, or tracked, so the child's
`SaveChangesAsync` cannot accidentally flush an incomplete parent.

For create handlers, `BeforeTrackEntityAsync` additionally runs after generated
mapping and before `DbContext.AddAsync`. It can enforce graph invariants before EF
begins tracking a new aggregate. Prefer `mapToEntity: false` for orchestration input;
the hook is a final customization and safety point, not a replacement for correct
mapper configuration.

## Create order example

```csharp
using FastEndpoints;

public partial class CreateOrderCommandHandler
{
    private long _customerId;

    protected override Task OnStartingAsync(
        CreateOrderCommand command,
        CancellationToken ct)
        => StartTransactionAsync(ct);

    protected override async Task BeforeGetEntityAsync(
        CreateOrderCommand command,
        CancellationToken ct)
    {
        var child = new UpsertCustomerCommand
        {
            CustomerCode = command.Customer.CustomerCode,
            CustomerName = command.Customer.CustomerName,
            Address = command.Customer.Address
        };

        var childResult = await child.ExecuteAsync(ct);
        if (!childResult.IsSucceed)
        {
            ForwardFailure(childResult);
            return;
        }

        if (childResult.Value is null)
        {
            Error("Customer upsert returned no result.");
            return;
        }

        _customerId = childResult.Value.CustomerId;
    }

    protected override Task FillEntityFields(
        CreateOrderCommand command,
        OrderEntity entity,
        CancellationToken ct)
    {
        entity.CustomerId = _customerId;
        return Task.CompletedTask;
    }
}
```

## Update order example

Update uses the same orchestration sequence. The generated order mapper ignores the
`Customer` navigation because the definition uses `mapToEntity: false`.

```csharp
using FastEndpoints;

public partial class UpdateOrderCommandHandler
{
    private long _customerId;

    protected override Task OnStartingAsync(
        UpdateOrderCommand command,
        CancellationToken ct)
        => StartTransactionAsync(ct);

    protected override async Task BeforeGetEntityAsync(
        UpdateOrderCommand command,
        CancellationToken ct)
    {
        var child = new UpsertCustomerCommand
        {
            CustomerCode = command.Customer.CustomerCode,
            CustomerName = command.Customer.CustomerName,
            Address = command.Customer.Address
        };

        var childResult = await child.ExecuteAsync(ct);
        if (!childResult.IsSucceed)
        {
            ForwardFailure(childResult);
            return;
        }

        if (childResult.Value is null)
        {
            Error("Customer upsert returned no result.");
            return;
        }

        _customerId = childResult.Value.CustomerId;
    }

    protected override Task FillEntityFields(
        UpdateOrderCommand command,
        OrderEntity entity,
        CancellationToken ct)
    {
        entity.CustomerId = _customerId;
        return Task.CompletedTask;
    }
}
```

The child result must expose the customer key. PDK normally generates the entity's
primary-key property on `UpsertCustomerCommandResult`; use that generated property in
`BeforeGetEntityAsync`.

## Transaction behavior

The parent handler starts and owns the transaction. Because parent and child resolve
the same scoped DbContext, `UpsertCustomerCommandHandler` joins the current EF
transaction. A joining handler may call `SaveChangesAsync` to obtain a generated
customer key, but it does not commit, roll back, or dispose the transaction.

The parent commits only after the order is saved. If child validation fails, order
processing short-circuits through `ForwardFailure`. If the child or parent fails after
a save, the parent owner rolls back the complete transaction.

This guarantee requires both commands to use the same scoped DbContext and database.
Cross-service or cross-database orchestration requires an outbox and process manager;
it cannot use the local transaction behavior described here.

## Planned declarative orchestration

The current API deliberately keeps dispatch and input/result mapping in developer
handler code. A future generator DSL may describe the same workflow declaratively:

```csharp
.IncludeNavigation(
    order => order.Customer,
    customer => customer
        .IncludeAll()
        .ResolveWithCommand("UpsertCustomer", resolution => resolution
            .MapInputByConvention()
            .AssignResult("CustomerId", order => order.CustomerId)),
    mapToEntity: false)
```

`ResolveWithCommand` is a design proposal, not an implemented API. Until it is
implemented, use `BeforeGetEntityAsync`, `ForwardFailure`, and `FillEntityFields` as
shown above.
