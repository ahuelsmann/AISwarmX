using System.Text;
using MobaDev.SwarmBus.Core.Store;
using MobaDev.SwarmBus.Core.Wake;

namespace MobaDev.SwarmBus.Mcp;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = utf8NoBom;
        Console.OutputEncoding = utf8NoBom;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var wakeDispatcher = new CodexWakeDispatcher();
            var store = new SwarmBusStore(
                dbPath: null,
                options: new SwarmBusStoreOptions
                {
                    WakeDispatcher = wakeDispatcher.CreateWakeDelegate()
                });

            var cliIndex = Array.FindIndex(args, static a => string.Equals(a, "--cli", StringComparison.Ordinal));
            if (cliIndex >= 0)
            {
                var cliArgs = args[(cliIndex + 1)..];
                return await CliRunner.RunAsync(store, cliArgs, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
            }

            return await McpStdioServer.RunAsync(store, Console.In, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[mobadev-swarm-bus-mcp] Fatal startup error: {ex}").ConfigureAwait(false);
            return 1;
        }
    }
}
