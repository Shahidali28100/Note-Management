using Microsoft.Data.SqlClient;

namespace NoteManagement.Tests.Integration.TestSupport;

/// <summary>
/// Builds an isolated, per-test-class SQL Server connection string from whatever server/auth
/// environment the test process is actually running in, instead of a connection string
/// hardcoded to one environment.
///
/// Locally this falls back to a trusted connection against ".\SQLEXPRESS" — the instance every
/// dev is expected to run per appsettings.Development.json.example (LocalDB can't host Full-Text
/// Search, AB-1007 plan.md §0). In CI, the "backend" job in .github/workflows/ci.yml exports the
/// Linux SQL Server container's connection string as the ConnectionStrings__DefaultConnection
/// env var (consumed by the app itself via ASP.NET Core's env-var config provider); this reads
/// that same variable, keeps its server/auth portion, and swaps in the given per-test-class
/// database name so each test class still gets its own isolated database in either environment.
/// </summary>
internal static class TestConnectionStringFactory
{
    private const string BaseConnectionStringEnvironmentVariable = "ConnectionStrings__DefaultConnection";

    private const string LocalFallbackConnectionString =
        "Server=.\\SQLEXPRESS;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static string ForDatabase(string databaseName)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(BaseConnectionStringEnvironmentVariable);
        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(baseConnectionString) ? LocalFallbackConnectionString : baseConnectionString)
        {
            InitialCatalog = databaseName,
        };

        return builder.ConnectionString;
    }
}
