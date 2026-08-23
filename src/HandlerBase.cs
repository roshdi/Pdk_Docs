using FastEndpoints;
using Microsoft.EntityFrameworkCore.Storage;
using Pdk.Core;
using Pdk.Core.Abstractions;
using Pdk.Core.Data;
using Pdk.Api.Services;
using Pdk.Core.Security;
using System.Net;

namespace Pdk.Api.Commands;

/// <summary>
/// Non-generic base for all command/query handlers. Exposes the ambient
/// <see cref="RequestContext"/> together with lazily-resolved authentication and authorization
/// services scoped to the current request.
/// </summary>
public abstract class HandlerBase
{
    /// <summary>
    /// The context of the request currently being handled (correlation id, scoped services, …).
    /// </summary>
    public IRequestContext RequestContext { get; }

    private IAuthenticationService? _authenticationService;

    /// <summary>
    /// Lazily resolves the <see cref="IAuthenticationService"/> for the current request from
    /// <see cref="RequestContext"/>; the resolved instance is cached for the handler's lifetime.
    /// </summary>
    public IAuthenticationService AuthenticationService
        => _authenticationService ??= RequestContext.GetRequiredService<IAuthenticationService>();

    private IAuthorizationService? _authorizationService;

    /// <summary>
    /// Lazily resolves the <see cref="IAuthorizationService"/> for the current request from
    /// <see cref="RequestContext"/>; the resolved instance is cached for the handler's lifetime.
    /// </summary>
    public IAuthorizationService AuthorizationService
        => _authorizationService ??= RequestContext.GetRequiredService<IAuthorizationService>();

    /// <summary>Initializes the handler with its request context.</summary>
    protected HandlerBase(IRequestContext requestContext)
    {
        RequestContext = requestContext;
    }
}

