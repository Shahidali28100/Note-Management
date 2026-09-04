using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Tests.Integration.TestSupport;

namespace NoteManagement.Tests.Integration.Infrastructure;

[TestClass]
public sealed class ApplicationDbContextTests
{
    private static readonly string TestConnectionString =
        TestConnectionStringFactory.ForDatabase("NoteManagementDb_IntegrationTests");

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
