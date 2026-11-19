using System.Text.Json;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Queries open and recently merged MOBAflow PRs (and optional GitHub Project Kanban items) strictly read-only
/// via gh CLI, retaining a last-known cache fallback if GitHub is temporarily unreachable.
/// </summary>
public sealed partial class PipelineProvider
{
    private readonly ICommandRunner _commandRunner;
    private readonly object _fallbackLock = new();
    private PipelineResponse? _lastSuccessfulResponse;

    public PipelineProvider(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PipelineResponse> GetPipelineAsync(DashboardOptions options, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var openPrTask = _commandRunner.RunAsync(
            "gh",
            [
                "pr", "list",
                "--repo", options.GitHubRepository,
                "--state", "open",
                "--json", "number,title,url,headRefName,isDraft,mergeStateStatus,autoMergeRequest,updatedAt,body,statusCheckRollup,comments,closingIssuesReferences"
            ],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(12),
            cancellationToken);

        var mergedPrTask = _commandRunner.RunAsync(
            "gh",
            [
                "pr", "list",
                "--repo", options.GitHubRepository,
                "--state", "merged",
                "--limit", "5",
                "--json", "number,title,url,headRefName,mergedAt,updatedAt,body,comments,closingIssuesReferences"
            ],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(12),
            cancellationToken);

        var projectTask = TryLoadProjectStatusesAsync(options, cancellationToken);
        var issuesTask = TryLoadIssuesAsync(options, cancellationToken);

        await Task.WhenAll(openPrTask, mergedPrTask, projectTask, issuesTask).ConfigureAwait(false);

        var openResult = openPrTask.Result;
        if (!openResult.Succeeded)
        {
            return BuildUnavailableOrCached(options.GitHubRepository, now, openResult);
        }

        var (projectAvailable, projectNotice, projectStatuses) = projectTask.Result;
        var issuesByNumber = issuesTask.Result;

        try
        {
            var mergedResult = mergedPrTask.Result;
            var mergedJson = mergedResult.Succeeded ? mergedResult.StdOut : null;
            var knownPrNumbers = CollectPrNumbers(openResult.StdOut, mergedJson);

            var openPullRequests = ParsePullRequests(
                openResult.StdOut,
                isMergedList: false,
                options.GitHubRepository,
                projectStatuses,
                issuesByNumber,
                knownPrNumbers);

            var recentlyMerged = mergedResult.Succeeded
                ? ParseMergedSafe(mergedResult.StdOut, options.GitHubRepository, issuesByNumber, knownPrNumbers)
                : [];

            var response = new PipelineResponse(
                GeneratedAtUtc: now,
                Repository: options.GitHubRepository,
                PullRequests: openPullRequests,
                RecentlyMerged: recentlyMerged,
                ProjectBoardAvailable: projectAvailable,
                ProjectBoardNotice: projectNotice,
                Source: new SourceHealth("ok", now));

            lock (_fallbackLock)
            {
                _lastSuccessfulResponse = response;
            }

            return response;
        }
        catch (JsonException ex)
        {
            return BuildUnavailableOrCached(
                options.GitHubRepository,
                now,
                new CommandResult(-1, string.Empty, ex.Message));
        }
    }

    private PipelineResponse BuildUnavailableOrCached(
        string repository,
        DateTimeOffset now,
        CommandResult failedResult)
    {
        PipelineResponse? cached;
        lock (_fallbackLock)
        {
            cached = _lastSuccessfulResponse;
        }

        var reason = failedResult.NotFound
            ? "GitHub CLI (gh) is not installed."
            : failedResult.TimedOut
                ? "GitHub query timed out."
                : "GitHub is unreachable.";

        if (cached is not null)
        {
            return cached with
            {
                GeneratedAtUtc = now,
                Source = new SourceHealth(
                    "degraded",
                    cached.Source.UpdatedAtUtc,
                    $"{reason} Showing last cached snapshot.",
                    FromCache: true)
            };
        }

        return new PipelineResponse(
            GeneratedAtUtc: now,
            Repository: repository,
            PullRequests: [],
            RecentlyMerged: [],
            ProjectBoardAvailable: false,
            ProjectBoardNotice: null,
            Source: new SourceHealth("unavailable", now, reason));
    }

    private async Task<(bool Available, string? Notice, Dictionary<int, string> Statuses)> TryLoadProjectStatusesAsync(
        DashboardOptions options,
        CancellationToken cancellationToken)
    {
        var statuses = new Dictionary<int, string>();
        if (options.GitHubProjectNumber <= 0 || string.IsNullOrWhiteSpace(options.GitHubProjectOwner))
        {
            return (false, "No GitHub project configured.", statuses);
        }

        var projectResult = await _commandRunner.RunAsync(
            "gh",
            [
                "project", "item-list",
                options.GitHubProjectNumber.ToString(),
                "--owner", options.GitHubProjectOwner,
                "--limit", "40",
                "--format", "json"
            ],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        if (!projectResult.Succeeded)
        {
            return (
                false,
                "Project board data hidden: missing read:project scope or Kanban CLI unreachable.",
                statuses);
        }

        try
        {
            using var doc = JsonDocument.Parse(projectResult.StdOut);
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) ||
                        content.ValueKind != JsonValueKind.Object ||
                        !content.TryGetProperty("number", out var numberProp) ||
                        !numberProp.TryGetInt32(out var prNumber))
                    {
                        continue;
                    }

                    if (item.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
                    {
                        var status = statusProp.GetString();
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            statuses[prNumber] = status;
                        }
                    }
                }
            }

