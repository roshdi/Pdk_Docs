using Microsoft.EntityFrameworkCore;
using Pdk.Core.Abstractions;
using Pdk.Core.Data;

namespace Pdk.Api.Commands;

/// <summary>
/// Handles create commands: instantiates a new entity, applies the command payload, invokes its
/// creation capabilities, adds it to the DbContext, then delegates to
/// <see cref="SaveCommandHandler{TEntity,TCommand,TResult}"/> for persistence and returns the
/// result with a <see cref="System.Net.HttpStatusCode.Created"/> status.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TCommand">The create command type.</typeparam>
/// <typeparam name="TResult">The response value type.</typeparam>
public abstract class CreateCommandHandler<TEntity, TCommand, TResult> : SaveCommandHandler<TEntity, TCommand, TResult>
    where TEntity : class, new()
    where TCommand : CommandBase<TResult>
{
    /// <summary>Initializes the handler with its DbContext and request context.</summary>
    protected CreateCommandHandler(IDbContextBase dbContext, IRequestContext requestContext)
        : base(dbContext, requestContext)
    {
    }

    /// <summary>
    /// Creates a new <typeparamref name="TEntity"/>, applies the command payload, invokes every
    /// implemented creation capability, and tracks it via <c>dbContext.AddAsync</c>.
    /// </summary>
    protected override async Task<TEntity> GetEntityAsync(TCommand command, CancellationToken cancellationToken)
    {
        IsUpdate = false;
        var entity = await ApplyCommandAsync(command, new TEntity(), cancellationToken);
        CapabilityInvoker<TEntity, ICreated>.Instance.Invoke(entity, RequestContext);
        await dbContext.AddAsync(entity, cancellationToken);
        return entity;
    }

    /// <summary>Sets the success result with a <see cref="System.Net.HttpStatusCode.Created"/> status.</summary>
    protected override void SetResult(TResult result)
        => Created(result);

}
