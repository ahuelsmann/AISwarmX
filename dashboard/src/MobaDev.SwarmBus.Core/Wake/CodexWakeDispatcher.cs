using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MobaDev.SwarmBus.Core.Contracts;

namespace MobaDev.SwarmBus.Core.Wake;

/// <summary>
/// Rich outcome describing how a Codex wake signal was delivered (<c>steer</c>, <c>start</c>, or <c>cli-queue</c>).
/// </summary>
public sealed record CodexWakeDeliveryOutcome(
    bool Ok,
    string DeliveryMode,
    string ThreadId,
    string? TurnId = null,
    string? Reason = null,
    string? Error = null);

/// <summary>
/// Request descriptor for invoking the fallback <c>codex.exe queue</c> CLI.
/// </summary>
public sealed record CodexCliRequest(
    string ExecutablePath,
    string ThreadId,
    string Message,
    TimeSpan Timeout);

/// <summary>
/// Result of a fallback <c>codex.exe queue</c> CLI invocation.
/// </summary>
public sealed record CodexCliResult(
    int ExitCode,
    string StandardOutput = "",
    string StandardError = "");

/// <summary>
/// Abstraction over a Codex App-Server JSON-RPC session supporting live turn steering and turn creation.
/// </summary>
public interface ICodexAppServerSession : IAsyncDisposable
{
    /// <summary>
    /// Sends the <c>initialize</c> request and <c>initialized</c> notification handshake.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends <c>thread/resume</c> for <paramref name="threadId"/> and returns an active turn ID if one is reported.
    /// </summary>
    Task<string?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends <c>turn/steer</c> against <paramref name="expectedTurnId"/> and returns the resulting turn ID if reported.
    /// </summary>
    Task<string?> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        string text,
        string clientUserMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends <c>turn/start</c> for an idle thread and returns the created turn ID if reported.
    /// </summary>
    Task<string?> StartTurnAsync(
        string threadId,
        string text,
        string clientUserMessageId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Configuration options for <see cref="CodexWakeDispatcher"/>.
/// </summary>
public sealed class CodexWakeDispatcherOptions
{
    /// <summary>
    /// Optional Codex App-Server WebSocket URL (overrides <c>MOBADEV_CODEX_APP_SERVER_URL</c>).
    /// </summary>
    public string? AppServerUrl { get; init; }

    /// <summary>
    /// Optional factory to create and connect <see cref="ICodexAppServerSession"/> instances.
    /// </summary>
    public Func<Uri, CancellationToken, Task<ICodexAppServerSession>>? SessionFactory { get; init; }

    /// <summary>
    /// Optional path to <c>codex.exe</c> for CLI fallback (overrides <c>MOBADEV_CODEX_EXE</c>).
    /// </summary>
    public string? CodexExecutablePath { get; init; }

    /// <summary>
    /// Optional default Codex thread ID when neither message context nor <c>MOBADEV_CODEX_THREAD_ID</c> supplies one.
    /// </summary>
    public string? DefaultThreadId { get; init; }

    /// <summary>
    /// Timeout applied to the CLI fallback process invocation. Defaults to 6 seconds.
    /// </summary>
    public TimeSpan CliTimeout { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Optional CLI process runner override for deterministic testing.
    /// </summary>
    public Func<CodexCliRequest, CancellationToken, Task<CodexCliResult>>? CliRunner { get; init; }
}

/// <summary>
/// WebSocket JSON-RPC 2.0 implementation of <see cref="ICodexAppServerSession"/> using <see cref="ClientWebSocket"/>.
/// </summary>
public sealed class WebSocketCodexAppServerSession : ICodexAppServerSession
{
    private readonly ClientWebSocket _socket = new();
    private long _nextId;
    private bool _disposed;

    private WebSocketCodexAppServerSession()
    {
    }

    /// <summary>
    /// Connects a new WebSocket session to <paramref name="serverUri"/>.
    /// </summary>
    public static async Task<WebSocketCodexAppServerSession> ConnectAsync(Uri serverUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        var session = new WebSocketCodexAppServerSession();
        try
        {
            await session._socket.ConnectAsync(serverUri, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "mobadev-swarm-bus", version = "1.0.0" },
                capabilities = new { }
            },
            cancellationToken).ConfigureAwait(false);

        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(
            "thread/resume",
            new { threadId },
            cancellationToken).ConfigureAwait(false);

        return ExtractTurnId(response.RootElement);
    }

    public async Task<string?> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        string text,
        string clientUserMessageId,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(
            "turn/steer",
            new
            {
                threadId,
                expectedTurnId,
                input = new[] { new { type = "text", text } },
                clientUserMessageId
            },
            cancellationToken).ConfigureAwait(false);

        return ExtractTurnId(response.RootElement) ?? expectedTurnId;
    }

    public async Task<string?> StartTurnAsync(
        string threadId,
        string text,
        string clientUserMessageId,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(
            "turn/start",
            new
            {
                threadId,
                input = new[] { new { type = "text", text } },
                clientUserMessageId
            },
            cancellationToken).ConfigureAwait(false);

        return ExtractTurnId(response.RootElement);
    }

    /// <summary>
    /// Extracts <c>turn.id</c>, <c>turnId</c>, <c>activeTurnId</c>, or the latest <c>inProgress</c> turn from <c>thread.turns</c>.
    /// </summary>
    public static string? ExtractTurnId(JsonElement resultElement)
    {
        if (resultElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (resultElement.TryGetProperty("turn", out var turnObj) &&
            turnObj.ValueKind == JsonValueKind.Object &&
            turnObj.TryGetProperty("id", out var nestedId) &&
            nestedId.ValueKind == JsonValueKind.String)
        {
            var value = nestedId.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        foreach (var propName in new[] { "turnId", "activeTurnId" })
        {
            if (resultElement.TryGetProperty(propName, out var directId) && directId.ValueKind == JsonValueKind.String)
            {
                var value = directId.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        if (resultElement.TryGetProperty("thread", out var threadObj) &&
            threadObj.ValueKind == JsonValueKind.Object &&
            threadObj.TryGetProperty("turns", out var turnsElem) &&
            turnsElem.ValueKind == JsonValueKind.Array)
        {
            for (var i = turnsElem.GetArrayLength() - 1; i >= 0; i--)
            {
                var turnItem = turnsElem[i];
                if (turnItem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (turnItem.TryGetProperty("status", out var statusElem) &&
                    statusElem.ValueKind == JsonValueKind.String &&
                    string.Equals(statusElem.GetString()?.Trim(), "inProgress", StringComparison.OrdinalIgnoreCase) &&
                    turnItem.TryGetProperty("id", out var itemIdElem) &&
                    itemIdElem.ValueKind == JsonValueKind.String)
                {
                    var value = itemIdElem.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
        }

        return null;
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object @params, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextId);
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params
        });

        await SendTextFrameAsync(payload, cancellationToken).ConfigureAwait(false);
        return await ReceiveResponseForIdAsync(requestId, method, cancellationToken).ConfigureAwait(false);
    }

    private Task SendNotificationAsync(string method, object @params, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params
        });

        return SendTextFrameAsync(payload, cancellationToken);
    }

    private Task SendTextFrameAsync(string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private async Task<JsonDocument> ReceiveResponseForIdAsync(long requestId, string method, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (true)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult receiveResult;
            do
            {
                receiveResult = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException($"Codex App-Server closed WebSocket while awaiting '{method}'.");
                }

                if (receiveResult.Count > 0)
                {
                    ms.Write(buffer, 0, receiveResult.Count);
                }
            }
            while (!receiveResult.EndOfMessage);

            var rawText = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            using var doc = JsonDocument.Parse(rawText);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idElem) || idElem.ValueKind != JsonValueKind.Number || !idElem.TryGetInt64(out var incomingId))
            {
                continue;
            }

            if (incomingId != requestId)
            {
                continue;
            }

            if (root.TryGetProperty("error", out var errorElem) && errorElem.ValueKind != JsonValueKind.Null && errorElem.ValueKind != JsonValueKind.Undefined)
            {
                var errMsg = errorElem.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == JsonValueKind.String
                    ? msgElem.GetString()
                    : errorElem.GetRawText();
                throw new InvalidOperationException($"Codex App-Server '{method}' failed: {errMsg}");
            }

            if (root.TryGetProperty("result", out var resultElem))
            {
                return JsonDocument.Parse(resultElem.GetRawText());
            }

            return JsonDocument.Parse("{}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort close on shutdown.
        }
        finally
        {
            _socket.Dispose();
        }
    }
}

/// <summary>
/// Codex wake adapter supporting live steering via Codex App-Server (<c>turn/steer</c> / <c>turn/start</c>)
/// with automatic fallback to <c>codex.exe queue</c> when <c>MOBADEV_CODEX_APP_SERVER_URL</c> is unset.
/// </summary>
public sealed class CodexWakeDispatcher
{
    public const string AppServerUrlEnvVar = "MOBADEV_CODEX_APP_SERVER_URL";
    public const string CodexExeEnvVar = "MOBADEV_CODEX_EXE";
    public const string CodexThreadIdEnvVar = "MOBADEV_CODEX_THREAD_ID";

