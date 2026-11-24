export interface SourceHealth {
  status: 'ok' | 'degraded' | 'unavailable' | string;
  updatedAtUtc: string;
  notice?: string | null;
  fromCache: boolean;
}

export interface MonitorInfo {
  id: string;
  displayName: string;
  role: string;
  profile: string;
  tool: string;
  state: 'busy' | 'standby' | 'rate_limited' | 'offline' | string;
  model?: string | null;
  lastActivityUtc?: string | null;
  rateLimitResetLabel?: string | null;
  activeSessionCount: number;
  automationStatus?: string | null;
  source: SourceHealth;
  activeSubAgentCount?: number;
  activeSubAgentsByModel?: Record<string, number> | null;
}

export interface SubAgentModelCount {
  model: string;
  profile: string;
  count: number;
}

export interface ActiveAgentsSummary {
  standingMonitorsTotal: number;
  standingMonitorsOnline: number;
  standingMonitorsBusy: number;
  activeSubAgentsTotal: number;
  totalActiveAgents: number;
  subAgentsByModel?: SubAgentModelCount[] | null;
}

export interface MonitorsResponse {
  generatedAtUtc: string;
  monitors: MonitorInfo[];
  source: SourceHealth;
  summary?: ActiveAgentsSummary | null;
}

export interface CapabilityProfileInfo {
  profile: string;
  name: string;
  models: string;
  strengths: string;
}

export interface RoleDefinitionInfo {
  role: string;
  profiles: string;
  responsibility: string;
}

export interface RiskClassInfo {
  code: string;
  name: string;
  areas: string;
  requiredDiversity: string;
}

export interface AgentSyncItem {
  tool: string;
  kind: 'agent' | 'rule' | string;
  name: string;
  fileName: string;
  description?: string | null;
  profileHint?: string | null;
  syncStatus: 'synced' | 'drift' | 'missing_local' | 'missing_repo' | string;
  repoHash?: string | null;
  localHash?: string | null;
  driftSummary?: string | null;
}

export interface SkillInfo {
  name: string;
  description: string;
  sourceRepo: string;
  relativePath: string;
}

export interface GovernanceResponse {
  generatedAtUtc: string;
  profiles: CapabilityProfileInfo[];
  roles: RoleDefinitionInfo[];
  riskClasses: RiskClassInfo[];
  agents: AgentSyncItem[];
  skills: SkillInfo[];
  syncedCount: number;
  driftCount: number;
  missingCount: number;
  source: SourceHealth;
}

export interface DiffLineInfo {
  kind: 'context' | 'added' | 'removed' | 'hunk' | string;
  targetLineNumber?: number | null;
  actualLineNumber?: number | null;
  text: string;
}

export interface RuleDiffResponse {
  tool: string;
  kind: string;
  fileName: string;
  syncStatus: string;
  targetHash?: string | null;
  actualHash?: string | null;
  addedLines: number;
  removedLines: number;
  truncated: boolean;
  lines: DiffLineInfo[];
}

export type ConfigDocsScope = 'mobadev';

export interface ConfigDocItem {
  scope: ConfigDocsScope | string;
  relativePath: string;
  fileName: string;
  group: string;
  repository: string;
  sizeBytes: number;
  lastModifiedUtc: string;
}

export interface ConfigDocsCatalogResponse {
  generatedAtUtc: string;
  scope: ConfigDocsScope | string;
  repository: string;
  files: ConfigDocItem[];
  source: SourceHealth;
}

export interface ConfigDocContentResponse {
  scope: ConfigDocsScope | string;
  relativePath: string;
  fileName: string;
  group: string;
  repository: string;
  sizeBytes: number;
  lineCount: number;
  lastModifiedUtc: string;
  truncated: boolean;
  content: string;
}

export interface TaskParticipantInfo {
  agent: string;
  tool: string;
  role: string;
  profile: string;
  model: string;
  status: string;
  evidenceSource: string;
}

export interface LinkedIssueInfo {
  number: number;
  title?: string | null;
  url: string;
  state?: string | null;
}

export interface LinkedPullRequestRef {
  number: number;
  title: string;
  url: string;
  stateLabel: string;
  isMerged: boolean;
}

