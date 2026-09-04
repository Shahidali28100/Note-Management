## MODIFIED Requirements

### Requirement: EF Core + SQL Server Wiring
The system SHALL configure an `ApplicationDbContext` using Entity Framework Core targeting a local SQL Server Express instance (not SQL Server Express LocalDB), with connection settings externalized to configuration and never committed as plaintext secrets.

LocalDB SHALL NOT be used as the dev-database engine, in local development or CI, because LocalDB does not support SQL Server Full-Text Search (a hard requirement of the `search` capability, AB-1007/FRS-SEARCH-001) — LocalDB runs as a per-user process rather than a Windows service, and SQL Server's Full-Text daemon requires the latter. No LocalDB configuration or reinstall can add Full-Text Search support.

#### Scenario: DbContext resolves at startup
- **WHEN** the `apps/api` application starts in the Development environment
- **THEN** `ApplicationDbContext` successfully opens a connection to the configured local SQL Server Express instance without throwing

#### Scenario: Full-Text Search is available on the configured instance
- **WHEN** a developer or CI runner queries `SELECT SERVERPROPERTY('IsFullTextInstalled')` against the configured SQL Server instance
- **THEN** the result is `1` — confirming the instance can host the full-text catalog/index the `search` capability depends on
