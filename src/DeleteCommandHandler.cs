using Pdk.Core.Abstractions;
using Pdk.Core.Data;
using Pdk.Core.Utilities;

namespace Pdk.Api.Commands;

/// <summary>
/// Handles delete commands: loads the entity, enforces the security filter, and either performs a
/// soft-delete (when the entity implements <see cref="ISoftDelete"/>) or a hard delete by removing
/// the entity from the DbContext.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TCommand">The delete command type.</typeparam>
/// <typeparam name="TResult">The response value type.</typeparam>
public abstract class DeleteCommandHandler<TEntity, TCommand, TResult> : SaveCommandHandler<TEntity, TCommand, TResult>
    where TEntity : class
    where TCommand : CommandBase<TResult>
{
    /// <summary>Initializes the handler with its DbContext and request context.</summary>
    protected DeleteCommandHandler(IDbContextBase dbContext, IRequestContext requestContext)
        : base(dbContext, requestContext)
    {
    }

    /// <summary>
    /// Loads the target entity, applies the security check and a concurrency check, then either
    /// stamps the soft-delete audit fields or removes the entity. Produces a <c>NotFound</c>,
    /// <c>Forbidden</c> or <c>ConcurrencyError</c> result on failure; the pipeline short-circuits
    /// accordingly.
    /// </summary>
    protected override async Task<TEntity> GetEntityAsync(TCommand command, CancellationToken cancellationToken)
    {
        var entity = await LoadEntityAsync(command, cancellationToken);
        if (entity is null)
        {
            NotFound($"Entity \"{typeof(TEntity).Name}\" ({command.ToJson()}) was not found.");
            return entity!;
        }

        var filter = await GetSecurityFilter(command);
        if (filter is not null && !filter.Compile().Invoke(entity))
        {
            Forbidden("Action is not allowed!");
            return entity;
        }

        // Snapshot the optimistic-concurrency token before applying changes so we can detect
        // whether ApplyCommandAsync altered it.
        string? concurrencyBefore = (entity as IOptimisticConcurrency)?.GetConcurrency();

        await ApplyCommandAsync(command, entity, cancellationToken);

        if (concurrencyBefore is not null
            && entity is IOptimisticConcurrency entityConcurrent
            && entityConcurrent.GetConcurrency() != concurrencyBefore)
        {
            ConcurrencyError("Concurrency error!");
            return entity;
        }

        // Prefer a soft-delete when the entity supports it; otherwise remove it outright.
        if (CapabilityInvoker<TEntity, ISoftDelete>.Instance.HasAny)
        {
            CapabilityInvoker<TEntity, ISoftDelete>.Instance.Invoke(entity, RequestContext);
        }
        else
        {
            dbContext.Remove(entity);
        }

        return entity;
    }
}