    private readonly CodexWakeDispatcherOptions _options;
    private readonly ConcurrentDictionary<string, string> _activeTurnIds = new(StringComparer.OrdinalIgnoreCase);

    public CodexWakeDispatcher(CodexWakeDispatcherOptions? options = null)
    {
        _options = options ?? new CodexWakeDispatcherOptions();
    }

    /// <summary>
    /// Creates a <see cref="SwarmBusStoreOptions.WakeDispatcher"/>-compatible delegate.
    /// </summary>
    public Func<SwarmWakeContext, CancellationToken, Task<SwarmWakeResult>> CreateWakeDelegate() =>
        async (context, cancellationToken) =>
        {
            var outcome = await DispatchAsync(context, cancellationToken).ConfigureAwait(false);
            var attempted = !string.Equals(outcome.Reason, "codex_exe_not_found", StringComparison.Ordinal) &&
                            !string.Equals(outcome.Reason, "no_thread_id", StringComparison.Ordinal);
            return new SwarmWakeResult(
                Attempted: attempted,
                Ok: outcome.Ok,
                Target: context.Recipient,
                ThreadId: outcome.ThreadId,
                Reason: outcome.Ok ? outcome.DeliveryMode : outcome.Reason ?? outcome.DeliveryMode,
                Error: outcome.Error);
        };

