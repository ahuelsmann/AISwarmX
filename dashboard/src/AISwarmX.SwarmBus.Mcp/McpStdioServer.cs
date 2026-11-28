using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AISwarmX.SwarmBus.Core.Store;

namespace AISwarmX.SwarmBus.Mcp;

/// <summary>
/// Line-delimited stdio JSON-RPC 2.0 MCP server.
/// Strictly reserves <c>stdout</c> for single-line JSON-RPC 2.0 responses; diagnostics go to <c>stderr</c>.
/// </summary>
internal static class McpStdioServer
{
    public const string ServerName = "aiswarmx-swarm-bus-mcp";
    public const string ServerVersion = "1.0.0";
    public const string DefaultProtocolVersion = "2024-11-05";
    private const int MaxConcurrentFastRequests = 12;
    private const int MaxConcurrentWaitRequests = 4;
    private static readonly string[] SupportedProtocolVersions = [DefaultProtocolVersion];

    public static async Task<int> RunAsync(
        SwarmBusStore store,
        TextReader input,
        TextWriter output,
        TextWriter errorOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errorOutput);

        var toolsNode = SwarmBusToolCatalog.BuildToolDefinitions();
        using var outputLock = new SemaphoreSlim(1, 1);
        using var errorLock = new SemaphoreSlim(1, 1);
        using var fastConcurrency = new SemaphoreSlim(MaxConcurrentFastRequests, MaxConcurrentFastRequests);
        using var waitConcurrency = new SemaphoreSlim(MaxConcurrentWaitRequests, MaxConcurrentWaitRequests);
        using var waitShutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var inFlightRequests = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var fastTasks = new List<Task>();
        var waitTasks = new List<Task>();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                await WriteResponseAsync(
                    output,
                    outputLock,
                    BuildErrorResponse(null, -32700, "Parse error"),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    await WriteResponseAsync(
                        output,
                        outputLock,
                        BuildErrorResponse(null, -32600, "Invalid Request"),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var hasId = root.TryGetProperty("id", out var idElem);
                var idIsValid = !hasId || idElem.ValueKind is JsonValueKind.String or JsonValueKind.Number;
                JsonNode? validIdNode = hasId && idIsValid ? JsonNode.Parse(idElem.GetRawText()) : null;

                var hasValidJsonRpc = root.TryGetProperty("jsonrpc", out var jsonRpcElem) &&
                                      jsonRpcElem.ValueKind == JsonValueKind.String &&
                                      string.Equals(jsonRpcElem.GetString(), "2.0", StringComparison.Ordinal);
                if (!hasValidJsonRpc)
                {
                    await WriteResponseAsync(
                        output,
                        outputLock,
                        BuildErrorResponse(validIdNode, -32600, "Invalid Request: 'jsonrpc' must be \"2.0\"."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!idIsValid)
                {
                    await WriteResponseAsync(
                        output,
                        outputLock,
                        BuildErrorResponse(null, -32600, "Invalid Request: 'id' must be a string or number."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var method = root.TryGetProperty("method", out var methodElem) && methodElem.ValueKind == JsonValueKind.String
                    ? methodElem.GetString()
                    : null;

                if (!hasId)
                {
                    if (string.IsNullOrWhiteSpace(method))
                    {
                        await WriteResponseAsync(
                            output,
                            outputLock,
                            BuildErrorResponse(null, -32600, "Invalid Request: missing or invalid 'method'."),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(method, "notifications/cancelled", StringComparison.Ordinal))
                    {
                        TryCancelInFlightRequest(root, inFlightRequests);
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(method))
                {
                    await WriteResponseAsync(
                        output,
                        outputLock,
                        BuildErrorResponse(validIdNode, -32600, "Invalid Request: missing or invalid 'method'."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var paramsClone = root.TryGetProperty("params", out var paramsElem) ? paramsElem.Clone() : default;
                var isWaitToolCall = string.Equals(method, "tools/call", StringComparison.Ordinal) &&
                                     paramsClone.ValueKind == JsonValueKind.Object &&
                                     paramsClone.TryGetProperty("name", out var toolNameElem) &&
                                     toolNameElem.ValueKind == JsonValueKind.String &&
                                     string.Equals(toolNameElem.GetString(), "bus_wait", StringComparison.Ordinal);

                var pool = isWaitToolCall ? waitConcurrency : fastConcurrency;
                if (!pool.Wait(0))
                {
                    var poolLabel = isWaitToolCall ? "concurrent bus_wait" : "concurrent";
                    var limit = isWaitToolCall ? MaxConcurrentWaitRequests : MaxConcurrentFastRequests;
                    await WriteResponseAsync(
                        output,
                        outputLock,
                        BuildErrorResponse(validIdNode, -32001, $"Server busy: too many {poolLabel} requests (max {limit})."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var requestKey = TryGetCanonicalRequestIdKey(idElem, out var key) ? key : null;
                var requestResponseToken = isWaitToolCall ? waitShutdownCts.Token : cancellationToken;
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestResponseToken);
                if (requestKey is not null)
                {
                    inFlightRequests[requestKey] = linkedCts;
                }

                fastTasks.RemoveAll(static t => t.IsCompleted);
                waitTasks.RemoveAll(static t => t.IsCompleted);
                var taskList = isWaitToolCall ? waitTasks : fastTasks;
                var capturedMethod = method;
                var capturedRequestKey = requestKey;
                var capturedIdNode = validIdNode;
                var capturedPool = pool;
                var capturedLinkedCts = linkedCts;
                var capturedResponseToken = requestResponseToken;
                var task = Task.Run(
                    () => DispatchRequestAsync(
                        store,
                        output,
                        outputLock,
                        errorOutput,
                        errorLock,
                        capturedPool,
                        inFlightRequests,
                        capturedRequestKey,
                        capturedIdNode,
                        capturedMethod,
                        paramsClone,
                        toolsNode,
                        capturedLinkedCts,
                        capturedResponseToken),
                    CancellationToken.None);
                taskList.Add(task);
            }
        }

        // Let already-queued fast RPCs (one-shot pipes closing stdin right after send) finish writing their replies
        // before cancelling remaining long-poll waits so server shutdown stays prompt without truncating one-shot calls.
        if (fastTasks.Count > 0)
        {
            await Task.WhenAll(fastTasks).ConfigureAwait(false);
        }

        waitShutdownCts.Cancel();
        if (waitTasks.Count > 0)
        {
            await Task.WhenAll(waitTasks).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task DispatchRequestAsync(
        SwarmBusStore store,
        TextWriter output,
        SemaphoreSlim outputLock,
        TextWriter errorOutput,
        SemaphoreSlim errorLock,
        SemaphoreSlim slotPool,
        ConcurrentDictionary<string, CancellationTokenSource> inFlightRequests,
        string? requestKey,
        JsonNode? idNode,
        string method,
        JsonElement @params,
        JsonArray toolsNode,
        CancellationTokenSource linkedCts,
        CancellationToken serverCancellationToken)
    {
        try
        {
            var response = await HandleRequestAsync(store, idNode, method, @params, toolsNode, linkedCts.Token)
                .ConfigureAwait(false);
            try
            {
                await WriteResponseAsync(output, outputLock, response, serverCancellationToken).ConfigureAwait(false);
            }
            catch (Exception writeEx) when (writeEx is IOException or ObjectDisposedException)
            {
                // Broken stdout pipe when client closes early; log best-effort and exit cleanly without faulting shutdown.
                await SafeWriteErrorLogAsync(errorOutput, errorLock, $"[aiswarmx-swarm-bus-mcp] {method} stdout closed: {writeEx.Message}").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested || serverCancellationToken.IsCancellationRequested)
        {
            // Request cancelled via notifications/cancelled or server shutdown; per MCP spec, emit no response.
        }
        catch (Exception ex)
        {
            await SafeWriteErrorLogAsync(errorOutput, errorLock, $"[aiswarmx-swarm-bus-mcp] {method} error: {ex}").ConfigureAwait(false);
            JsonObject errorEnvelope;
            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                var errorPayload = JsonSerializer.Serialize(
                    new { ok = false, error = ex.Message },
                    SwarmBusToolDispatcher.CompactJsonOptions);
                errorEnvelope = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = errorPayload
                            }
                        },
                        ["isError"] = true
                    }
                };
            }
            else
            {
                errorEnvelope = BuildErrorResponse(idNode, -32603, $"Internal error: {ex.Message}");
            }

            try
            {
                await WriteResponseAsync(output, outputLock, errorEnvelope, serverCancellationToken).ConfigureAwait(false);
            }
            catch (Exception writeEx) when (writeEx is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Server shutting down or stdout already closed by peer.
            }
        }
        finally
        {
            if (requestKey is not null)
            {
                inFlightRequests.TryRemove(new KeyValuePair<string, CancellationTokenSource>(requestKey, linkedCts));
            }

            slotPool.Release();
            linkedCts.Dispose();
        }
    }

    private static void TryCancelInFlightRequest(
        JsonElement notificationRoot,
        ConcurrentDictionary<string, CancellationTokenSource> inFlightRequests)
    {
        if (!notificationRoot.TryGetProperty("params", out var paramsElem) ||
            paramsElem.ValueKind != JsonValueKind.Object ||
            !paramsElem.TryGetProperty("requestId", out var requestIdElem) ||
            !TryGetCanonicalRequestIdKey(requestIdElem, out var cancelKey))
        {
            return;
        }

        if (inFlightRequests.TryGetValue(cancelKey, out var targetCts))
        {
            try
            {
                targetCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Request already completed and disposed its CTS.
            }
        }
    }

    private static bool TryGetCanonicalRequestIdKey(JsonElement idElem, out string key)
    {
        switch (idElem.ValueKind)
        {
            case JsonValueKind.String:
                key = "s:" + idElem.GetString();
                return true;
            case JsonValueKind.Number:
                key = idElem.TryGetInt64(out var intId) ? $"n:{intId}" : $"n:{idElem.GetRawText()}";
                return true;
            default:
                key = string.Empty;
                return false;
        }
    }

    private static async Task<JsonObject> HandleRequestAsync(
        SwarmBusStore store,
        JsonNode? idNode,
        string method,
        JsonElement @params,
        JsonArray toolsNode,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
            {
                var requestedVersion = @params.ValueKind == JsonValueKind.Object &&
                                       @params.TryGetProperty("protocolVersion", out var verElem) &&
                                       verElem.ValueKind == JsonValueKind.String
                    ? verElem.GetString()?.Trim()
                    : null;

                var negotiatedVersion = SupportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal)
                    ? requestedVersion!
                    : DefaultProtocolVersion;

                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject
                    {
                        ["protocolVersion"] = negotiatedVersion,
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject
                            {
                                ["listChanged"] = false
                            }
                        },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = ServerName,
                            ["version"] = ServerVersion
                        }
                    }
                };
            }

            case "ping":
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject()
                };

            case "tools/list":
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject
                    {
                        ["tools"] = JsonNode.Parse(toolsNode.ToJsonString())
                    }
                };

            case "tools/call":
            {
                if (@params.ValueKind != JsonValueKind.Object ||
                    !@params.TryGetProperty("name", out var nameElem) ||
                    nameElem.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(nameElem.GetString()))
                {
                    return BuildErrorResponse(idNode, -32602, "tools/call requires non-empty 'params.name'.");
                }

                var toolName = nameElem.GetString()!;
                if (!SwarmBusToolDispatcher.IsKnownTool(toolName))
                {
                    return BuildErrorResponse(idNode, -32602, $"Invalid params: unknown tool '{toolName}'.");
                }

                var toolArgs = @params.TryGetProperty("arguments", out var argsElem) && argsElem.ValueKind == JsonValueKind.Object
                    ? argsElem
                    : default;

                var (payload, isError) = await SwarmBusToolDispatcher
                    .ExecuteAsync(store, toolName, toolArgs, cancellationToken)
                    .ConfigureAwait(false);

                var text = JsonSerializer.Serialize(payload, SwarmBusToolDispatcher.IndentedJsonOptions);
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = text
                            }
                        },
                        ["isError"] = isError
                    }
                };
            }

            default:
                return BuildErrorResponse(idNode, -32601, $"Method not found: {method}");
        }
    }

    private static JsonObject BuildErrorResponse(JsonNode? idNode, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = idNode,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };

    private static async Task WriteResponseAsync(
        TextWriter output,
        SemaphoreSlim outputLock,
        JsonObject response,
        CancellationToken cancellationToken)
    {
        var singleLine = response.ToJsonString(SwarmBusToolDispatcher.CompactJsonOptions);
        await outputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteLineAsync(singleLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            outputLock.Release();
        }
    }

    private static async Task SafeWriteErrorLogAsync(TextWriter errorOutput, SemaphoreSlim errorLock, string message)
    {
        try
        {
            await errorLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await errorOutput.WriteLineAsync(message).ConfigureAwait(false);
                await errorOutput.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                errorLock.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Ignore broken stderr pipe on shutdown.
        }
    }
}
