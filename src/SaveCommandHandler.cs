using FluentValidation;
using FastEndpoints;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Pdk.Api.Events;
using Pdk.Api.Services;
using Pdk.Api.StateManager;
using Pdk.Api.Validation;
using Pdk.Core.Abstractions;
using Pdk.Core.Data;
using System.Linq.Expressions;

namespace Pdk.Api.Commands;

/// <summary>
/// Template-method base for handlers that load, mutate and persist a single aggregate
/// (<typeparamref name="TEntity"/>) in response to a <see cref="CommandBase{TResult}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The processing is organized as a fixed sequence of overridable steps:
/// </para>
/// <list type="number">
///   <item><description><see cref="OnStartingAsync"/> – optional initialization.</description></item>
///   <item><description><see cref="ValidateCommandAsync"/> – FluentValidation of the command (opt-in via <see cref="ValidateCommand"/>).</description></item>
///   <item><description><see cref="GetEntityAsync"/> – load or create the target entity (specialized per operation).</description></item>
///   <item><description><see cref="FillEntityFields"/> – apply additional field mappings.</description></item>
///   <item><description><see cref="ValidateCommandEntityAsync"/> – entity-level validation.</description></item>
///   <item><description><see cref="DoEntityStates"/> – child/detail state processing.</description></item>
///   <item><description><see cref="BeforeSaveAsync"/> – last chance to modify the entity before persistence.</description></item>
///   <item><description><see cref="SaveAsync"/> – persist via the DbContext.</description></item>
///   <item><description><see cref="AfterSaveAsync"/> – post-save side effects.</description></item>
///   <item><description>commit transaction, <see cref="GetResultAsync"/> + <see cref="SetResult"/>.</description></item>
/// </list>
/// <para>
/// After each step the pipeline checks <see cref="HandlerBase{TRequest,TResult}.IsResultSet"/> and
/// short-circuits, so any step that produces an error outcome (NotFound, Forbidden, …) stops the
/// remaining steps from running.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The response value type.</typeparam>
public abstract class SaveCommandHandler<TEntity, TCommand, TResult> : HandlerBase<TCommand, TResult>
    where TEntity : class
    where TCommand : CommandBase<TResult>
{
    /// <summary>Initializes the handler with its DbContext and request context.</summary>
    protected SaveCommandHandler(IDbContextBase dbContext, IRequestContext requestContext)
        : base(dbContext, requestContext)
    {
    }

    /// <summary>
    /// When <c>true</c>, the command is validated against its FluentValidation validator before
    /// the entity is loaded. Defaults to <c>false</c>; set to <c>true</c> in a subclass constructor
    /// to opt in.
    /// </summary>
    protected bool ValidateCommand = false;

    /// <summary>
    /// Indicates whether the handler is running in update mode. Set by the create/update
    /// specializations so that shared steps can branch on the operation kind.
    /// </summary>
    public bool IsUpdate { get; set; }

    /// <summary>
    /// Runs the full save pipeline, short-circuiting as soon as any step sets a result.
    /// </summary>
    protected override async Task RunAsync(TCommand command, CancellationToken ct)
    {
        await OnStartingAsync(command, ct);

        if (ValidateCommand)
        {
            await ValidateCommandAsync(command, ct);
            if (IsResultSet) return;
        }

        var entity = await GetEntityAsync(command, ct);
        if (IsResultSet) return;

        await FillEntityFields(command, entity, ct);

        await ValidateCommandEntityAsync(command, entity, ct);
        if (IsResultSet) return;

        await DoEntityStates(command, entity, ct);
        if (IsResultSet) return;

        //await ApplyToRaiseEvents(entity, command, ct);
        await BeforeSaveAsync(command, entity, ct);
        if (IsResultSet) return;

        var affected = await SaveAsync(command, entity, ct);
        if (IsResultSet) return;
        if (affected == 0)
        {
            Error($"Query affected {affected} rows!");
            return;
        }

        await AfterSaveAsync(command, entity, ct);
        if (IsResultSet) return;

        await CommitTransactionAsync(ct);

        var result = await GetResultAsync(entity, command, affected, ct);
        SetResult(result);
    }

    /// <summary>Resolves the FluentValidation validator for the command from the request scope.</summary>
    protected virtual IValidator<TCommand> GetValidator()
        => RequestContext.GetValidator<TCommand>();

    /// <summary>Optional initialization hook executed before validation and entity loading.</summary>
    protected virtual Task OnStartingAsync(TCommand command, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Validates the command against its <see cref="GetValidator"/> and, on failure, sets a
    /// <see cref="HandlerBase{TRequest,TResult}.ValidationFailed(object)"/> result.
    /// </summary>
    protected virtual async Task ValidateCommandAsync(TCommand command, CancellationToken ct)
    {
        var validator = GetValidator();
        var validationResult = await validator.ValidateAsync(command, ct);
        if (!validationResult.IsValid)
            ValidationFailed(validationResult);
    }

    /// <summary>
    /// Loads (update/delete) or creates (create) the target entity. Implemented by the
    /// operation-specific subclasses.
    /// </summary>
    protected abstract Task<TEntity> GetEntityAsync(TCommand command, CancellationToken ct);

    protected async Task<TEntity> ApplyCommandToEntityAsync(TCommand command, TEntity entity, CancellationToken cancellationToken)
    {
        var filter = await GetSecurityFilter(command);
        if (filter is not null && !filter.Compile().Invoke(entity))
        {
            Forbidden("Action is not allowed!");
            return entity;
        }

        // Snapshot the optimistic-concurrency token before applying changes so we can detect
        // whether ApplyCommandAsync/ApplyCommandDetailAsync altered it.
        string? concurrencyBefore = (entity as IOptimisticConcurrency)?.GetConcurrency();

        await ApplyCommandAsync(command, entity, cancellationToken);
        await ApplyCommandDetailAsync(command, entity, cancellationToken);

        if (concurrencyBefore is not null
            && entity is IOptimisticConcurrency entityConcurrent
            && entityConcurrent.GetConcurrency() != concurrencyBefore)
        {
            ConcurrencyError("Concurrency error!");
            return entity;
        }

        CapabilityInvoker<TEntity, IModified>.Instance.Invoke(entity, RequestContext);
        return entity;
    }

    /// <summary>
    /// Processes child/detail entity states. The default implementation is a no-op; the
    /// detail-sync machinery is commented out below and may be re-enabled per aggregate.
    /// </summary>
    protected virtual async Task DoEntityStates(TCommand command, TEntity entity, CancellationToken ct)
    {
        if (entity is IEntityWithState entityWithState)
        {
            var entityStateManager = RequestContext.GetEntityStateManager<TEntity>();
            if (entityStateManager is not null)
                entityStateManager.ProcessAction(entityWithState, IsUpdate ? "update" : "create");
        }

        foreach (var detail in DetailFieldInfos)
        {
            await detail.DoDetailEntityStates(entity, dbContext, RequestContext, ct);
        }
    }

    /// <summary>
    /// Applies additional field mappings onto <paramref name="entity"/> after it has been loaded
    /// or created. Default no-op.
    /// </summary>
    protected virtual Task FillEntityFields(TCommand command, TEntity entity, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>Entity-level validation hook executed after <see cref="FillEntityFields"/>. Default no-op.</summary>
    protected virtual Task ValidateCommandEntityAsync(TCommand command, TEntity entity, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Loads the target entity using <see cref="CreateItemFilter"/> against <see cref="GetEntitySet"/>.
    /// </summary>
    protected async Task<TEntity?> LoadEntityAsync(TCommand command, CancellationToken ct)
    {
        var itemFilter = CreateItemFilter(command);
        var entitySet = await ApplySecurityFilter(GetEntitySet(command), command);
        var matches = await entitySet
            .Where(itemFilter)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"More than one {typeof(TEntity).Name} matched the command. " +
                "MatchBy fields must be protected by a unique database key or index.");

        return matches.SingleOrDefault();
    }

    /// <summary>
    /// Applies the optional <see cref="GetSecurityFilter"/> expression to a query so security
    /// constraints are enforced on the database side.
    /// </summary>
    protected virtual async Task<IQueryable<TEntity>> ApplySecurityFilter(
        IQueryable<TEntity> query,
        TCommand command)
    {
        var filter = await GetSecurityFilter(command);
        if (filter is null)
            return query;
        return query.Where(filter);
    }

    /// <summary>
    /// Builds the per-entity security filter expression for the current command. Returns
    /// <c>null</c> (no restriction) when no <see cref="HandlerBase{TRequest,TResult}.PermissionKey"/>
    /// is defined; the permission-based implementation is commented out below.
    /// </summary>
    protected virtual async Task<Expression<Func<TEntity, bool>>?> GetSecurityFilter(TCommand command)
    {
        if (PermissionKey is null)
            return null;
        var decision = await AuthorizationService.AuthorizeAsync(PermissionKey.Id);
        return AuthorizationSecurityFilterResolver.Resolve<TEntity>(RequestContext, decision);
    }

    /// <summary>Last chance to modify the entity before persistence. Default no-op.</summary>
    protected virtual Task BeforeSaveAsync(TCommand command, TEntity entity, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Persists pending changes via <c>SaveChangesAsync</c> and converts a concurrency conflict
    /// into a <see cref="HandlerBase{TRequest,TResult}.ConcurrencyError(string)"/> result.
    /// </summary>
    protected virtual async Task<int> SaveAsync(
        TCommand command,
        TEntity entity,
        CancellationToken ct)
    {
        try
        {
            return await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            ConcurrencyError("Concurrency error!");
            return 0;
        }
    }

    /// <summary>Post-save side effects. The default implementation is a no-op stub.</summary>
    protected virtual async Task AfterSaveAsync(TCommand command, TEntity entity, CancellationToken ct)
    {
        foreach (var postAction in postSaveActions)
            await postAction();

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Builds the response value from the saved entity. The default implementation returns
    /// <c>default(TResult)</c>; subclasses (including generated handlers) override this.
    /// </summary>
    protected virtual Task<TResult> GetResultAsync(TEntity entity, TCommand command, int affected, CancellationToken ct)
        => Task.FromResult(default(TResult)!);

    /// <summary>
    /// Returns the queryable entity set with all registered command details included, followed
    /// by developer-owned query customization.
    /// </summary>
    protected virtual IQueryable<TEntity> GetEntitySet(TCommand command)
    {
        IQueryable<TEntity> query = dbContext.Set<TEntity>();
        foreach (var detailFieldInfo in DetailFieldInfos)
            query = detailFieldInfo.SetInclude(query);
        return CustomizeEntitySet(query, command);
    }

    /// <summary>
    /// Allows an application handler to add query behavior without replacing the generated
    /// detail includes applied by <see cref="GetEntitySet(TCommand)"/>.
    /// </summary>
    protected virtual IQueryable<TEntity> CustomizeEntitySet(
        IQueryable<TEntity> query,
        TCommand command)
        => query;

    /// <summary>
    /// Builds the predicate that uniquely identifies the target entity for update/delete.
    /// Generated handlers override this (e.g. <c>e => e.Id == command.Id</c>).
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>> CreateItemFilter(TCommand command)
        => throw new NotImplementedException();

    /// <summary>
    /// Maps the command onto an existing or newly-created entity. Generated handlers override
    /// this to copy the command payload onto the entity.
    /// </summary>
    protected virtual Task<TEntity> ApplyCommandAsync(TCommand command, TEntity entity, CancellationToken ct)
        => Task.FromResult(entity);

    /// <summary>Applies child/detail command payloads onto <paramref name="entity"/>. Default no-op.</summary>
    protected virtual Task<TEntity> ApplyCommandDetailAsync(TCommand command, TEntity entity, CancellationToken ct)
        => Task.FromResult(entity);

    /// <summary>Sets the success result; defaults to <see cref="HandlerBase{TRequest,TResult}.Ok(TResult)"/>.</summary>
    protected virtual void SetResult(TResult result)
        => Ok(result);

    private async Task ApplyToRaiseEvents(TEntity entity, TCommand command, CancellationToken ct)
    {
        if (!HasAnyEvent())
            return;

        await StartTransactionAsync(ct);
        postSaveActions.Add(async () =>
        {
            await RaiseEvent(entity, command, ct);
        });

    }
    protected virtual bool HasAnyEvent()
    {
        return false;
    }
    protected virtual Task RaiseEvent(TEntity entity, TCommand command, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
    private IPublishEndpoint PublishEndpoint => RequestContext.GetRequiredService<IPublishEndpoint>();
    protected async Task PublishEvent<TEvent>(TEvent eventMessage)
         where TEvent : class, IEventMessage, new()
    {
        var eventContext = new EventContext<TEvent>(RequestContext, eventMessage);
        await eventContext.PublishAsync();
        await PublishEndpoint.Publish(eventMessage,
                context => context.CorrelationId = RequestContext.CorrelationId);
    }
    protected virtual Task<T> MapToEvent<T>(TEntity entity)
        where T : class, IEventMessage, new()
    {
        return Task.FromResult(default(T)!);
    }

    private readonly List<Func<Task>> postSaveActions = [];

    protected List<HandlerDetailFieldInfo<TEntity>> DetailFieldInfos { get; } = [];

    protected void AddDetail<TDetail>(
        Expression<Func<TEntity, IEnumerable<TDetail>>> expression,
        Action<IEntityDetailOption<TDetail>>? entityDetailOptionBuilder = null)
         where TDetail : class
    {
        var detailInfo = new HandlerDetailFieldInfo<TEntity, TDetail>(expression);
        if (entityDetailOptionBuilder is not null)
            entityDetailOptionBuilder(detailInfo);
        DetailFieldInfos.Add(detailInfo);
    }
}

public class HandlerDetailFieldInfo<TEntity>
{
    public virtual IEnumerable<string> GetIncludePaths(string? parentPath = null)
    {
        throw new NotImplementedException();
    }

    public virtual IQueryable<TEntity> SetInclude(IQueryable<TEntity> query)
    {
        throw new NotImplementedException();
    }
    public virtual IQueryable<T1> SetThenInclude<T1>(IIncludableQueryable<T1, IEnumerable<TEntity>> query)
        where T1 : class
    {
        throw new NotImplementedException();
    }

    public virtual Task DoDetailEntityStates(TEntity entity, IDbContextBase dbContext, IRequestContext requestContext, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
public interface IEntityDetailOption<TEntity>
{
    void AddDetail<TDetail>(
        Expression<Func<TEntity, IEnumerable<TDetail>>> expression,
        Action<IEntityDetailOption<TDetail>>? entityDetailOptionBuilder = null)
        where TDetail : class;
}
public class HandlerDetailFieldInfo<TEntity, TDetail> : HandlerDetailFieldInfo<TEntity>, IEntityDetailOption<TDetail>
    where TEntity : class
    where TDetail : class
{
    public HandlerDetailFieldInfo(Expression<Func<TEntity, IEnumerable<TDetail>>> expression)
    {
        this.DetailExpression = expression;
    }

    public Expression<Func<TEntity, IEnumerable<TDetail>>> DetailExpression { get; }

    protected List<HandlerDetailFieldInfo<TDetail>> DetailFieldInfos { get; } = [];

    public void AddDetail<TDetailDetail>(
        Expression<Func<TDetail, IEnumerable<TDetailDetail>>> expression,
        Action<IEntityDetailOption<TDetailDetail>>? entityDetailOptionBuilder = null)
        where TDetailDetail : class
    {
        var detailInfo = new HandlerDetailFieldInfo<TDetail, TDetailDetail>(expression);
        if (entityDetailOptionBuilder is not null)
            entityDetailOptionBuilder(detailInfo);
        DetailFieldInfos.Add(detailInfo);
    }
    public override IQueryable<TEntity> SetInclude(IQueryable<TEntity> query)
    {
        foreach (var path in GetIncludePaths())
            query = query.Include(path);
        return query;
    }

    public override IEnumerable<string> GetIncludePaths(string? parentPath = null)
    {
        if (DetailExpression.Body is not MemberExpression member)
            throw new InvalidOperationException("A detail expression must select a navigation property.");

        var path = string.IsNullOrWhiteSpace(parentPath)
            ? member.Member.Name
            : $"{parentPath}.{member.Member.Name}";
        if (DetailFieldInfos.Count == 0)
        {
            yield return path;
            yield break;
        }

        foreach (var detailFieldInfo in DetailFieldInfos)
        foreach (var childPath in detailFieldInfo.GetIncludePaths(path))
            yield return childPath;
    }
    public override IQueryable<T1> SetThenInclude<T1>(IIncludableQueryable<T1, IEnumerable<TEntity>> query)
        where T1 : class
    {
        var result = query.ThenInclude(DetailExpression);
        foreach (var detailFieldInfo in DetailFieldInfos)
            detailFieldInfo.SetThenInclude(result);
        return result;
    }

    public override async Task DoDetailEntityStates(TEntity entity, IDbContextBase dbContext, IRequestContext requestContext, CancellationToken cancellationToken)
    {
        var details = DetailExpression.Compile().Invoke(entity).ToArray();
        if (details.Length == 0)
            return;

        var detail = details.First();
        if (detail is IEntityWithState)
        {
            var entityStateManager = requestContext.GetEntityStateManager<TDetail>();
            if (entityStateManager is not null)
            {
                foreach (var detailItem in details)
                {
                    var entry = dbContext.Entry(detailItem);
                    string action;
                    switch (entry.State)
                    {
                        case Microsoft.EntityFrameworkCore.EntityState.Detached:
                        case Microsoft.EntityFrameworkCore.EntityState.Added:
                            action = "create";
                            break;
                        case Microsoft.EntityFrameworkCore.EntityState.Modified:
                            action = "update";
                            break;
                        case Microsoft.EntityFrameworkCore.EntityState.Deleted:
                            action = "delete";
                            break;
                        case Microsoft.EntityFrameworkCore.EntityState.Unchanged:
                            continue;
                        default:
                            throw new NotImplementedException("Action is unknown");
                    }
                    entityStateManager.ProcessAction((detailItem as IEntityWithState)!, action);
                }
                var deletedEntities = dbContext.ChangeTracker.Entries<TDetail>().Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted).Select(e => e.Entity);
                foreach (var deleteItem in deletedEntities)
                    entityStateManager.ProcessAction((deleteItem as IEntityWithState)!, "delete");
            }
        }

        if (DetailFieldInfos.Count > 0)
            foreach (var detailItem in details)
            {
                foreach (var detailInfo in DetailFieldInfos)
                    await detailInfo.DoDetailEntityStates(detailItem, dbContext, requestContext, cancellationToken);
            }
    }
}
