using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Tests.Integration.Infrastructure;

[TestClass]
public sealed class ApplicationDbContextTests
{
    private const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=NoteManagementDb_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    [TestMethod]
    public async Task CanConnectAsync_AfterMigration_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(TestConnectionString));

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        var canConnect = await dbContext.Database.CanConnectAsync();

        Assert.IsTrue(canConnect);
    }
}
