using Microsoft.Data.Sqlite;
using AISwarmX.SwarmBus.Core.Store;

namespace AISwarmX.SwarmBus.Tests;

internal sealed class TempBusFixture : IDisposable
{
    public string RootPath { get; }
    public string DbPath { get; }

    public TempBusFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "mobadev-swarmbus-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        DbPath = Path.Combine(RootPath, "swarm-bus.db");
    }

    public SwarmBusStore CreateStore(SwarmBusStoreOptions? options = null) => new(DbPath, options);

    public static void ExecuteRawSql(string dbPath, string sql)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup on Windows.
        }
    }
}

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset? initialUtc = null)
    {
        _utcNow = initialUtc ?? new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