/// <summary>
/// Generic command/query handler base that implements FastEndpoints' <c>ICommandHandler</c> by
/// delegating the actual work to the abstract <see cref="RunAsync"/> pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline (<see cref="RunAsync"/>) signals completion by setting <see cref="Result"/> via
/// the result helpers (<see cref="Ok"/>, <see cref="NotFound(string)"/>, <see cref="Forbidden"/>,
/// …). After each pipeline step, a short-circuit check on <see cref="IsResultSet"/> ends the
/// processing as soon as an outcome is produced, so later steps run only while the request is
/// still eligible to succeed.
/// </para>
/// <para>
/// Subclasses typically derive from <see cref="SaveCommandHandler{TEntity,TCommand,TResult}"/> or
/// one of its concrete derivatives rather than this class directly.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The command/query request type.</typeparam>
/// <typeparam name="TResult">The response value type.</typeparam>
public abstract class HandlerBase<TRequest, TResult> : HandlerBase, ICommandHandler<TRequest, IHandlerResult<TResult>>
    where TRequest : ICommand<IHandlerResult<TResult>>
{
    /// <summary>The EF Core-compatible DbContext used to load and persist entities.</summary>
    protected readonly IDbContextBase dbContext;

    /// <summary>
    /// When overridden, returns the permission key used to build a per-entity security filter.
    /// The default of <c>null</c> means no security filter is applied.
    /// </summary>
    protected virtual PermissionDefinition? PermissionKey => null;

    /// <summary>Initializes the handler with its DbContext and request context.</summary>
    protected HandlerBase(IDbContextBase dbContext, IRequestContext requestContext)
        : base(requestContext)
    {
        this.dbContext = dbContext;
    }

    /// <summary>
    /// FastEndpoints entry point: runs the pipeline, rolling back any open transaction on failure
    /// and returning the accumulated <see cref="Result"/>. Never throws — failures are converted
    /// into a 500 result via <see cref="OnError"/>.
    /// </summary>
    public async Task<IHandlerResult<TResult>> ExecuteAsync(TRequest request, CancellationToken ct)
    {
        try
        {
            await RunAsync(request, ct);
            // A specialized handler may start a transaction before a pipeline step
            // that short-circuits (validation, authorization, concurrency, etc.).
            // Successful save pipelines commit explicitly; any transaction still
            // open here must therefore be rolled back.
            if (_transaction is not null)
                await RollbackTransactionAsync(ct);
            return Result;
        }
        catch (Exception ex)
        {
            await RollbackTransactionAsync();
            OnError(ex);
            return Result;
        }
    }

    /// <summary>
    /// Called when an unexpected exception escapes the pipeline. Converts the error into a 500
    /// result. Override to add logging or custom error payloads.
    /// </summary>
    protected virtual void OnError(Exception ex)
        => Exception("Handler Error!" + ex.Message);

    /// <summary>
    /// Executes the handler pipeline. Implementations should check <see cref="IsResultSet"/> after
    /// each step and short-circuit (return) once an outcome has been produced.
    /// </summary>
    protected abstract Task RunAsync(TRequest request, CancellationToken ct);

    /// <summary>The accumulated outcome of the pipeline, set via the result helpers.</summary>
    protected IHandlerResult<TResult> Result { get; set; } = null!;

    /// <summary><c>true</c> once any result helper has been invoked; drives pipeline short-circuiting.</summary>
    protected bool IsResultSet => Result is not null;

    /// <summary>Sets a <see cref="HttpStatusCode.OK"/> result carrying <paramref name="result"/>.</summary>
    protected void Ok(TResult result)
        => Result = new HandlerResult<TResult>(result);

    /// <summary>Sets a <see cref="HttpStatusCode.Created"/> result carrying <paramref name="result"/>.</summary>
    protected void Created(TResult result)
        => Result = new HandlerResult<TResult>(HttpStatusCode.Created, result);

    /// <summary>Sets a <see cref="HttpStatusCode.NotFound"/> result with no body.</summary>
    protected void NotFound()
        => Result = new HandlerResult<TResult>(HttpStatusCode.NotFound, "Not Found");

    /// <summary>Sets a <see cref="HttpStatusCode.NotFound"/> result with a textual <paramref name="message"/>.</summary>
    protected void NotFound(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.NotFound, message);

    /// <summary>Sets a <see cref="HttpStatusCode.NotFound"/> result with an arbitrary body.</summary>
    protected void NotFound(object objectResult)
        => Result = new HandlerResult<TResult>(HttpStatusCode.NotFound, objectResult);

    /// <summary>Sets a <see cref="HttpStatusCode.Unauthorized"/> result carrying <paramref name="message"/>.</summary>
    protected void Unauthorized(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.Unauthorized, message);

    /// <summary>Sets a <see cref="HttpStatusCode.Forbidden"/> result carrying <paramref name="message"/>.</summary>
    protected void Forbidden(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.Forbidden, message);

    /// <summary>
    /// Sets a <see cref="HttpStatusCode.BadRequest"/> result carrying a validation error payload.
    /// </summary>
    protected void ValidationFailed(object validationErrors)
        => Result = new HandlerResult<TResult>(HttpStatusCode.BadRequest, validationErrors);

    FluentValidation.Results.ValidationResult validationResult;
    protected void AddValidationFailed(string propertyName, string errorMessage)
    {
        if (validationResult is null)
        {
            validationResult = new();
            ValidationFailed(validationResult);
        }
        validationResult.Errors.Add(new FluentValidation.Results.ValidationFailure(propertyName, errorMessage));
    }

    /// <summary>Sets a <see cref="HttpStatusCode.BadRequest"/> result carrying a textual <paramref name="message"/>.</summary>
    protected void Error(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.BadRequest, message);

    /// <summary>Sets a <see cref="HttpStatusCode.InternalServerError"/> result carrying <paramref name="message"/>.</summary>
    protected void Exception(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.InternalServerError, message);

    /// <summary>Sets a <see cref="HttpStatusCode.BadRequest"/> result indicating a concurrency conflict.</summary>
    protected void ConcurrencyError(string message)
        => Result = new HandlerResult<TResult>(HttpStatusCode.BadRequest, message);

    private IDbContextTransaction? _transaction;

    /// <summary>Starts a database transaction unless one is already open for this handler.</summary>
    protected async Task StartTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            return;
        _transaction = await dbContext.Database.BeginTransactionAsync(ct);
    }

    /// <summary>Commits the open transaction, if any, and releases it.</summary>
    protected async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <summary>Rolls back the open transaction, if any, and releases it.</summary>
    protected async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
