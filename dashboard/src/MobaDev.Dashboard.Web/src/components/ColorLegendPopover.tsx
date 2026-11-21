import { useState } from 'react';
import {
  Button,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Text,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import { Color16Regular, Flash12Regular } from '@fluentui/react-icons';
import {
  getIdentityStyles,
  getStatusStyles,
  type IdentityKey,
  type StatusKey
} from '../theme/mobaFluentTheme';

const useStyles = makeStyles({
  surface: {
    maxWidth: '348px',
    padding: '12px 14px'
  },
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: '12px'
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px'
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px',
    paddingTop: '8px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  sectionTitle: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.03em'
  },
  rows: {
    display: 'flex',
    flexDirection: 'column',
    gap: '5px'
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1
  },
  identityChip: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '2px',
    minWidth: '46px',
    padding: '1px 7px',
    borderRadius: tokens.borderRadiusCircular,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: '16px',
    flexShrink: 0
  },
  statusSwatch: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '20px',
    height: '20px',
    borderRadius: tokens.borderRadiusCircular,
    flexShrink: 0
  },
  statusDot: {
    width: '8px',
    height: '8px',
    borderRadius: tokens.borderRadiusCircular,
    display: 'inline-block'
  },
  symbolBox: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '46px',
    height: '20px',
    padding: '0 6px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0
  }
});

interface IdentityLegendEntry {
  key: IdentityKey;
  tag: string;
  label: string;
  hasFlash?: boolean;
}

interface StatusLegendEntry {
  key: StatusKey;
  label: string;
}

const IDENTITY_ENTRIES: readonly IdentityLegendEntry[] = [
  { key: 'p2', tag: 'P2', label: 'ChatGPT / Codex (Build & Verify)' },
  { key: 'p1', tag: 'P1', label: 'Claude / Opus (Steering & Final Review)' },
  { key: 'p3', tag: 'P3', label: 'Antigravity / Gemini (Strategy & UI/UX)' },
  { key: 'p4', tag: 'P4', label: 'Fast Delivery (Docs & Quick Tasks)', hasFlash: true },
  { key: 'triad', tag: 'Triad', label: 'Decision & Steering Triad' }
];

const STATUS_ENTRIES: readonly StatusLegendEntry[] = [
  { key: 'ok', label: 'Pass / Healthy / Ready' },
  { key: 'warn', label: 'Active / Pending / Attention' },
  { key: 'danger', label: 'Blocked / Error / Class H' },
  { key: 'neutral', label: 'Context / Idle / Unknown' }
];

interface ColorLegendPopoverProps {
  isDark: boolean;
}

export function ColorLegendPopover({ isDark }: ColorLegendPopoverProps) {
  const styles = useStyles();
  const [open, setOpen] = useState(false);

  const identitySample = getIdentityStyles('p2', isDark);
  const statusSample = getStatusStyles('ok', isDark);

  return (
    <Popover
      open={open}
      onOpenChange={(_, data) => setOpen(data.open)}
      withArrow
      positioning="below-end"
    >
      <PopoverTrigger disableButtonEnhancement>
        <Button
          size="small"
          appearance="subtle"
          icon={<Color16Regular aria-hidden="true" />}
          aria-label="Open color and symbol legend"
        >
          Legend
        </Button>
      </PopoverTrigger>

      <PopoverSurface className={styles.surface}>
        <div className={styles.root} role="region" aria-label="Color and symbol legend">
          <div className={styles.header}>
            <Text weight="semibold" size={300}>
              Color &amp; Symbol Legend
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
              Two separate axes: Identity shows who works; Status shows current state.
            </Text>
          </div>

          <div className={styles.section}>
            <span className={styles.sectionTitle}>Axis 1 · AI Identity (Who)</span>
            <div className={styles.rows}>
              {IDENTITY_ENTRIES.map((entry) => {
                const toneStyles = getIdentityStyles(entry.key, isDark);
                return (
                  <div key={entry.key} className={styles.row}>
                    <span
                      className={`${styles.identityChip} tabular`}
                      style={{
                        backgroundColor: toneStyles.bg,
                        border: `1px solid ${toneStyles.border}`,
                        color: toneStyles.fg
                      }}
                    >
                      {entry.hasFlash && <Flash12Regular aria-hidden="true" />}
                      {entry.tag}
                    </span>
                    <span>{entry.label}</span>
                  </div>
                );
              })}
            </div>
          </div>

          <div className={styles.section}>
            <span className={styles.sectionTitle}>Axis 2 · Status (State)</span>
            <div className={styles.rows}>
              {STATUS_ENTRIES.map((entry) => {
                const toneStyles = getStatusStyles(entry.key, isDark);
                return (
                  <div key={entry.key} className={styles.row}>
                    <span
                      className={styles.statusSwatch}
                      aria-hidden="true"
                      style={{
                        backgroundColor: toneStyles.bg,
                        border: `1px solid ${toneStyles.border}`
                      }}
                    >
                      <span
                        className={styles.statusDot}
                        style={{ backgroundColor: toneStyles.solid }}
                      />
                    </span>
                    <span>{entry.label}</span>
                  </div>
                );
              })}
            </div>
          </div>

          <div className={styles.section}>
            <span className={styles.sectionTitle}>Round Table Key</span>
            <div className={styles.rows}>
              <div className={styles.row}>
                <span className={`${styles.symbolBox} tabular`} aria-hidden="true">
                  1→2→3
                </span>
                <span>Numbered solid arcs = workflow handoff</span>
              </div>
              <div className={styles.row}>
                <span className={styles.symbolBox} aria-hidden="true">
                  - - -
                </span>
                <span>Dashed spoke = steering / advisory</span>
              </div>
              <div className={styles.row}>
                <span
                  className={styles.symbolBox}
                  aria-hidden="true"
                  style={{
                    borderLeft: `4px solid ${identitySample.solid}`,
                    color: identitySample.fg
                  }}
                >
                  WHO
                </span>
                <span>Seat circle + left card bar = AI identity</span>
              </div>
              <div className={styles.row}>
                <span
                  className={styles.symbolBox}
                  aria-hidden="true"
                  style={{
                    backgroundColor: statusSample.bg,
                    borderColor: statusSample.border,
                    color: statusSample.fg
                  }}
                >
                  ●
                </span>
                <span>Top-right dot + card pill = live status</span>
              </div>
            </div>
          </div>
        </div>
      </PopoverSurface>
    </Popover>
  );
}