export interface ActiveIssueInfo {
  number: number;
  itemKind?: 'Issue' | 'PullRequest' | string;
  title: string;
  url: string;
  gitHubState: 'OPEN' | 'CLOSED' | 'MERGED' | string;
  kanbanStatus: string;
  priority?: string | null;
  area?: string | null;
  issueType?: string | null;
  riskClass?: string | null;
  writer?: string | null;
  lockedAreas: string[];
  updatedAtUtc: string;
  linkedPullRequests: LinkedPullRequestRef[];
  linkedIssues?: LinkedIssueInfo[];
  participants: TaskParticipantInfo[];
  boardDriftReason?: string | null;
}

export interface PullRequestInfo {
  number: number;
  title: string;
  url: string;
  branch: string;
  state: 'OPEN' | 'MERGED' | string;
  stateLabel: 'Draft' | 'Ready' | 'Auto-Merge' | 'Merged' | string;
  isDraft: boolean;
  autoMergeEnabled: boolean;
  mergeState: 'CLEAN' | 'BEHIND' | 'DIRTY' | 'BLOCKED' | 'UNSTABLE' | 'MERGED' | string;
  ciStatus: 'pass' | 'pending' | 'fail' | 'none' | string;
  sonarStatus: 'pass' | 'pending' | 'fail' | 'none' | string;
  riskClass: 'H' | 'M' | 'L' | '?' | string;
  lockedAreas: string[];
  updatedAtUtc: string;
  mergedAtUtc?: string | null;
  projectStatus?: string | null;
  participants: TaskParticipantInfo[];
  linkedIssues: LinkedIssueInfo[];
  mergeBlockReasons?: string[];
}

export interface PipelineResponse {
  generatedAtUtc: string;
  repository: string;
  pullRequests: PullRequestInfo[];
  recentlyMerged: PullRequestInfo[];
  activeIssues?: ActiveIssueInfo[];
  projectBoardAvailable: boolean;
  projectBoardNotice?: string | null;
  projectBoardUrl?: string | null;
  source: SourceHealth;
}

export interface ToolSessionHygiene {
  tool: string;
  monitorName: string;
  targetCount: number;
  actualCount: number;
  status: 'ok' | 'inactive' | 'warning' | string;
  notice?: string | null;
}

export interface WorktreeInfo {
  folderName: string;
  branch: string;
  headShort: string;
  isMain: boolean;
  status: 'main' | 'active' | 'stale' | string;
  linkedPrNumber?: number | null;
  isDirty?: boolean;
}

export interface BranchHygieneInfo {
  repository: string;
  name: string;
  scope: 'local' | 'remote' | string;
  status: 'active' | 'stale' | string;
  upstreamGone: boolean;
  linkedPrNumber?: number | null;
}

export interface HygieneResponse {
  generatedAtUtc: string;
  sessions: ToolSessionHygiene[];
  worktrees: WorktreeInfo[];
  staleWorktreeCount: number;
  source: SourceHealth;
  branches?: BranchHygieneInfo[];
  staleLocalBranchCount?: number;
  staleRemoteBranchCount?: number;
}

export interface WarningBreakdown {
  rulesDrift: number;
  staleWorktrees: number;
  staleBranches: number;
  extraSessions: number;
  quotaPauses: number;
}

export interface SwarmBusMessageInfo {
  id: number;
  createdAtUtc: string;
  sender: string;
  recipient: string;
  topic: string;
  body: string;
  prNumber?: number | null;
  issueNumber?: number | null;
  priority: string;
  replyToId?: number | null;
  status: 'unread' | 'acked' | string;
  ackedBy?: string | null;
}

export interface SwarmBusLockInfo {
  id: number;
  agent: string;
  pathPattern: string;
  prNumber?: number | null;
  claimedAtUtc: string;
  expiresAtUtc: string;
}

export interface SwarmBusResponse {
  generatedAtUtc: string;
  unreadCount: number;
  activeLockCount: number;
  recentMessages: SwarmBusMessageInfo[];
  activeLocks: SwarmBusLockInfo[];
  source: SourceHealth;
}

export interface DashboardSnapshot {
  generatedAtUtc: string;
  readOnly: boolean;
  bindAddress: string;
  overallStatus: 'healthy' | 'warnings' | 'degraded' | string;
  warningCount: number;
  monitors: MonitorsResponse;
  governance: GovernanceResponse;
  pipeline: PipelineResponse;
  hygiene: HygieneResponse;
  changelogVersion?: string | null;
  swarmBus?: SwarmBusResponse | null;
}

export type ThemePreference = 'system' | 'light' | 'dark';
export type DashboardTab = 'live' | 'communication' | 'architecture' | 'matrix' | 'mobadev-docs';
