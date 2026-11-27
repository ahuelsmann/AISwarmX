using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Store;
using MobaDev.SwarmBus.Core.Wake;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

[Collection("EnvVarTests")]
public sealed class CodexWakeDispatcherTests
{
    private sealed class FakeCodexSession : ICodexAppServerSession
    {
        public List<string> Calls { get; } = [];
        public string? ResumeActiveTurnId { get; init; }
        public Exception? SteerException { get; init; }
        public string? SteerResultTurnId { get; init; }
        public string? StartResultTurnId { get; init; }
        public string? LastExpectedTurnId { get; private set; }
        public string? LastText { get; private set; }
        public string? LastClientUserMessageId { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Calls.Add("initialize");
            return Task.CompletedTask;
        }

        public Task<string?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            Calls.Add($"thread/resume:{threadId}");
            return Task.FromResult(ResumeActiveTurnId);
        }

        public Task<string?> SteerTurnAsync(
            string threadId,
            string expectedTurnId,
            string text,
            string clientUserMessageId,
            CancellationToken cancellationToken)
        {
            Calls.Add("turn/steer");
            LastExpectedTurnId = expectedTurnId;
            LastText = text;
            LastClientUserMessageId = clientUserMessageId;
            if (SteerException is not null)
            {
                throw SteerException;
            }

            return Task.FromResult<string?>(SteerResultTurnId ?? expectedTurnId);
        }

        public Task<string?> StartTurnAsync(
            string threadId,
            string text,
            string clientUserMessageId,
            CancellationToken cancellationToken)
        {
            Calls.Add("turn/start");
            LastText = text;
            LastClientUserMessageId = clientUserMessageId;
            return Task.FromResult(StartResultTurnId);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DispatchAsyncSteersWhenActiveTurnExistsAndIntegratesWithStore()
    {
        using var fx = new TempBusFixture();
        var session = new FakeCodexSession
        {
            ResumeActiveTurnId = "turn-active-1",
            SteerResultTurnId = "turn-active-1"
        };

        var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
        {
            AppServerUrl = "ws://127.0.0.1:45999",
            SessionFactory = (_, _) => Task.FromResult<ICodexAppServerSession>(session)
        });

        var store = fx.CreateStore(new SwarmBusStoreOptions
        {
            WakeDispatcher = dispatcher.CreateWakeDelegate()
        });

        var send = await store.SendMessageAsync(new SwarmSendRequest(
            From: "claude-opus",
            To: "codex",
            Topic: "design-go",
            Body: "Proceed with slice 2",
            PrNumber: 56,
            Wake: true,
            CodexThreadId: "thread-abc"));

        Assert.True(send.Ok);
        Assert.Equal("ok", send.WakeState);
        Assert.NotNull(send.WakeResults);
        Assert.Single(send.WakeResults);
        Assert.True(send.WakeResults[0].Ok);
        Assert.Equal("steer", send.WakeResults[0].Reason);
        Assert.Equal("thread-abc", send.WakeResults[0].ThreadId);

        Assert.Equal(["initialize", "thread/resume:thread-abc", "turn/steer"], session.Calls);
        Assert.Equal("turn-active-1", session.LastExpectedTurnId);
        Assert.Equal($"swarmbus-{send.Id}", session.LastClientUserMessageId);
        Assert.Contains("Proceed with slice 2", session.LastText);
    }

    [Fact]
    public async Task DispatchAsyncFallsBackToTurnStartWhenSteerFailsConcurrently()
    {
        var session = new FakeCodexSession
        {
            ResumeActiveTurnId = null,
            SteerException = new InvalidOperationException("No active turn"),
            StartResultTurnId = "turn-fresh-9"
        };

        var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
        {
            AppServerUrl = "ws://127.0.0.1:45999",
            SessionFactory = (_, _) => Task.FromResult<ICodexAppServerSession>(session)
        });
        dispatcher.SetActiveTurnId("thread-xyz", "turn-stale-0");

        var outcome = await dispatcher.DispatchAsync(new SwarmWakeContext(
            MessageId: 42,
            WorkspaceId: "default",
            Sender: "watchdog",
            Recipient: "codex",
            PrNumber: 56,
            Topic: "AllChecksGreenReady",
            Body: "Green now",
            CodexThreadId: "thread-xyz",
            FormattedMessage: "[SwarmBus #42] watchdog -> codex (PR #56) [AllChecksGreenReady]: Green now"));

