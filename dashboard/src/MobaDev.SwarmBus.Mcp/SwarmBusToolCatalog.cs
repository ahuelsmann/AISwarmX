using System.Text.Json.Nodes;

namespace MobaDev.SwarmBus.Mcp;

/// <summary>
/// MCP tool definitions and JSON schemas exposed by <c>mobadev-swarm-bus-mcp</c>.
/// </summary>
internal static class SwarmBusToolCatalog
{
    public static JsonArray BuildToolDefinitions() =>
    [
        Tool(
            "bus_send",
            "Send a cross-tool message via MOBAi Swarm Bus to another standing agent or @all (Claude, Codex, Antigravity, Watchdog).",
            new JsonObject
            {
                ["from"] = StringProp("Sender identifier, e.g. claude-opus, codex, antigravity-gemini, antigravity-opus."),
                ["to"] = StringProp("Recipient identifier (claude-opus, codex, antigravity-gemini, antigravity-opus, or all)."),
                ["topic"] = StringProp("Short topic tag, e.g. design-go, review-pass, sonar-finding, lock-handoff."),
                ["body"] = StringProp("Concise message body with exact SHA/files/decision."),
                ["pr"] = IntProp("Optional PR number."),
                ["issue"] = IntProp("Optional GitHub issue number."),
                ["priority"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("low", "normal", "high", "urgent"),
                    ["default"] = "normal"
                },
                ["reply_to"] = IntProp("Optional parent bus message id."),
                ["state"] = StringProp("Optional deduplication state key."),
                ["wake"] = BoolProp("If true and recipient includes codex or all, triggers live steering / wake dispatch.", false),
                ["codex_thread_id"] = StringProp("Optional override Codex thread ID when wake is true."),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default').")
            },
            ["from", "to", "topic", "body"]),

        Tool(
            "bus_inbox",
            "Read unread (or recent) cross-tool messages addressed to the specified agent or all.",
            new JsonObject
            {
                ["agent"] = StringProp("Recipient agent id, e.g. claude-opus, codex, antigravity-gemini."),
                ["unread_only"] = BoolProp("When true, returns only unread messages.", true),
                ["limit"] = IntProp("Maximum messages to return (1..100).", 20),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default', or '*' for all).")
            },
            ["agent"]),

        Tool(
            "bus_ack",
            "Acknowledge a Swarm Bus message as processed and optionally send a threaded reply to the sender.",
            new JsonObject
            {
                ["message_id"] = IntProp("ID of the message to acknowledge."),
                ["agent"] = StringProp("Agent acknowledging the message."),
                ["reply"] = StringProp("Optional reply body sent back to original sender.")
            },
            ["message_id", "agent"]),

        Tool(
            "bus_locks",
            "Claim, release, or list active cross-tool path locks to prevent overlapping edits across Claude, Codex, and Antigravity.",
            new JsonObject
            {
                ["action"] = EnumProp(["list", "claim", "release"], "list"),
                ["agent"] = StringProp("Owning agent identifier."),
                ["paths"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Repo-relative locked area paths/prefixes."
                },
                ["pr"] = IntProp("Optional associated PR number."),
                ["ttl_minutes"] = IntProp("Lease duration in minutes (5..1440).", 120),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default').")
            },
            ["action"]),

        Tool(
            "bus_review_claim",
            "Manage single-holder review leases per (workspace_id, pr_number, head_sha, role) with expiration, completion, release, and listing.",
            new JsonObject
            {
                ["action"] = EnumProp(["claim", "complete", "release", "list"], "claim"),
                ["agent"] = StringProp("Reviewing agent identifier."),
                ["pr_number"] = IntProp("Pull request number."),
                ["head_sha"] = StringProp("Exact 40-char hex PR head commit SHA."),
                ["role"] = EnumProp(["companion", "final", "p3-verification"], null),
                ["ttl_minutes"] = IntProp("Lease duration in minutes for claim (1..1440).", 60),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default').")
            },
            ["action"]),

        Tool(
            "bus_queue",
            "Coordinate Ready work queue claims, lease renewals/resumes, releases, guarded completions, Watchdog sync, and metadata.",
            new JsonObject
            {
                ["action"] = EnumProp(["list", "next", "resume", "release", "done", "upsert", "sync-meta"], "list"),
                ["agent"] = StringProp("Claiming, resuming, or releasing agent identifier."),
                ["owner"] = StringProp("Owning agent identifier (for resume; falls back to agent)."),
                ["issue_number"] = IntProp("GitHub issue number."),
                ["title"] = StringProp("Issue title (for upsert)."),
                ["priority"] = IntProp("Numeric priority (0=P0 .. 3=P3)."),
                ["risk_class"] = StringProp("Risk class (H, M, or L)."),
                ["status"] = EnumProp(["queued", "claimed", "done", "removed"], null),
                ["expected_status"] = EnumProp(["queued", "claimed", "done", "removed"], null),
                ["expected_owner"] = StringProp("Atomic guard for done: expected claimed_by owner."),
                ["claimed_before_utc"] = StringProp("Atomic guard for done: ISO-8601 UTC upper bound for claimed_at_utc."),
                ["meta_key"] = StringProp("Metadata key (for sync-meta), e.g. code_wip."),
                ["meta_value"] = StringProp("Metadata value (for sync-meta)."),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default').")
            },
            ["action"]),

        Tool(
            "bus_status",
            "Inspect live Swarm Bus summary: unread counts, active cross-tool path locks, and recent messages.",
            new JsonObject
            {
                ["limit"] = IntProp("Maximum recent messages to include (1..50).", 15),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default', or '*' for all).")
            },
            []),

        Tool(
            "bus_wait",
            "Zero-token blocking wait for unread Swarm Bus messages newer than since_id using FileSystemWatcher with 250ms fallback polling.",
            new JsonObject
            {
                ["agent"] = StringProp("Recipient agent id waiting for messages."),
                ["since_id"] = IntProp("Return unread messages with id > since_id.", 0),
                ["mentions_only"] = BoolProp("When true, restricts to exact recipient + 'all' even for wildcard queries.", false),
                ["timeout_ms"] = IntProp("Maximum wait duration in milliseconds (1..120000).", 30_000),
                ["limit"] = IntProp("Maximum unread messages to return (1..100).", 20),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default', or '*' for all).")
            },
            ["agent"]),

        Tool(
            "bus_start_work",
            "Single-roundtrip startup: optionally claim path locks, fetch unread inbox messages, and inspect active foreign locks.",
            new JsonObject
            {
                ["agent"] = StringProp("Agent starting work."),
                ["paths"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Optional repo-relative paths/patterns to claim atomically."
                },
                ["pr"] = IntProp("Optional associated PR number."),
                ["ttl_minutes"] = IntProp("Lock lease duration in minutes (5..1440).", 120),
                ["message_limit"] = IntProp("Maximum unread inbox messages to return (1..100).", 20),
                ["workspace_id"] = StringProp("Optional workspace scope (defaults to 'default').")
            },
            ["agent"])
    ];

    private static JsonObject Tool(string name, string description, JsonObject properties, string[] required)
    {
        var reqArray = new JsonArray();
        foreach (var item in required)
        {
            reqArray.Add(item);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = reqArray
            }
        };
    }

    private static JsonObject StringProp(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject IntProp(string description, int? defaultValue = null)
    {
        var obj = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description
        };
        if (defaultValue.HasValue)
        {
            obj["default"] = defaultValue.Value;
        }

        return obj;
    }

    private static JsonObject BoolProp(string description, bool defaultValue) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
        ["default"] = defaultValue
    };

    private static JsonObject EnumProp(string[] values, string? defaultValue)
    {
        var arr = new JsonArray();
        foreach (var v in values)
        {
            arr.Add(v);
        }

        var obj = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = arr
        };
        if (defaultValue is not null)
        {
            obj["default"] = defaultValue;
        }

        return obj;
    }
}
