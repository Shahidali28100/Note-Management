using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;

namespace NoteManagement.Tests.Unit.Application;

[TestClass]
public sealed class HealthCheckServiceTests
{
    [TestMethod]
    public async Task CheckAsync_WhenDatabaseReachable_ReturnsHealthyStatus()
    {
        var sut = new HealthCheckService(new FakeDatabaseHealthChecker(canConnect: true));

        var result = await sut.CheckAsync(CancellationToken.None);

        Assert.AreEqual("healthy", result.Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenDatabaseUnreachable_ThrowsInvalidOperationException()
    {
        var sut = new HealthCheckService(new FakeDatabaseHealthChecker(canConnect: false));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.CheckAsync(CancellationToken.None));
    }

    /// <summary>
    /// Hand-rolled test double — the interface has exactly one method, so a mocking
    /// library isn't worth adding as a dependency for this ticket.
    /// </summary>
    private sealed class FakeDatabaseHealthChecker : IDatabaseHealthChecker
    {
        private readonly bool _canConnect;

        public FakeDatabaseHealthChecker(bool canConnect)
        {
            _canConnect = canConnect;
        }

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
            => Task.FromResult(_canConnect);
    }
}
