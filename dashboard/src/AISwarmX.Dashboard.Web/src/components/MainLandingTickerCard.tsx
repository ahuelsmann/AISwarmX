import { Badge, Card, Link, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircle16Regular, Merge16Regular, Open16Regular } from '@fluentui/react-icons';
import { formatRelativeTimeEn } from '../api/dashboardClient';
import type { PipelineResponse, PullRequestInfo } from '../types/dashboard';
import { ContextChip } from './shared/ContextChip';

interface MainLandingTickerCardProps {
  pipeline: PipelineResponse;
  nowMs: number;
}

export interface RankedLandingPr {
  rank: number;
  pr: PullRequestInfo;
  tier: 1 | 2 | 3 | 4 | 5 | 6;
  stageLabel: string;
  badgeColor: 'success' | 'warning' | 'subtle' | 'danger';
  detailNote: string;
}

export function rankPullRequestsForMain(pullRequests: PullRequestInfo[]): RankedLandingPr[] {
  const classified = pullRequests.map((pr) => {
    const ciPass = pr.ciStatus === 'pass';
    const sonarPassOrPending = pr.sonarStatus !== 'fail';
    const hasFailure =
      pr.ciStatus === 'fail' || pr.sonarStatus === 'fail' || pr.mergeState === 'DIRTY';

    let tier: 1 | 2 | 3 | 4 | 5 | 6;
    let stageLabel: string;
    let badgeColor: 'success' | 'warning' | 'subtle' | 'danger';

    if (pr.autoMergeEnabled && ciPass && sonarPassOrPending && pr.mergeState === 'CLEAN') {
      tier = 1;
      stageLabel = 'Auto-Merge · Ready';
      badgeColor = 'success';
    } else if (pr.autoMergeEnabled && !hasFailure) {
      tier = 2;
      stageLabel = 'Auto-Merge · Running';
      badgeColor = 'warning';
    } else if (!pr.isDraft && ciPass && sonarPassOrPending && !hasFailure) {
      tier = 3;
      stageLabel = 'Ready · Checks Green';
      badgeColor = 'success';
    } else if (pr.isDraft && ciPass && sonarPassOrPending && !hasFailure) {
      tier = 4;
      stageLabel = 'Draft · Checks Green';
      badgeColor = 'subtle';
    } else if (!hasFailure) {
      tier = 5;
      stageLabel = 'CI / Review Running';
      badgeColor = 'subtle';
    } else {
      tier = 6;
      stageLabel = 'Action Needed';
      badgeColor = 'danger';
    }

    const parts: string[] = [
      `CI: ${pr.ciStatus}`,
      `Sonar: ${pr.sonarStatus}`,
      `Merge: ${pr.mergeState}`
    ];

    return {
      pr,
      tier,
      stageLabel,
      badgeColor,
      detailNote: parts.join(' · ')
    };
  });

  classified.sort((a, b) => {
    if (a.tier !== b.tier) return a.tier - b.tier;
    if (a.pr.isDraft !== b.pr.isDraft) return a.pr.isDraft ? 1 : -1;
    const aClean = a.pr.mergeState === 'CLEAN';
    const bClean = b.pr.mergeState === 'CLEAN';
    if (aClean !== bClean) return aClean ? -1 : 1;
    const aTime = Date.parse(a.pr.updatedAtUtc) || 0;
    const bTime = Date.parse(b.pr.updatedAtUtc) || 0;
    if (aTime !== bTime) return bTime - aTime;
    return a.pr.number - b.pr.number;
  });

  return classified.map((entry, idx) => ({
    ...entry,
    rank: idx + 1
  }));
}

const useStyles = makeStyles({
  card: {
    padding: '12px 16px',
    gap: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  header: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
  },
  titleGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    flexWrap: 'wrap'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  badges: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap'
  },
  list: {
    listStyle: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
  },
  row: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    padding: '7px 10px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  rowLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    minWidth: 0,
    flex: '1 1 320px',
    flexWrap: 'wrap'
  },
  rankTag: {
    fontWeight: tokens.fontWeightBold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1
  },
  prLink: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200
  },
  rowTitle: {
    flex: '1 1 260px',
    minWidth: 0,
    maxWidth: 'min(840px, 100%)',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  rowRight: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap'
  },
  detailText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2
  },
  empty: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3
  },
  landedStrip: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px',
    paddingTop: '6px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  landedLabel: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2
  },
  landedChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '2px 8px',
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200
  },
  landedChipTitle: {
    maxWidth: 'min(420px, 65vw)',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  }
});

export function MainLandingTickerCard({ pipeline, nowMs }: MainLandingTickerCardProps) {
  const styles = useStyles();
  const ranked = rankPullRequestsForMain(pipeline.pullRequests);
  const recentlyLanded = pipeline.recentlyMerged.slice(0, 3);

  return (
    <Card className={styles.card} aria-label="Main Landing Queue">
      <div className={styles.header}>
        <div className={styles.titleGroup}>
          <Merge16Regular aria-hidden="true" />
          <h3 className={styles.title}>Main Landing Queue</h3>
          <ContextChip appearance="tint">Arrival Order</ContextChip>
        </div>
        <div className={styles.badges}>
          <Badge appearance="tint" color={ranked.length > 0 ? 'warning' : 'success'}>
            {ranked.length} Approaching main
          </Badge>
          <Badge appearance="tint" color="success">
            {pipeline.recentlyMerged.length} Landed
          </Badge>
        </div>
      </div>

      {ranked.length === 0 ? (
        <Text className={styles.empty}>Quiet window — no open PRs waiting for main.</Text>
      ) : (
        <ol className={styles.list} aria-label="Features approaching main">
          {ranked.map((item) => (
            <li key={item.pr.number} className={styles.row}>
              <div className={styles.rowLeft}>
                <span className={`${styles.rankTag} tabular`}>#{item.rank}</span>
                <Link
                  href={item.pr.url}
                  target="_blank"
                  rel="noreferrer"
                  className={styles.prLink}
                >
                  <span>PR #{item.pr.number}</span>
                  <Open16Regular aria-hidden="true" />
                </Link>
                <span className={styles.rowTitle} title={item.pr.title}>
                  {item.pr.title}
                </span>
                {item.pr.linkedIssues.map((issue) => (
                  <Badge key={issue.number} appearance="outline" color="subtle">
                    #{issue.number}
                  </Badge>
                ))}
              </div>
              <div className={styles.rowRight}>
                <Text className={`${styles.detailText} tabular`}>{item.detailNote}</Text>
                <Badge appearance="tint" color={item.badgeColor}>
                  {item.stageLabel}
                </Badge>
              </div>
            </li>
          ))}
        </ol>
      )}

      {recentlyLanded.length > 0 && (
        <div className={styles.landedStrip} aria-label="Just landed on main">
          <span className={styles.landedLabel}>
            <CheckmarkCircle16Regular aria-hidden="true" />
            <span>Just landed on main:</span>
          </span>
          {recentlyLanded.map((merged) => (
            <Link
              key={merged.number}
              href={merged.url}
              target="_blank"
              rel="noreferrer"
              className={styles.landedChip}
              title={merged.title}
            >
              <strong>PR #{merged.number}</strong>
              <span className={styles.landedChipTitle}>{merged.title}</span>
              <span className="tabular">
                ({formatRelativeTimeEn(merged.mergedAtUtc ?? merged.updatedAtUtc, nowMs)})
              </span>
            </Link>
          ))}
        </div>
      )}
    </Card>
  );
}
