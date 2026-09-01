using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Infrastructure.Data;

public sealed class UnitOfWork : IUnitOfWork
{
    // SQL Server error numbers for a unique-index/unique-constraint violation.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private readonly ApplicationDbContext _dbContext;

    public UnitOfWork(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Translates EF Core's <see cref="DbUpdateException"/> into the Application-owned
    /// <see cref="UniqueConstraintViolationException"/> when it's caused by a unique-index
    /// violation, so Application-layer callers (which don't reference EF Core) can catch it by
    /// type instead of by string-matching a leaked provider exception.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            throw new UniqueConstraintViolationException(ex);
        }
    }

    /// <summary>
    /// Uses EF Core's execution strategy (rather than a bare `using var tx = ...`) so a manual
    /// transaction doesn't defeat SQL Server's automatic connection-retry behavior.
    /// </summary>
    public async Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
