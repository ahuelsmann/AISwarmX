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
    private readonly Dictionary<string, PipelineResponse> _lastSuccessfulResponses = new(StringComparer.OrdinalIgnoreCase);

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
                "--json", "number,title,url,headRefName,isDraft,mergeStateStatus,mergeable,reviewDecision,autoMergeRequest,updatedAt,body,statusCheckRollup,comments,closingIssuesReferences,projectItems"
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

        var projectTask = TryLoadActiveKanbanBoardAsync(options, cancellationToken);
        var issuesTask = TryLoadIssuesAsync(options, cancellationToken);

        await Task.WhenAll(openPrTask, mergedPrTask, projectTask, issuesTask).ConfigureAwait(false);

        var openResult = openPrTask.Result;
        var pullRequestsDisabled = await IsPullRequestsDisabledAsync(
            openResult,
            options.GitHubRepository,
            cancellationToken).ConfigureAwait(false);
        if (!openResult.Succeeded && !pullRequestsDisabled)
        {
            return BuildUnavailableOrCached(options, now, openResult);
        }

        var (projectAvailable, projectNotice, projectStatuses, kanbanItems) = projectTask.Result;
        var issuesByNumber = issuesTask.Result;

        try
        {
            var mergedResult = mergedPrTask.Result;
            var openJson = openResult.Succeeded ? openResult.StdOut : "[]";
            var mergedJson = mergedResult.Succeeded ? mergedResult.StdOut : null;
            var knownPrNumbers = CollectPrNumbers(openJson, mergedJson);

            var openPullRequests = openResult.Succeeded
                ? ParsePullRequests(
                    openResult.StdOut,
                    isMergedList: false,
                    options.GitHubRepository,
                    projectStatuses,
                    issuesByNumber,
                    knownPrNumbers)
                : [];

            var recentlyMerged = mergedResult.Succeeded
                ? ParseMergedSafe(mergedResult.StdOut, options.GitHubRepository, issuesByNumber, knownPrNumbers)
                : [];

            var activeIssues = BuildActiveIssues(
                kanbanItems,
                projectAvailable,
                openPullRequests,
                recentlyMerged,
                now);

            var projectBoardUrl = options.GitHubProjectNumber > 0 &&
                                  !string.IsNullOrWhiteSpace(options.GitHubProjectOwner)
                ? $"https://github.com/users/{options.GitHubProjectOwner}/projects/{options.GitHubProjectNumber}"
                : null;

            var sourceNotice = pullRequestsDisabled
                ? "Pull requests are disabled on this repository; showing issues and project board only."
                : null;

            var response = new PipelineResponse(
                GeneratedAtUtc: now,
                Repository: options.GitHubRepository,
                PullRequests: openPullRequests,
                RecentlyMerged: recentlyMerged,
                ActiveIssues: activeIssues,
                ProjectBoardAvailable: projectAvailable,
                ProjectBoardNotice: projectNotice,
                ProjectBoardUrl: projectBoardUrl,
                Source: new SourceHealth("ok", now, sourceNotice));

            var cacheKey = BuildCacheKey(options);
            lock (_fallbackLock)
            {
                _lastSuccessfulResponses[cacheKey] = response;
            }

            return response;
        }
        catch (JsonException ex)
        {
            return BuildUnavailableOrCached(
                options,
                now,
                new CommandResult(-1, string.Empty, ex.Message));
        }
    }

    private async Task<bool> IsPullRequestsDisabledAsync(
        CommandResult result,
        string repository,
        CancellationToken cancellationToken)
    {
        var (definitelyDisabled, needs404Confirmation) = ClassifyPullRequestsError(result);
        if (definitelyDisabled)
        {
            return true;
        }

        if (!needs404Confirmation || string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        var metadataResult = await _commandRunner.RunAsync(
            "gh",
            ["api", $"repos/{repository.Trim()}", "--jq", ".has_pull_requests"],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        return metadataResult.Succeeded &&
               string.Equals(metadataResult.StdOut.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    internal static (bool DefinitelyDisabled, bool Needs404Confirmation) ClassifyPullRequestsError(CommandResult result)
    {
        if (result.Succeeded || result.NotFound || result.TimedOut || string.IsNullOrWhiteSpace(result.StdErr))
        {
            return (false, false);
        }

        var err = result.StdErr;
        if (err.Contains("Pull requests are disabled", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Pull requests have been disabled", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("disabled pull requests", StringComparison.OrdinalIgnoreCase))
        {
            return (true, false);
        }

        var mentionsPullsEndpoint = err.Contains("/pulls", StringComparison.OrdinalIgnoreCase);
        if (err.Contains("HTTP 410", StringComparison.OrdinalIgnoreCase) ||
            (mentionsPullsEndpoint && err.Contains("410", StringComparison.Ordinal)))
        {
            return (true, false);
        }

        var has404 = err.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase) ||
                     (mentionsPullsEndpoint && err.Contains("404", StringComparison.Ordinal));
        return (false, has404);
    }

    private PipelineResponse BuildUnavailableOrCached(
        DashboardOptions options,
        DateTimeOffset now,
        CommandResult failedResult)
    {
        PipelineResponse? cached;
        var cacheKey = BuildCacheKey(options);
        lock (_fallbackLock)
        {
            _lastSuccessfulResponses.TryGetValue(cacheKey, out cached);
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
            Repository: options.GitHubRepository,
            PullRequests: [],
            RecentlyMerged: [],
            ActiveIssues: [],
            ProjectBoardAvailable: false,
            ProjectBoardNotice: null,
            ProjectBoardUrl: null,
            Source: new SourceHealth("unavailable", now, reason));
    }

    private static string BuildCacheKey(DashboardOptions options)
    {
        return $"{options.GitHubRepository?.Trim()}|{options.GitHubProjectOwner?.Trim()}|{options.GitHubProjectNumber}";
    }

    private const string ActiveKanbanGraphQlQuery =
        "query($owner:String!,$num:Int!){user(login:$owner){projectV2(number:$num){items(first:100,query:\"status:\\\"In progress\\\",\\\"In review\\\",Ready\"){nodes{fieldValues(first:12){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}}... on ProjectV2ItemFieldTextValue{text field{... on ProjectV2Field{name}}}}}content{__typename ... on Issue{number title url state updatedAt}... on PullRequest{number title url state isDraft updatedAt}}}}}}}";

    private sealed record RawKanbanItem(
        int Number,
        string ItemKind,
        string Title,
        string Url,
        string GitHubState,
        string KanbanStatus,
        string? Priority,
        string? Area,
        string? IssueType,
        string? RiskClass,
        string? Writer,
        IReadOnlyList<string> LockedAreas,
        DateTimeOffset UpdatedAtUtc);

    private async Task<(
        bool Available,
        string? Notice,
        Dictionary<int, string> PrStatuses,
        List<RawKanbanItem> RawItems)> TryLoadActiveKanbanBoardAsync(
        DashboardOptions options,
        CancellationToken cancellationToken)
    {
        var prStatuses = new Dictionary<int, string>();
        var rawItems = new List<RawKanbanItem>();

        if (options.GitHubProjectNumber <= 0 || string.IsNullOrWhiteSpace(options.GitHubProjectOwner))
        {
            return (false, "No GitHub project configured.", prStatuses, rawItems);
        }

        var projectResult = await _commandRunner.RunAsync(
            "gh",
            [
                "api", "graphql",
                "-f", $"owner={options.GitHubProjectOwner}",
                "-F", $"num={options.GitHubProjectNumber}",
                "-f", $"query={ActiveKanbanGraphQlQuery}"
            ],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(12),
            cancellationToken).ConfigureAwait(false);

        if (!projectResult.Succeeded || string.IsNullOrWhiteSpace(projectResult.StdOut))
        {
            return (
                false,
                "Project board data hidden: missing read:project scope or Kanban CLI unreachable.",
                prStatuses,
                rawItems);
        }

        try
        {
            using var doc = JsonDocument.Parse(projectResult.StdOut);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errorsEl) &&
                errorsEl.ValueKind == JsonValueKind.Array &&
                errorsEl.GetArrayLength() > 0)
            {
                return (
                    false,
                    "Project board data hidden: missing read:project scope or Kanban CLI unreachable.",
                    prStatuses,
                    rawItems);
            }

            if (!root.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Object ||
                !dataEl.TryGetProperty("user", out var userEl) ||
                userEl.ValueKind != JsonValueKind.Object ||
                !userEl.TryGetProperty("projectV2", out var projectEl) ||
                projectEl.ValueKind != JsonValueKind.Object ||
                !projectEl.TryGetProperty("items", out var itemsEl) ||
                itemsEl.ValueKind != JsonValueKind.Object ||
                !itemsEl.TryGetProperty("nodes", out var nodesEl) ||
                nodesEl.ValueKind != JsonValueKind.Array)
            {
                return (
                    false,
                    "Unable to parse GitHub project data.",
                    prStatuses,
                    rawItems);
            }

            foreach (var node in nodesEl.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object ||
                    !node.TryGetProperty("content", out var contentEl) ||
                    contentEl.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!contentEl.TryGetProperty("number", out var numProp) ||
                    !numProp.TryGetInt32(out var number))
                {
                    continue;
                }

                var fields = ReadKanbanFields(node);
                var status = fields.GetValueOrDefault("Status");
                var typeName = GetString(contentEl, "__typename");
                var isPr = string.Equals(typeName, "PullRequest", StringComparison.OrdinalIgnoreCase);
                var isIssue = string.Equals(typeName, "Issue", StringComparison.OrdinalIgnoreCase);

                if (isPr && !string.IsNullOrWhiteSpace(status))
                {
                    prStatuses[number] = status;
                }

                if ((!isPr && !isIssue) || string.IsNullOrWhiteSpace(status) || !IsActiveKanbanColumn(status))
                {
                    continue;
                }

                var itemKind = isPr ? "PullRequest" : "Issue";
                var title = GetString(contentEl, "title")?.Trim() ??
                            $"{(isPr ? "PR" : "Issue")} #{number}";
                var url = GetString(contentEl, "url") ??
                          $"https://github.com/{options.GitHubRepository}/{(isPr ? "pull" : "issues")}/{number}";
                var gitHubState = (GetString(contentEl, "state") ?? "OPEN").Trim().ToUpperInvariant();
                var updatedAt = ParseDateTimeOffset(contentEl, "updatedAt") ?? DateTimeOffset.UtcNow;

                var riskRaw = fields.GetValueOrDefault("Risk class");
                var riskClass = NormalizeKanbanRiskClass(riskRaw);

                var lockedRaw = fields.GetValueOrDefault("Locked areas");
                var lockedAreas = string.IsNullOrWhiteSpace(lockedRaw)
                    ? (IReadOnlyList<string>)[]
                    : lockedRaw
                        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => s.Trim('`', '*', ' '))
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();

                rawItems.Add(new RawKanbanItem(
                    Number: number,
                    ItemKind: itemKind,
                    Title: title,
                    Url: url,
                    GitHubState: gitHubState,
                    KanbanStatus: status,
                    Priority: fields.GetValueOrDefault("Priority"),
                    Area: fields.GetValueOrDefault("Area"),
                    IssueType: fields.GetValueOrDefault("Type"),
                    RiskClass: riskClass,
                    Writer: fields.GetValueOrDefault("Writer"),
                    LockedAreas: lockedAreas,
                    UpdatedAtUtc: updatedAt));
            }

            return (true, null, prStatuses, rawItems);
        }
        catch (JsonException)
        {
            return (false, "Unable to parse GitHub project data.", prStatuses, rawItems);
        }
    }

    private static Dictionary<string, string> ReadKanbanFields(JsonElement itemNode)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!itemNode.TryGetProperty("fieldValues", out var fieldValuesEl) ||
            fieldValuesEl.ValueKind != JsonValueKind.Object ||
            !fieldValuesEl.TryGetProperty("nodes", out var nodesEl) ||
            nodesEl.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var fv in nodesEl.EnumerateArray())
        {
            if (fv.ValueKind != JsonValueKind.Object ||
                !fv.TryGetProperty("field", out var fieldEl) ||
                fieldEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fieldName = GetString(fieldEl, "name");
            var value = GetString(fv, "name") ?? GetString(fv, "text");
            if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(value))
            {
                map[fieldName.Trim()] = value.Trim();
            }
        }

        return map;
    }

    private static bool IsActiveKanbanColumn(string status) =>
        string.Equals(status, "In progress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "In review", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeKanbanRiskClass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed is "H" or "M" or "L")
        {
            return trimmed;
        }

        if (trimmed.StartsWith("HIGH", StringComparison.Ordinal))
        {
            return "H";
        }

        if (trimmed.StartsWith("MEDIUM", StringComparison.Ordinal))
        {
            return "M";
        }

        if (trimmed.StartsWith("LOW", StringComparison.Ordinal))
        {
            return "L";
        }

        return null;
    }

    private static IReadOnlyList<ActiveIssueInfo> BuildActiveIssues(
        IReadOnlyList<RawKanbanItem> kanbanItems,
        bool projectBoardAvailable,
        IReadOnlyList<PullRequestInfo> openPullRequests,
        IReadOnlyList<PullRequestInfo> recentlyMerged,
        DateTimeOffset now)
    {
        var results = new List<(bool HasOpenPr, ActiveIssueInfo Info)>();
        var seenItems = new HashSet<(string ItemKind, int Number)>();

        if (projectBoardAvailable)
        {
            foreach (var item in kanbanItems)
            {
                if (!seenItems.Add((item.ItemKind, item.Number)))
                {
                    continue;
                }

                if (string.Equals(item.ItemKind, "PullRequest", StringComparison.OrdinalIgnoreCase))
                {
                    var driftReason = item.GitHubState is "MERGED" or "CLOSED"
                        ? "Merged/Closed on GitHub — move to Done on Kanban"
                        : null;

                    // Open PRs already have their dedicated Open PRs section and appear as linked chips on issues;
                    // keep PullRequest cards in ActiveIssues only when they are already merged/closed (board drift).
                    if (driftReason is null)
                    {
                        continue;
                    }

                    var matchingOpenPr = openPullRequests.FirstOrDefault(p => p.Number == item.Number);
                    var matchingMergedPr = recentlyMerged.FirstOrDefault(p => p.Number == item.Number);
                    var matchedPr = matchingOpenPr ?? matchingMergedPr;

                    var riskClass = item.RiskClass ??
                                    (matchedPr?.RiskClass is "H" or "M" or "L" ? matchedPr.RiskClass : null);

                    var writer = !string.IsNullOrWhiteSpace(item.Writer)
                        ? item.Writer
                        : matchedPr?.Participants
                            .FirstOrDefault(pt => string.Equals(pt.Role, "Writer", StringComparison.OrdinalIgnoreCase))
                            ?.Agent;

                    var lockedAreas = item.LockedAreas.Count > 0
                        ? item.LockedAreas
                        : matchedPr?.LockedAreas ?? [];

                    var prParticipants = matchedPr?.Participants ?? [];
                    var kanbanPrParticipants = PrParticipantExtractor.ParseKanbanParticipants(writer);
                    var participants = MergePrAndKanbanParticipants(prParticipants, kanbanPrParticipants);
                    var linkedIssues = matchedPr?.LinkedIssues ?? [];

                    results.Add((matchingOpenPr is not null, new ActiveIssueInfo(
                        Number: item.Number,
                        ItemKind: "PullRequest",
                        Title: item.Title,
                        Url: item.Url,
                        GitHubState: item.GitHubState,
                        KanbanStatus: item.KanbanStatus,
                        Priority: item.Priority,
                        Area: item.Area,
                        IssueType: item.IssueType,
                        RiskClass: riskClass,
                        Writer: writer,
                        LockedAreas: lockedAreas,
                        UpdatedAtUtc: item.UpdatedAtUtc,
                        LinkedPullRequests: [],
                        LinkedIssues: linkedIssues,
                        Participants: participants,
                        BoardDriftReason: driftReason)));
                    continue;
                }

                var linkedOpenPrs = openPullRequests
                    .Where(p => p.LinkedIssues.Any(li => li.Number == item.Number))
                    .OrderByDescending(p => p.Number)
                    .ToList();

                var linkedMergedPrs = recentlyMerged
                    .Where(p => p.LinkedIssues.Any(li => li.Number == item.Number))
                    .OrderByDescending(p => p.Number)
                    .ToList();

                var linkedPullRequests = BuildLinkedPrRefs(linkedOpenPrs, linkedMergedPrs);
                var prIssueParticipants =
                    MergeParticipants(linkedOpenPrs.Count > 0 ? linkedOpenPrs : linkedMergedPrs);

                var issueRiskClass = item.RiskClass ??
                                     linkedOpenPrs
                                         .Select(p => p.RiskClass)
                                         .FirstOrDefault(r => r is "H" or "M" or "L");

                var issueWriter = !string.IsNullOrWhiteSpace(item.Writer)
                    ? item.Writer
                    : linkedOpenPrs
                        .SelectMany(p => p.Participants)
                        .FirstOrDefault(pt => string.Equals(pt.Role, "Writer", StringComparison.OrdinalIgnoreCase))
                        ?.Agent;

                var kanbanIssueParticipants = PrParticipantExtractor.ParseKanbanParticipants(issueWriter);
                var issueParticipants = MergePrAndKanbanParticipants(prIssueParticipants, kanbanIssueParticipants);

                var issueLockedAreas = item.LockedAreas.Count > 0
                    ? item.LockedAreas
                    : linkedOpenPrs
                        .SelectMany(p => p.LockedAreas)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var issueDriftReason = ComputeBoardDriftReason(
                    item.GitHubState,
                    item.KanbanStatus,
                    hasOpenPr: linkedOpenPrs.Count > 0,
                    effectiveWriter: issueWriter,
                    effectiveRiskClass: issueRiskClass,
                    updatedAtUtc: item.UpdatedAtUtc,
                    now: now);

                results.Add((linkedOpenPrs.Count > 0, new ActiveIssueInfo(
                    Number: item.Number,
                    ItemKind: "Issue",
                    Title: item.Title,
                    Url: item.Url,
                    GitHubState: item.GitHubState,
                    KanbanStatus: item.KanbanStatus,
                    Priority: item.Priority,
                    Area: item.Area,
                    IssueType: item.IssueType,
                    RiskClass: issueRiskClass,
                    Writer: issueWriter,
                    LockedAreas: issueLockedAreas,
                    UpdatedAtUtc: item.UpdatedAtUtc,
                    LinkedPullRequests: linkedPullRequests,
                    LinkedIssues: [],
                    Participants: issueParticipants,
                    BoardDriftReason: issueDriftReason)));
            }
        }
        else
        {
            // Fallback only when Kanban board API is unavailable (e.g., missing read:project scope):
            // surface OPEN issues referenced from open PRs so the tab stays useful.
            var prReferencedIssues = openPullRequests
                .SelectMany(p => p.LinkedIssues)
                .Where(li => li.Number > 0 && !string.Equals(li.State, "CLOSED", StringComparison.OrdinalIgnoreCase))
                .GroupBy(li => li.Number)
                .Select(g => g.First());

            foreach (var li in prReferencedIssues)
            {
                if (!seenItems.Add(("Issue", li.Number)))
                {
                    continue;
                }

                var linkedOpenPrs = openPullRequests
                    .Where(p => p.LinkedIssues.Any(x => x.Number == li.Number))
                    .OrderByDescending(p => p.Number)
                    .ToList();

                var linkedMergedPrs = recentlyMerged
                    .Where(p => p.LinkedIssues.Any(x => x.Number == li.Number))
                    .OrderByDescending(p => p.Number)
                    .ToList();

                var riskClass = linkedOpenPrs
                    .Select(p => p.RiskClass)
                    .FirstOrDefault(r => r is "H" or "M" or "L");

                var writer = linkedOpenPrs
                    .SelectMany(p => p.Participants)
                    .FirstOrDefault(pt => string.Equals(pt.Role, "Writer", StringComparison.OrdinalIgnoreCase))
                    ?.Agent;

                var lockedAreas = linkedOpenPrs
                    .SelectMany(p => p.LockedAreas)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                results.Add((true, new ActiveIssueInfo(
                    Number: li.Number,
                    ItemKind: "Issue",
                    Title: !string.IsNullOrWhiteSpace(li.Title) ? li.Title! : $"Issue #{li.Number}",
                    Url: li.Url,
                    GitHubState: li.State ?? "OPEN",
                    KanbanStatus: "Via Open PR",
                    Priority: null,
                    Area: null,
                    IssueType: null,
                    RiskClass: riskClass,
                    Writer: writer,
                    LockedAreas: lockedAreas,
                    UpdatedAtUtc: linkedOpenPrs.Max(p => p.UpdatedAtUtc),
                    LinkedPullRequests: BuildLinkedPrRefs(linkedOpenPrs, linkedMergedPrs),
                    LinkedIssues: [],
                    Participants: MergeParticipants(linkedOpenPrs),
                    BoardDriftReason: null)));
            }
        }

        return results
            .OrderBy(item => KanbanStatusRank(item.Info.KanbanStatus))
            .ThenBy(item => item.Info.BoardDriftReason is null ? 0 : 1)
            .ThenBy(item => string.Equals(item.Info.ItemKind, "PullRequest", StringComparison.OrdinalIgnoreCase) || item.HasOpenPr ? 0 : 1)
            .ThenByDescending(item => item.Info.UpdatedAtUtc)
            .Select(item => item.Info)
            .ToList();
    }

    private static List<LinkedPullRequestRef> BuildLinkedPrRefs(
        List<PullRequestInfo> openPrs,
        List<PullRequestInfo> mergedPrs)
    {
        var refs = new List<LinkedPullRequestRef>();
        var seen = new HashSet<int>();

        foreach (var pr in openPrs)
        {
            if (seen.Add(pr.Number))
            {
                refs.Add(new LinkedPullRequestRef(pr.Number, pr.Title, pr.Url, pr.StateLabel, IsMerged: false));
            }
        }

        foreach (var pr in mergedPrs)
        {
            if (seen.Add(pr.Number))
            {
                refs.Add(new LinkedPullRequestRef(pr.Number, pr.Title, pr.Url, pr.StateLabel, IsMerged: true));
            }
        }

        return refs;
    }

    private static List<TaskParticipantInfo> MergeParticipants(IReadOnlyList<PullRequestInfo> prs)
    {
        return prs
            .SelectMany(p => p.Participants)
            .GroupBy(pt => (pt.Role, pt.Tool, pt.Profile))
            .Select(g => g.First())
            .ToList();
    }

    private static IReadOnlyList<TaskParticipantInfo> MergePrAndKanbanParticipants(
        IReadOnlyList<TaskParticipantInfo> prParticipants,
        IReadOnlyList<TaskParticipantInfo> kanbanParticipants)
    {
        if (prParticipants.Count == 0)
        {
            return kanbanParticipants;
        }

        if (kanbanParticipants.Count == 0)
        {
            return prParticipants;
        }

        var merged = new List<TaskParticipantInfo>(prParticipants);
        foreach (var kp in kanbanParticipants)
        {
            var alreadyCovered = merged.Any(p =>
                string.Equals(p.Profile, kp.Profile, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(p.Tool, kp.Tool, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(p.Role, kp.Role, StringComparison.OrdinalIgnoreCase)));
            if (!alreadyCovered)
            {
                merged.Add(kp);
            }
        }

        return merged;
    }

    private static string? ComputeBoardDriftReason(
        string gitHubState,
        string kanbanStatus,
        bool hasOpenPr,
        string? effectiveWriter,
        string? effectiveRiskClass,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset now)
    {
        if (string.Equals(gitHubState, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return "Closed on GitHub — move to Done on Kanban";
        }

        if ((string.Equals(kanbanStatus, "In progress", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(kanbanStatus, "In review", StringComparison.OrdinalIgnoreCase)) &&
            !hasOpenPr &&
            string.IsNullOrWhiteSpace(effectiveWriter) &&
            string.IsNullOrWhiteSpace(effectiveRiskClass) &&
            (now - updatedAtUtc) >= TimeSpan.FromHours(48))
        {
            return "No active PR or Writer (stale on board)";
        }

        return null;
    }

    private static int KanbanStatusRank(string status)
    {
        if (string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(status, "In progress", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(status, "In review", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
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
            var projectStatus = isMergedList ? null : ExtractInlineProjectStatus(el);
            if (string.IsNullOrWhiteSpace(projectStatus))
            {
                projectStatuses.TryGetValue(number, out projectStatus);
            }

            var comments = ParseComments(el);
            var participants = PrParticipantExtractor.Extract(branch, body, comments);
            var linkedIssues = ExtractLinkedIssues(el, number, repository, branch, body, issuesByNumber, knownPrNumbers);
            var mergeBlockReasons = isMergedList
                ? []
                : ExtractMergeBlockReasons(el, isDraft, mergeState, participants);

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
                LinkedIssues: linkedIssues,
                MergeBlockReasons: mergeBlockReasons));
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

    private static string? ExtractInlineProjectStatus(JsonElement prElement)
    {
        if (!prElement.TryGetProperty("projectItems", out var projectItems) ||
            projectItems.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var item in projectItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("status", out var statusEl) ||
                statusEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var statusName = GetString(statusEl, "name");
            if (string.IsNullOrWhiteSpace(statusName))
            {
                continue;
            }

            var projectTitle = GetString(item, "title");
            if (string.Equals(projectTitle, "Kanban", StringComparison.OrdinalIgnoreCase))
            {
                return statusName.Trim();
            }

            fallback ??= statusName.Trim();
        }

        return fallback;
    }

    private static IReadOnlyList<string> ExtractMergeBlockReasons(
        JsonElement prElement,
        bool isDraft,
        string mergeState,
        IReadOnlyList<TaskParticipantInfo> participants)
    {
        var normalizedState = mergeState.ToUpperInvariant();
        if (normalizedState is not ("BLOCKED" or "DIRTY" or "UNSTABLE" or "BEHIND"))
        {
            return [];
        }

        var reasons = new List<string>();

        if (isDraft)
        {
            reasons.Add("Draft PR (not yet marked Ready for review)");
        }

        var mergeable = GetString(prElement, "mergeable") ?? string.Empty;
        if (string.Equals(mergeable, "CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
            normalizedState == "DIRTY")
        {
            reasons.Add("Merge conflict with main — merge main and resolve conflicts");
        }
        else if (normalizedState == "BEHIND")
        {
            reasons.Add("Branch is behind main — merge current main");
        }

        var reviewDecision = GetString(prElement, "reviewDecision") ?? string.Empty;
        if (string.Equals(reviewDecision, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Changes requested in GitHub review");
        }
        else if (string.Equals(reviewDecision, "REVIEW_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Required approving review missing");
        }

        if (participants.Any(p => string.Equals(p.Status, "Blocked", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("Review/Model gate marked BLOCKED (see PR comments)");
        }

        var failingChecks = CollectCheckNames(prElement, "fail");
        if (failingChecks.Count > 0)
        {
            reasons.Add(FormatCheckReason(failingChecks, "Check failing", "checks failing"));
        }

        var runningChecks = CollectCheckNames(prElement, "pending");
        if (runningChecks.Count > 0)
        {
            reasons.Add(FormatCheckReason(runningChecks, "Check running", "checks running"));
        }

        if (normalizedState == "BLOCKED" && reasons.Count == 0)
        {
            reasons.Add("Blocked by GitHub branch protection rule or required check/review");
        }

        return reasons;
    }

    private static List<string> CollectCheckNames(JsonElement prElement, string targetOutcome)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!prElement.TryGetProperty("statusCheckRollup", out var checks) ||
            checks.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var check in checks.EnumerateArray())
        {
            if (check.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!string.Equals(ClassifySingleCheck(check), targetOutcome, StringComparison.Ordinal))
            {
                continue;
            }

            var name = GetString(check, "name") ?? GetString(check, "context");
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name.Trim()))
            {
                names.Add(name.Trim());
            }
        }

        return names;
    }

    private static string FormatCheckReason(IReadOnlyList<string> checkNames, string singularPrefix, string pluralSuffix)
    {
        return checkNames.Count switch
        {
            1 => $"{singularPrefix}: {checkNames[0]}",
            2 => $"2 {pluralSuffix}: {checkNames[0]}, {checkNames[1]}",
            _ => $"{checkNames.Count} {pluralSuffix}: {checkNames[0]}, {checkNames[1]} (+{checkNames.Count - 2} more)"
        };
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