        Assert.True(outcome.Ok);
        Assert.Equal("start", outcome.DeliveryMode);
        Assert.Equal("turn-fresh-9", outcome.TurnId);
        Assert.Equal("turn-fresh-9", dispatcher.GetActiveTurnId("thread-xyz"));
        Assert.Equal(["initialize", "thread/resume:thread-xyz", "turn/steer", "turn/start"], session.Calls);
    }

    [Fact]
    public async Task WebSocketSessionIdleTurnStartParsesNestedTurnIdOverLoopbackWebSocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var receivedMethods = new List<string>();

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            // Complete RFC 6455 HTTP upgrade handshake.
            var requestText = await ReadHttpHeadersAsync(stream);
            var keyLine = requestText
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
            var clientKey = keyLine["Sec-WebSocket-Key:".Length..].Trim();
            var acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(clientKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var handshakeResponse =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(handshakeResponse));

            using var ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
            var buf = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }

                var msgJson = Encoding.UTF8.GetString(buf, 0, res.Count);
                using var doc = JsonDocument.Parse(msgJson);
                var root = doc.RootElement;
                var method = root.GetProperty("method").GetString()!;
                receivedMethods.Add(method);

                if (!root.TryGetProperty("id", out var idElem))
                {
                    continue; // Notification (initialized)
                }

                var id = idElem.GetInt64();
                object resultObj = method switch
                {
                    "initialize" => new { serverInfo = new { name = "fake-codex" } },
                    "thread/resume" => new { threadId = "thread-ws" },
                    "turn/start" => new { turn = new { id = "turn-ws-777", status = "inProgress" } },
                    _ => new { }
                };

                var replyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = resultObj
                }));
                await ws.SendAsync(replyBytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        });

        try
        {
            var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
            {
                AppServerUrl = $"ws://127.0.0.1:{port}/"
            });

            var outcome = await dispatcher.DispatchAsync(new SwarmWakeContext(
                MessageId: 99,
                WorkspaceId: "default",
                Sender: "antigravity-gemini",
                Recipient: "codex",
                PrNumber: 56,
                Topic: "ws-check",
                Body: "Hello via WebSocket",
                CodexThreadId: "thread-ws",
                FormattedMessage: "[SwarmBus #99] Hello via WebSocket")).WaitAsync(TimeSpan.FromSeconds(5));

            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(outcome.Ok);
            Assert.Equal("start", outcome.DeliveryMode);
            Assert.Equal("turn-ws-777", outcome.TurnId);
            Assert.Equal("turn-ws-777", dispatcher.GetActiveTurnId("thread-ws"));
            Assert.Equal(["initialize", "initialized", "thread/resume", "turn/start"], receivedMethods);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DispatchAsyncFallsBackToCliQueueWhenAppServerUrlUnset()
    {
        using var fx = new TempBusFixture();
        var originalAppUrl = Environment.GetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar);
        var originalExe = Environment.GetEnvironmentVariable(CodexWakeDispatcher.CodexExeEnvVar);
        var originalThread = Environment.GetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar, null);
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar, "thread-env-cli");

            var capturedArgsPath = Path.Combine(fx.RootPath, "captured-args.txt");
            var fakeScriptPath = Path.Combine(fx.RootPath, "fake-codex.mjs");
            var scriptBody =
                "import fs from \"node:fs\";\n" +
                $"fs.writeFileSync({JsonSerializer.Serialize(capturedArgsPath)}, JSON.stringify(process.argv.slice(2)));\n" +
                "process.exit(0);\n";
            await File.WriteAllTextAsync(fakeScriptPath, scriptBody);

            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexExeEnvVar, fakeScriptPath);

            var dispatcher = new CodexWakeDispatcher();
            var outcome = await dispatcher.DispatchAsync(new SwarmWakeContext(
                MessageId: 105,
                WorkspaceId: "default",
                Sender: "claude-opus",
                Recipient: "codex",
                PrNumber: null,
                Topic: "cli-fallback",
                Body: "Wake via CLI",
                CodexThreadId: null,
                FormattedMessage: "[SwarmBus #105] Wake via CLI"));

            Assert.True(outcome.Ok);
            Assert.Equal("cli-queue", outcome.DeliveryMode);
            Assert.Equal("thread-env-cli", outcome.ThreadId);

            var recorded = await File.ReadAllTextAsync(capturedArgsPath);
            Assert.Contains("queue", recorded);
            Assert.Contains("--thread", recorded);
            Assert.Contains("thread-env-cli", recorded);
            Assert.Contains("--message", recorded);

            // Missing executable returns failed outcome cleanly.
            var missingExeDispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
            {
                CodexExecutablePath = Path.Combine(fx.RootPath, "does-not-exist.exe")
            });
            var missingOutcome = await missingExeDispatcher.DispatchAsync(new SwarmWakeContext(
                MessageId: 106,
                WorkspaceId: "default",
                Sender: "claude-opus",
                Recipient: "codex",
                PrNumber: null,
                Topic: "missing",
                Body: "Missing exe",
                CodexThreadId: "t1",
                FormattedMessage: "msg"));
            Assert.False(missingOutcome.Ok);
            Assert.Equal("cli-queue", missingOutcome.DeliveryMode);
            Assert.Equal("codex_exe_not_found", missingOutcome.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar, originalAppUrl);
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexExeEnvVar, originalExe);
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar, originalThread);
        }
    }

    [Fact]
    public async Task DispatchAsyncRejectsUnsafeCmdExecutableWithoutExecuting()
    {
        using var fx = new TempBusFixture();
        var originalAppUrl = Environment.GetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar);
        var originalExe = Environment.GetEnvironmentVariable(CodexWakeDispatcher.CodexExeEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar, null);

            var markerPath = Path.Combine(fx.RootPath, "should-not-exist.txt");
            var maliciousCmdPath = Path.Combine(fx.RootPath, "evil-codex.cmd");
            await File.WriteAllTextAsync(
                maliciousCmdPath,
                $"@echo off\r\necho pwned > \"{markerPath}\"\r\nrem x\" & calc & \"\r\n");

            var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
            {
                CodexExecutablePath = maliciousCmdPath,
                DefaultThreadId = "thread-safe-check"
            });

            var outcome = await dispatcher.DispatchAsync(new SwarmWakeContext(
                MessageId: 107,
                WorkspaceId: "default",
                Sender: "claude-opus",
                Recipient: "codex",
                PrNumber: 56,
                Topic: "batbadbut-check",
                Body: "x\" & calc & \"",
                CodexThreadId: null,
                FormattedMessage: "x\" & calc & \""));

            Assert.False(outcome.Ok);
            Assert.Equal("cli-queue", outcome.DeliveryMode);
            Assert.Equal("unsafe_executable", outcome.Reason);
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.AppServerUrlEnvVar, originalAppUrl);
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexExeEnvVar, originalExe);
        }
    }

    [Fact]
    public async Task DispatchAsyncReturnsNoThreadIdWhenUnconfigured()
    {
        var originalThread = Environment.GetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar, null);

            var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
            {
                AppServerUrl = "ws://127.0.0.1:45999"
            });

            var context = new SwarmWakeContext(
                MessageId: 108,
                WorkspaceId: "default",
                Sender: "watchdog",
                Recipient: "codex",
                PrNumber: 56,
                Topic: "missing-thread",
                Body: "Should not wake",
                CodexThreadId: null,
                FormattedMessage: "msg");

            var outcome = await dispatcher.DispatchAsync(context);
            Assert.False(outcome.Ok);
            Assert.Equal("none", outcome.DeliveryMode);
            Assert.Equal(string.Empty, outcome.ThreadId);
            Assert.Equal("no_thread_id", outcome.Reason);

            var wakeDelegate = dispatcher.CreateWakeDelegate();
            var wakeResult = await wakeDelegate(context, CancellationToken.None);
            Assert.False(wakeResult.Attempted);
            Assert.False(wakeResult.Ok);
            Assert.Equal("no_thread_id", wakeResult.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodexWakeDispatcher.CodexThreadIdEnvVar, originalThread);
        }
    }

    [Fact]
    public void ExtractTurnIdPicksLatestInProgressTurnFromThreadResumeResponse()
    {
        using var activeDoc = JsonDocument.Parse("""
            {
              "thread": {
                "id": "thread-1",
                "turns": [
                  { "id": "turn-old-done", "status": "completed" },
                  { "id": "turn-older-active", "status": "inProgress" },
                  { "id": "turn-latest-active", "status": "inProgress" },
                  { "id": "turn-failed", "status": "failed" }
                ]
              }
            }
            """);
        Assert.Equal("turn-latest-active", WebSocketCodexAppServerSession.ExtractTurnId(activeDoc.RootElement));

        using var completedOnlyDoc = JsonDocument.Parse("""
            {
              "thread": {
                "id": "thread-1",
                "turns": [
                  { "id": "turn-1", "status": "completed" },
                  { "id": "turn-2", "status": "interrupted" }
                ]
              }
            }
            """);
        Assert.Null(WebSocketCodexAppServerSession.ExtractTurnId(completedOnlyDoc.RootElement));
    }

    [Theory]
    [InlineData("Authentication failed: invalid token")]
    [InlineData("Codex App-Server 'turn/steer' failed: Rate limit exceeded")]
    public async Task DispatchAsyncDoesNotFallThroughToTurnStartOnNonTurnSteerErrors(string errorMessage)
    {
        var session = new FakeCodexSession
        {
            ResumeActiveTurnId = "turn-active-55",
            SteerException = new InvalidOperationException(errorMessage),
            StartResultTurnId = "turn-should-not-start"
        };

        var dispatcher = new CodexWakeDispatcher(new CodexWakeDispatcherOptions
        {
            AppServerUrl = "ws://127.0.0.1:45999",
            SessionFactory = (_, _) => Task.FromResult<ICodexAppServerSession>(session)
        });

        var outcome = await dispatcher.DispatchAsync(new SwarmWakeContext(
            MessageId: 109,
            WorkspaceId: "default",
            Sender: "claude-opus",
            Recipient: "codex",
            PrNumber: 56,
            Topic: "auth-failure",
            Body: "Should fail cleanly",
            CodexThreadId: "thread-auth",
            FormattedMessage: "msg"));

        Assert.False(outcome.Ok);
        Assert.Equal("app_server_error", outcome.Reason);
        Assert.Contains(errorMessage, outcome.Error);
        Assert.Equal(["initialize", "thread/resume:thread-auth", "turn/steer"], session.Calls);
        Assert.Equal("turn-active-55", dispatcher.GetActiveTurnId("thread-auth"));
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read <= 0)
            {
                break;
            }

            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        return sb.ToString();
    }
}
