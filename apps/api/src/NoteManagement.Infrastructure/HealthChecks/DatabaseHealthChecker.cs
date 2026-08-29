using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Infrastructure.HealthChecks;

public sealed class DatabaseHealthChecker : IDatabaseHealthChecker
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseHealthChecker(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => _dbContext.Database.CanConnectAsync(cancellationToken);
}
