using Pdk.Core.Abstractions;
using Pdk.Core.Data;
using Pdk.Core.Utilities;

namespace Pdk.Api.Commands;

/// <summary>
/// Handles update commands: loads the entity, enforces the security filter, applies the command
/// payload, detects optimistic-concurrency conflicts, then invokes modification capabilities
/// before delegating to <see cref="SaveCommandHandler{TEntity,TCommand,TResult}"/> for persistence.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TCommand">The update command type.</typeparam>
/// <typeparam name="TResult">The response value type.</typeparam>
public abstract class UpdateCommandHandler<TEntity, TCommand, TResult> : SaveCommandHandler<TEntity, TCommand, TResult>
    where TEntity : class
    where TCommand : CommandBase<TResult>
{
    /// <summary>Initializes the handler with its DbContext and request context.</summary>
    protected UpdateCommandHandler(IDbContextBase dbContext, IRequestContext requestContext)
        : base(dbContext, requestContext)
    {
    }

    /// <summary>
    /// Loads the target entity, applies the security/concurrency checks and the command payload,
    /// and prepares it for capability invocation. Produces a
    /// <c>NotFound</c>/<c>Forbidden</c>/<c>ConcurrencyError</c>
    /// result on failure; the pipeline short-circuits accordingly.
    /// </summary>
    protected override async Task<TEntity> GetEntityAsync(TCommand command, CancellationToken cancellationToken)
    {
        IsUpdate = true;

        var entity = await LoadEntityAsync(command, cancellationToken);
        if (entity is null)
        {
            NotFound($"Entity \"{typeof(TEntity).Name}\" ({command.ToJson()}) was not found.");
            return entity!;
        }
        return await ApplyCommandToEntityAsync(command, entity, cancellationToken);
    }

}