            return (true, null, statuses);
        }
        catch (JsonException)
        {
            return (false, "Unable to parse GitHub project data.", statuses);
        }
    }

    private async Task<Dictionary<int, LinkedIssueInfo>> TryLoadIssuesAsync(
        DashboardOptions options,
        CancellationToken cancellationToken)
    {
        var issues = new Dictionary<int, LinkedIssueInfo>();
        if (string.IsNullOrWhiteSpace(options.GitHubRepository))
        {
            return issues;
        }

        var issueResult = await _commandRunner.RunAsync(
            "gh",
            [
                "issue", "list",
                "--repo", options.GitHubRepository,
                "--state", "all",
                "--limit", "100",
                "--json", "number,title,url,state"
            ],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        if (!issueResult.Succeeded || string.IsNullOrWhiteSpace(issueResult.StdOut))
        {
            return issues;
        }

        try
        {
            using var doc = JsonDocument.Parse(issueResult.StdOut);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return issues;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("number", out var numberProp) ||
                    !numberProp.TryGetInt32(out var issueNumber))
                {
                    continue;
                }

                var title = GetString(item, "title");
                var url = GetString(item, "url") ?? $"https://github.com/{options.GitHubRepository}/issues/{issueNumber}";
                var state = GetString(item, "state");

                issues[issueNumber] = new LinkedIssueInfo(
                    Number: issueNumber,
                    Title: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    Url: url,
                    State: string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant());
            }
        }
        catch (JsonException)
        {
            // Keep graceful empty map on malformed CLI output.
        }

        return issues;
    }

    private static HashSet<int> CollectPrNumbers(string openJson, string? mergedJson)
    {
        var set = new HashSet<int>();
        AddPrNumbersFromJson(openJson, set);
        if (!string.IsNullOrWhiteSpace(mergedJson))
        {
            AddPrNumbersFromJson(mergedJson, set);
        }

        return set;
    }

    private static void AddPrNumbersFromJson(string json, HashSet<int> target)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object &&
                    el.TryGetProperty("number", out var prop) &&
                    prop.TryGetInt32(out var num))
                {
                    target.Add(num);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore; caller handles JSON errors when building pull requests.
        }
    }

    private static List<PullRequestInfo> ParseMergedSafe(
        string json,
        string repository,
        IReadOnlyDictionary<int, LinkedIssueInfo> issuesByNumber,
        IReadOnlySet<int> knownPrNumbers)
    {
        try
        {
            var merged = ParsePullRequests(
                json,
                isMergedList: true,
                repository,
                projectStatuses: new Dictionary<int, string>(),
                issuesByNumber,
                knownPrNumbers);
            return merged
                .OrderByDescending(p => p.MergedAtUtc ?? p.UpdatedAtUtc)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<PullRequestInfo> ParsePullRequests(
        string json,
        bool isMergedList,
        string repository,
        IReadOnlyDictionary<int, string> projectStatuses,
        IReadOnlyDictionary<int, LinkedIssueInfo> issuesByNumber,
        IReadOnlySet<int> knownPrNumbers)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<PullRequestInfo>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var number = el.TryGetProperty("number", out var n) && n.TryGetInt32(out var num) ? num : 0;
            var title = GetString(el, "title") ?? string.Empty;
            var url = GetString(el, "url") ?? string.Empty;
            var branch = GetString(el, "headRefName") ?? string.Empty;
            var isDraft = !isMergedList && el.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True;
            var autoMergeEnabled = !isMergedList &&
                                   el.TryGetProperty("autoMergeRequest", out var am) &&
                                   am.ValueKind == JsonValueKind.Object;
            var mergeState = isMergedList
                ? "MERGED"
                : GetString(el, "mergeStateStatus") ?? "UNKNOWN";
            var body = GetString(el, "body") ?? string.Empty;

            var updatedAt = ParseDateTimeOffset(el, "updatedAt") ?? DateTimeOffset.UtcNow;
            var mergedAt = isMergedList ? ParseDateTimeOffset(el, "mergedAt") : null;

            var stateLabel = isMergedList
                ? "Merged"
                : autoMergeEnabled
                    ? "Auto-Merge"
                    : isDraft
                        ? "Draft"
                        : "Ready";

            var (ciStatus, sonarStatus) = isMergedList ? ("pass", "pass") : ClassifyChecks(el);
            var riskClass = ExtractRiskClass(body, title);
            var lockedAreas = ExtractLockedAreas(body);
            projectStatuses.TryGetValue(number, out var projectStatus);

            var comments = ParseComments(el);
            var participants = PrParticipantExtractor.Extract(branch, body, comments);
            var linkedIssues = ExtractLinkedIssues(el, number, repository, branch, body, issuesByNumber, knownPrNumbers);

            list.Add(new PullRequestInfo(
                Number: number,
                Title: title,
                Url: url,
                Branch: branch,
                State: isMergedList ? "MERGED" : "OPEN",
                StateLabel: stateLabel,
                IsDraft: isDraft,
                AutoMergeEnabled: autoMergeEnabled,
                MergeState: mergeState,
                CiStatus: ciStatus,
                SonarStatus: sonarStatus,
                RiskClass: riskClass,
                LockedAreas: lockedAreas,
                UpdatedAtUtc: updatedAt,
                MergedAtUtc: mergedAt,
                ProjectStatus: projectStatus,
                Participants: participants,
                LinkedIssues: linkedIssues));
        }

        return list
            .OrderByDescending(p => p.Number)
            .ToList();
    }

    private static List<LinkedIssueInfo> ExtractLinkedIssues(
        JsonElement prElement,
        int prNumber,
        string repository,
        string branch,
        string body,
        IReadOnlyDictionary<int, LinkedIssueInfo> issuesByNumber,
        IReadOnlySet<int> knownPrNumbers)
    {
        var results = new List<LinkedIssueInfo>();
        var seen = new HashSet<int>();

        void TryAddIssue(int issueNumber, string? fallbackUrl = null)
        {
            if (issueNumber <= 0 || issueNumber == prNumber || seen.Contains(issueNumber))
            {
                return;
            }

            if (issuesByNumber.TryGetValue(issueNumber, out var resolved))
            {
                seen.Add(issueNumber);
                results.Add(resolved);
                return;
            }

            if (knownPrNumbers.Contains(issueNumber))
            {
                return;
            }

            seen.Add(issueNumber);
            var url = !string.IsNullOrWhiteSpace(fallbackUrl)
                ? fallbackUrl
                : $"https://github.com/{repository}/issues/{issueNumber}";
            results.Add(new LinkedIssueInfo(issueNumber, Title: null, Url: url, State: null));
        }

        // 1. GitHub GraphQL closingIssuesReferences
        if (prElement.TryGetProperty("closingIssuesReferences", out var closingRefs) &&
            closingRefs.ValueKind == JsonValueKind.Array)
        {
            foreach (var refEl in closingRefs.EnumerateArray())
            {
                if (refEl.ValueKind == JsonValueKind.Object &&
                    refEl.TryGetProperty("number", out var numProp) &&
                    numProp.TryGetInt32(out var issueNum))
                {
                    var refUrl = GetString(refEl, "url");
                    if (seen.Add(issueNum) && issueNum != prNumber)
                    {
                        if (issuesByNumber.TryGetValue(issueNum, out var enriched))
                        {
                            results.Add(enriched);
                        }
                        else
                        {
                            var fallback = !string.IsNullOrWhiteSpace(refUrl)
                                ? refUrl
                                : $"https://github.com/{repository}/issues/{issueNum}";
                            results.Add(new LinkedIssueInfo(issueNum, Title: null, Url: fallback, State: null));
                        }
                    }
                }
            }
        }

        // 2. Slice header "- Issue: ..." lines in PR body
        foreach (Match lineMatch in IssueHeaderLineRegex().Matches(body))
        {
            var lineValue = lineMatch.Groups[1].Value.Trim();
            if (lineValue.StartsWith("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isFollowUpOnly = lineValue.Contains("follow-up to #", StringComparison.OrdinalIgnoreCase);
            foreach (Match numMatch in HashNumberRegex().Matches(lineValue))
            {
                if (int.TryParse(numMatch.Groups[1].Value, out var issueNum))
                {
                    if (isFollowUpOnly && !issuesByNumber.ContainsKey(issueNum))
                    {
                        continue;
                    }

                    TryAddIssue(issueNum);
                }
            }
        }

        // 3. Explicit Closes / Fixes / Resolves / Part of / Refs #N in PR body
        foreach (Match kwMatch in ExplicitIssueRefRegex().Matches(body))
        {
            if (int.TryParse(kwMatch.Groups[1].Value, out var issueNum))
            {
                TryAddIssue(issueNum);
            }
        }

        // 4. Branch naming convention (e.g. claude/issue-366-rf09-completion)
        foreach (Match branchMatch in BranchIssueRegex().Matches(branch))
        {
            if (int.TryParse(branchMatch.Groups[1].Value, out var issueNum))
            {
                TryAddIssue(issueNum);
            }
        }

        return results;
    }

    private static List<PrCommentInput> ParseComments(JsonElement prElement)
    {
        if (!prElement.TryGetProperty("comments", out var comments) ||
            comments.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<PrCommentInput>();
        foreach (var comment in comments.EnumerateArray())
        {
            if (comment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var author = string.Empty;
            if (comment.TryGetProperty("author", out var authorEl) && authorEl.ValueKind == JsonValueKind.Object)
            {
                author = GetString(authorEl, "login") ?? string.Empty;
            }

            var commentBody = GetString(comment, "body") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(commentBody))
            {
                result.Add(new PrCommentInput(author, commentBody));
            }
        }

        return result;
    }

    private static DateTimeOffset? ParseDateTimeOffset(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(prop.GetString(), out var dt)
            ? dt
            : null;
    }

    private static (string CiStatus, string SonarStatus) ClassifyChecks(JsonElement prElement)
    {
        if (!prElement.TryGetProperty("statusCheckRollup", out var checks) ||
            checks.ValueKind != JsonValueKind.Array)
        {
            return ("none", "none");
        }

        var ciChecks = new List<string>();
        var sonarChecks = new List<string>();

        foreach (var check in checks.EnumerateArray())
        {
            if (check.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(check, "name") ?? GetString(check, "context") ?? string.Empty;
            var outcome = ClassifySingleCheck(check);
            if (outcome == "skip")
            {
                continue;
            }

            if (name.Contains("sonar", StringComparison.OrdinalIgnoreCase))
            {
                sonarChecks.Add(outcome);
            }
            else
            {
                ciChecks.Add(outcome);
            }
        }

        return (AggregateOutcome(ciChecks), AggregateOutcome(sonarChecks));
    }

    private static string ClassifySingleCheck(JsonElement check)
    {
        var status = GetString(check, "status");
        var conclusion = GetString(check, "conclusion");
        var state = GetString(check, "state");

        if (!string.IsNullOrEmpty(status) &&
            !string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return "pending";
        }

        var normalized = conclusion ?? state ?? string.Empty;
        return normalized.ToUpperInvariant() switch
        {
            "FAILURE" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE" or "ERROR" => "fail",
            "PENDING" or "EXPECTED" or "IN_PROGRESS" or "QUEUED" => "pending",
            "SKIPPED" => "skip",
            "SUCCESS" or "NEUTRAL" => "pass",
            _ => "none"
        };
    }

    private static string AggregateOutcome(List<string> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return "none";
        }

        if (outcomes.Contains("fail"))
        {
            return "fail";
        }

        if (outcomes.Contains("pending"))
        {
            return "pending";
        }

        if (outcomes.Contains("pass"))
        {
            return "pass";
        }

        return "none";
    }

    private static string ExtractRiskClass(string body, string title)
    {
        var combined = $"{title}\n{body}";
        var match = RiskClassRegex().Match(combined);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "?";
    }

    private static List<string> ExtractLockedAreas(string body)
    {
        var match = LockedAreasRegex().Match(body);
        if (!match.Success)
        {
            return [];
        }

        return match.Groups[1].Value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('`', '*', ' '))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    [GeneratedRegex(@"(?:Risk\s*class(?:\s*\([^)]*\))?|Risikoklasse(?:\s*\([^)]*\))?|Class)\s*[:\-]?\s*`?([HML])`?\b", RegexOptions.IgnoreCase)]
    private static partial Regex RiskClassRegex();

    [GeneratedRegex(@"(?:Locked\s*areas?|Gesperrte\s*Bereiche?)\s*[:\-]\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LockedAreasRegex();

    [GeneratedRegex(@"^[-*]\s*Issue\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex IssueHeaderLineRegex();

    [GeneratedRegex(@"\b(?:Closes|Fixes|Resolves|Part\s+of|Refs)\s+#(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitIssueRefRegex();

    [GeneratedRegex(@"(?:^|[-_/])issue[-_]?(\d+)(?:[-_/]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex BranchIssueRegex();

    [GeneratedRegex(@"#(\d+)")]
    private static partial Regex HashNumberRegex();
}
