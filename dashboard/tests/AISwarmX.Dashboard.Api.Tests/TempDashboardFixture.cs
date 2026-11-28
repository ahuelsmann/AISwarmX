using Microsoft.Data.Sqlite;

namespace AISwarmX.Dashboard.Api.Tests;

internal sealed class TempDashboardFixture : IDisposable
{
    public string RootPath { get; }
    public string MobaDevPath { get; }
    public string MobaFlowPath { get; }
    public string ClaudeHomePath { get; }
    public string CodexHomePath { get; }
    public string AntigravityHomePath { get; }
    public string GeminiConfigPath { get; }

    public TempDashboardFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"mobadev-dashboard-tests-{Guid.NewGuid():N}");
        MobaDevPath = Path.Combine(RootPath, "MOBAdev");
        MobaFlowPath = Path.Combine(RootPath, "MOBAflow");
        ClaudeHomePath = Path.Combine(RootPath, ".claude");
        CodexHomePath = Path.Combine(RootPath, ".codex");
        AntigravityHomePath = Path.Combine(RootPath, ".gemini", "antigravity");
        GeminiConfigPath = Path.Combine(RootPath, ".gemini", "config");

        Directory.CreateDirectory(MobaDevPath);
        Directory.CreateDirectory(MobaFlowPath);
        Directory.CreateDirectory(ClaudeHomePath);
        Directory.CreateDirectory(CodexHomePath);
        Directory.CreateDirectory(AntigravityHomePath);
        Directory.CreateDirectory(GeminiConfigPath);
    }

    public static void CreateSqliteDb(string dbPath, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        using var conn = new SqliteConnection(connStr);
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
            // Best-effort cleanup for Windows temp handles.
        }
    }
}
