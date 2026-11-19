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
}

export interface MonitorsResponse {
  generatedAtUtc: string;
  monitors: MonitorInfo[];
  source: SourceHealth;
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

export interface TaskParticipantInfo {
  agent: string;
  tool: string;
  role: string;
  profile: string;
  model: string;
  status: string;
  evidenceSource: string;
}

export interface PullRequestInfo {
  number: number;
  title: string;
  url: string;
  branch: string;
  state: 'OPEN' | 'MERGED' | string;
  stateLabel: 'Draft' | 'Ready' | 'Auto-Merge' | 'Gemergt' | string;
  isDraft: boolean;
  autoMergeEnabled: boolean;
  mergeState: 'CLEAN' | 'BEHIND' | 'DIRTY' | 'BLOCKED' | 'MERGED' | string;
  ciStatus: 'pass' | 'pending' | 'fail' | 'none' | string;
  sonarStatus: 'pass' | 'pending' | 'fail' | 'none' | string;
  riskClass: 'H' | 'M' | 'L' | '?' | string;
  lockedAreas: string[];
  updatedAtUtc: string;
  mergedAtUtc?: string | null;
  projectStatus?: string | null;
  participants: TaskParticipantInfo[];
}

export interface PipelineResponse {
  generatedAtUtc: string;
  repository: string;
  pullRequests: PullRequestInfo[];
  recentlyMerged: PullRequestInfo[];
  projectBoardAvailable: boolean;
  projectBoardNotice?: string | null;
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
}

export interface HygieneResponse {
  generatedAtUtc: string;
  sessions: ToolSessionHygiene[];
  worktrees: WorktreeInfo[];
  staleWorktreeCount: number;
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
}

export type ThemePreference = 'system' | 'light' | 'dark';
export type DashboardTab = 'live' | 'matrix';