    /// <summary>
    /// Seeds or clears the tracked active turn ID for <paramref name="threadId"/>.
    /// </summary>
    public void SetActiveTurnId(string threadId, string? turnId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var normalizedThread = threadId.Trim();
        if (string.IsNullOrWhiteSpace(turnId))
        {
            _activeTurnIds.TryRemove(normalizedThread, out _);
        }
        else
        {
            _activeTurnIds[normalizedThread] = turnId.Trim();
        }
    }

    /// <summary>
    /// Returns the currently tracked active turn ID for <paramref name="threadId"/>, if any.
    /// </summary>
    public string? GetActiveTurnId(string threadId) =>
        !string.IsNullOrWhiteSpace(threadId) && _activeTurnIds.TryGetValue(threadId.Trim(), out var turnId)
            ? turnId
            : null;

    /// <summary>
    /// Dispatches a wake message to Codex via App-Server live steering or <c>codex.exe queue</c> fallback.
    /// </summary>
    public async Task<CodexWakeDeliveryOutcome> DispatchAsync(SwarmWakeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var threadId = ResolveThreadId(context.CodexThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "none",
                ThreadId: string.Empty,
                Reason: "no_thread_id",
                Error: "No Codex thread ID configured.");
        }

        var rawAppServerUrl = ResolveAppServerUrl();

