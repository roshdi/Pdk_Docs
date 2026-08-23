using Pdk.Core.Abstractions;
using Pdk.Core.Data;

namespace Pdk.Api.Commands;

/// <summary>
/// Creates a new entity when <see cref="SaveCommandHandler{TEntity,TCommand,TResult}.CreateItemFilter"/>
/// finds no match, or updates the matching entity otherwise.
/// </summary>
public abstract class UpsertCommandHandler<TEntity, TCommand, TResult>
    : SaveCommandHandler<TEntity, TCommand, TResult>
    where TEntity : class, new()
    where TCommand : CommandBase<TResult>
{
    protected UpsertCommandHandler(IDbContextBase dbContext, IRequestContext requestContext)
        : base(dbContext, requestContext)
    {
    }

    /// <summary>True when this execution inserted a new entity.</summary>
    protected bool WasCreated { get; private set; }

    protected override Task OnStartingAsync(TCommand command, CancellationToken ct) =>
        StartTransactionAsync(ct);

    protected override async Task<TEntity> GetEntityAsync(
        TCommand command,
        CancellationToken cancellationToken)
    {
        var entity = await LoadEntityAsync(command, cancellationToken);
        if (entity is not null)
        {
            IsUpdate = true;
            WasCreated = false;
            return await ApplyCommandToEntityAsync(command, entity, cancellationToken);
        }

        IsUpdate = false;
        WasCreated = true;
        entity = await ApplyCommandAsync(command, new TEntity(), cancellationToken);
        CapabilityInvoker<TEntity, ICreated>.Instance.Invoke(entity, RequestContext);
        await dbContext.AddAsync(entity, cancellationToken);
        return entity;
    }

    protected override void SetResult(TResult result)
    {
        if (WasCreated)
            Created(result);
        else
            Ok(result);
    }
}