        if (!string.IsNullOrWhiteSpace(rawAppServerUrl) || _options.SessionFactory is not null)
        {
            return await DispatchViaAppServerAsync(rawAppServerUrl, threadId, context, cancellationToken).ConfigureAwait(false);
        }

        return await DispatchViaCliQueueAsync(threadId, context.FormattedMessage, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CodexWakeDeliveryOutcome> DispatchViaAppServerAsync(
        string? rawAppServerUrl,
        string threadId,
        SwarmWakeContext context,
        CancellationToken cancellationToken)
    {
        Uri serverUri;
        if (!string.IsNullOrWhiteSpace(rawAppServerUrl))
        {
            if (!Uri.TryCreate(rawAppServerUrl, UriKind.Absolute, out var parsed))
            {
                return new CodexWakeDeliveryOutcome(
                    Ok: false,
                    DeliveryMode: "start",
                    ThreadId: threadId,
                    Reason: "invalid_app_server_url",
                    Error: $"Invalid {AppServerUrlEnvVar} URI: '{rawAppServerUrl}'.");
            }

            serverUri = parsed;
        }
        else
        {
            serverUri = new Uri("ws://127.0.0.1:0");
        }

        var factory = _options.SessionFactory ?? (static (uri, ct) => ConnectDefaultSessionAsync(uri, ct));
        var clientUserMessageId = $"swarmbus-{context.MessageId}";

        try
        {
            await using var session = await factory(serverUri, cancellationToken).ConfigureAwait(false);
            await session.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var resumedTurnId = await session.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            var activeTurnId = !string.IsNullOrWhiteSpace(resumedTurnId)
                ? resumedTurnId.Trim()
                : GetActiveTurnId(threadId);

            if (!string.IsNullOrWhiteSpace(activeTurnId))
            {
                _activeTurnIds[threadId] = activeTurnId;
                try
                {
                    var steeredTurnId = await session.SteerTurnAsync(
                        threadId,
                        activeTurnId,
                        context.FormattedMessage,
                        clientUserMessageId,
                        cancellationToken).ConfigureAwait(false);

                    var effectiveTurnId = string.IsNullOrWhiteSpace(steeredTurnId) ? activeTurnId : steeredTurnId.Trim();
                    _activeTurnIds[threadId] = effectiveTurnId;
                    return new CodexWakeDeliveryOutcome(
                        Ok: true,
                        DeliveryMode: "steer",
                        ThreadId: threadId,
                        TurnId: effectiveTurnId);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTurnStateMismatchException(ex))
                {
                    // Active turn completed concurrently or does not exist; clear tracked turn and fall back to turn/start.
                    _activeTurnIds.TryRemove(threadId, out _);
                }
            }

            var startedTurnId = await session.StartTurnAsync(
                threadId,
                context.FormattedMessage,
                clientUserMessageId,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(startedTurnId))
            {
                _activeTurnIds[threadId] = startedTurnId.Trim();
            }

            return new CodexWakeDeliveryOutcome(
                Ok: true,
                DeliveryMode: "start",
                ThreadId: threadId,
                TurnId: string.IsNullOrWhiteSpace(startedTurnId) ? null : startedTurnId.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "start",
                ThreadId: threadId,
                Reason: "app_server_error",
                Error: ex.Message);
        }
    }

    private async Task<CodexWakeDeliveryOutcome> DispatchViaCliQueueAsync(
        string threadId,
        string formattedMessage,
        CancellationToken cancellationToken)
    {
        var codexExe = ResolveCodexExecutablePath();
        if (!File.Exists(codexExe))
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "cli-queue",
                ThreadId: threadId,
                Reason: "codex_exe_not_found",
                Error: $"Codex executable not found: {codexExe}");
        }

        var timeout = _options.CliTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(6) : _options.CliTimeout;
        var request = new CodexCliRequest(codexExe, threadId, formattedMessage, timeout);
        var runner = _options.CliRunner ?? RunProcessCliAsync;

        try
        {
            var result = await runner(request, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return new CodexWakeDeliveryOutcome(
                    Ok: true,
                    DeliveryMode: "cli-queue",
                    ThreadId: threadId);
            }

            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"codex queue exited with code {result.ExitCode}."
                : result.StandardError.Trim();
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "cli-queue",
                ThreadId: threadId,
                Reason: "cli_exit_nonzero",
                Error: detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "cli-queue",
                ThreadId: threadId,
                Reason: "timeout",
                Error: ex.Message);
        }
        catch (InvalidOperationException ex) when (!IsAllowedCliExecutable(codexExe))
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "cli-queue",
                ThreadId: threadId,
                Reason: "unsafe_executable",
                Error: ex.Message);
        }
        catch (Exception ex)
        {
            return new CodexWakeDeliveryOutcome(
                Ok: false,
                DeliveryMode: "cli-queue",
                ThreadId: threadId,
                Reason: "cli_error",
                Error: ex.Message);
        }
    }

    private static async Task<ICodexAppServerSession> ConnectDefaultSessionAsync(Uri uri, CancellationToken cancellationToken) =>
        await WebSocketCodexAppServerSession.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

    private static bool IsAllowedCliExecutable(string executablePath)
    {
        var ext = Path.GetExtension(executablePath);
        if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".mjs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !OperatingSystem.IsWindows() && string.IsNullOrEmpty(ext);
    }

    private static bool IsTurnStateMismatchException(Exception ex)
    {
        if (ex is OperationCanceledException or WebSocketException or IOException)
        {
            return false;
        }

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        const string steerPrefix = "Codex App-Server 'turn/steer' failed:";
        if (message.StartsWith(steerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            message = message[steerPrefix.Length..];
        }

        return message.Contains("turn", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("active", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("expectedTurnId", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("steerable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CodexCliResult> RunProcessCliAsync(CodexCliRequest request, CancellationToken cancellationToken)
    {
        if (!IsAllowedCliExecutable(request.ExecutablePath))
        {
            var ext = Path.GetExtension(request.ExecutablePath);
            throw new InvalidOperationException(
                $"Refusing unsafe Codex CLI executable extension '{ext}' for '{request.ExecutablePath}'. Only .exe{(OperatingSystem.IsWindows() ? string.Empty : ", extensionless binaries,")} or .mjs scripts are allowed.");
        }

        var isNodeScript = string.Equals(Path.GetExtension(request.ExecutablePath), ".mjs", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isNodeScript ? "node" : request.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (isNodeScript)
        {
            startInfo.ArgumentList.Add(request.ExecutablePath);
        }

        startInfo.ArgumentList.Add("queue");
        startInfo.ArgumentList.Add("--thread");
        startInfo.ArgumentList.Add(request.ThreadId);
        startInfo.ArgumentList.Add("--message");
        startInfo.ArgumentList.Add(request.Message);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start Codex CLI process '{startInfo.FileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Codex CLI queue timed out after {request.Timeout.TotalSeconds:0.##}s.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new CodexCliResult(process.ExitCode, stdout, stderr);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort process cleanup.
        }
    }

    private string? ResolveAppServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.AppServerUrl))
        {
            return _options.AppServerUrl.Trim();
        }

        var env = Environment.GetEnvironmentVariable(AppServerUrlEnvVar);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    private string ResolveCodexExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CodexExecutablePath))
        {
            return _options.CodexExecutablePath.Trim();
        }

        var env = Environment.GetEnvironmentVariable(CodexExeEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            ".sandbox-bin",
            "codex.exe");
    }

    private string? ResolveThreadId(string? overrideThreadId)
    {
        if (!string.IsNullOrWhiteSpace(overrideThreadId))
        {
            return overrideThreadId.Trim();
        }

        var env = Environment.GetEnvironmentVariable(CodexThreadIdEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return string.IsNullOrWhiteSpace(_options.DefaultThreadId)
            ? null
            : _options.DefaultThreadId.Trim();
    }
}
