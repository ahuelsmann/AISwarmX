import type { ReactNode } from 'react';
import { tokens } from '@fluentui/react-components';
import {
  Beaker16Regular,
  Book16Regular,
  Bot16Regular,
  Braces16Regular,
  BrainCircuit20Regular,
  Briefcase16Regular,
  Color16Regular,
  DocumentBulletList16Regular,
  Edit16Regular,
  Flash16Regular,
  Globe16Regular,
  HatGraduation16Regular,
  PeopleTeam16Regular,
  Person16Regular,
  Scales16Regular,
  ShieldCheckmark16Regular,
  Sparkle16Regular,
  WindowConsole20Regular,
  Wrench16Regular
} from '@fluentui/react-icons';
import { getToneStyles, type AccentTone } from '../theme/mobaFluentTheme';

export interface ResolvedVisual {
  icon: ReactNode;
  tone: AccentTone;
}

export function resolveModelVisual(model: string | null | undefined): ResolvedVisual {
  const normalized = (model ?? '').trim().toLowerCase();

  if (normalized.includes('orchestrator') || normalized.includes('neon')) {
    return { icon: <Beaker16Regular aria-hidden="true" />, tone: 'blue' };
  }
  if (normalized.includes('opus')) {
    return {
      icon: <BrainCircuit20Regular style={{ width: 16, height: 16 }} aria-hidden="true" />,
      tone: 'orange'
    };
  }
  if (
    normalized.includes('fable') ||
    normalized.includes('sonnet') ||
    normalized.includes('hydrogen')
  ) {
    return { icon: <Flash16Regular aria-hidden="true" />, tone: 'gold' };
  }
  if (
    normalized.includes('gpt') ||
    normalized.includes('astra') ||
    normalized.includes('codex') ||
    normalized.includes('sol')
  ) {
    return { icon: <Braces16Regular aria-hidden="true" />, tone: 'teal' };
  }
  if (
    normalized.includes('argon') ||
    normalized.includes('gemini') ||
    normalized.includes('antigravity')
  ) {
    return { icon: <Sparkle16Regular aria-hidden="true" />, tone: 'blue' };
  }

  return { icon: <Bot16Regular aria-hidden="true" />, tone: 'neutral' };
}

export function resolveRoleVisual(role: string | null | undefined): ResolvedVisual {
  const normalized = (role ?? '').trim().toLowerCase().replace(/[-_]+/g, ' ');

  if (normalized.includes('triad') || normalized.includes('council')) {
    return { icon: <PeopleTeam16Regular aria-hidden="true" />, tone: 'purple' };
  }
  if (normalized.includes('coo') || normalized.includes('chief operating')) {
    return { icon: <Briefcase16Regular aria-hidden="true" />, tone: 'orange' };
  }
  if (
    normalized.includes('technical director') ||
    normalized.includes('cpm') ||
    normalized.includes('td/cpm') ||
    normalized.includes('project manager')
  ) {
    return { icon: <Wrench16Regular aria-hidden="true" />, tone: 'teal' };
  }
  if (normalized.includes('ai strategy')) {
    return { icon: <Beaker16Regular aria-hidden="true" />, tone: 'blue' };
  }
  if (
    normalized.includes('ui/ux') ||
    normalized.includes('ui ux') ||
    normalized.includes('product experience')
  ) {
    return { icon: <Color16Regular aria-hidden="true" />, tone: 'pink' };
  }
  if (
    normalized.includes('mobanews') ||
    normalized.includes('docs') ||
    normalized.includes('editorial') ||
    normalized.includes('documentation')
  ) {
    return { icon: <Book16Regular aria-hidden="true" />, tone: 'blue' };
  }
  if (normalized.includes('final review')) {
    return { icon: <Scales16Regular aria-hidden="true" />, tone: 'red' };
  }
  if (normalized.includes('cross-family') || normalized.includes('verifier')) {
    return { icon: <ShieldCheckmark16Regular aria-hidden="true" />, tone: 'purple' };
  }
  if (normalized.includes('companion') || normalized.includes('auto-review')) {
    return { icon: <Edit16Regular aria-hidden="true" />, tone: 'teal' };
  }
  if (normalized.includes('writer') || normalized.includes('senior developer')) {
    return {
      icon: <WindowConsole20Regular style={{ width: 16, height: 16 }} aria-hidden="true" />,
      tone: 'blue'
    };
  }
  if (normalized.includes('architect')) {
    return { icon: <HatGraduation16Regular aria-hidden="true" />, tone: 'red' };
  }
  if (normalized.includes('executive assistant')) {
    return { icon: <Flash16Regular aria-hidden="true" />, tone: 'gold' };
  }
  if (normalized.includes('domain lead')) {
    return { icon: <Globe16Regular aria-hidden="true" />, tone: 'gold' };
  }
  if (normalized.includes('human') || normalized.includes('maintainer')) {
    return { icon: <Person16Regular aria-hidden="true" />, tone: 'neutral' };
  }
  if (normalized.includes('rule')) {
    return { icon: <DocumentBulletList16Regular aria-hidden="true" />, tone: 'blue' };
  }

  return { icon: <Bot16Regular aria-hidden="true" />, tone: 'neutral' };
}

export function resolveProfileTone(profile: string | null | undefined): AccentTone {
  switch ((profile ?? '').trim().toUpperCase()) {
    case 'P1':
      return 'orange';
    case 'P2':
      return 'teal';
    case 'P3':
      return 'blue';
    case 'P4':
      return 'gold';
    default:
      return 'neutral';
  }
}

interface IconGlyphBoxProps {
  icon: ReactNode;
  tone: AccentTone;
  isDark: boolean;
  size?: 'sm' | 'md';
}

export function IconGlyphBox({ icon, tone, isDark, size = 'md' }: IconGlyphBoxProps) {
  const styles = getToneStyles(tone, isDark);
  const px = size === 'sm' ? 20 : 24;

  return (
    <span
      aria-hidden="true"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: px,
        height: px,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: styles.bg,
        border: `1px solid ${styles.border}`,
        color: styles.fg,
        flexShrink: 0,
        lineHeight: 0
      }}
    >
      {icon}
    </span>
  );
}

interface RoleBadgeProps {
  role: string;
  isDark: boolean;
  label?: string;
  compact?: boolean;
}

export function RoleBadge({ role, isDark, label, compact = false }: RoleBadgeProps) {
  const visual = resolveRoleVisual(role);
  const styles = getToneStyles(visual.tone, isDark);
  const text = label ?? role;

  if (compact) {
    return (
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'flex-start',
          gap: 6,
          fontSize: tokens.fontSizeBase200,
          fontWeight: tokens.fontWeightSemibold,
          lineHeight: '20px',
          color: tokens.colorNeutralForeground1
        }}
      >
        <IconGlyphBox icon={visual.icon} tone={visual.tone} isDark={isDark} size="sm" />
        <span>{text}</span>
      </span>
    );
  }

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '2px 8px 2px 3px',
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: styles.bg,
        border: `1px solid ${styles.border}`,
        color: styles.fg,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: '18px'
      }}
    >
      <span
        aria-hidden="true"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 20,
          height: 20,
          borderRadius: tokens.borderRadiusCircular,
          backgroundColor: isDark ? 'rgba(0,0,0,0.22)' : 'rgba(255,255,255,0.65)',
          color: styles.fg,
          flexShrink: 0,
          lineHeight: 0
        }}
      >
        {visual.icon}
      </span>
      <span style={{ color: tokens.colorNeutralForeground1 }}>{text}</span>
    </span>
  );
}

interface ModelBadgeProps {
  model?: string | null;
  isDark: boolean;
  toolPrefix?: string | null;
  mono?: boolean;
  wrap?: boolean;
}

export function ModelBadge({
  model,
  isDark,
  toolPrefix,
  mono = true,
  wrap = false
}: ModelBadgeProps) {
  const displayModel = model && model.trim().length > 0 ? model : 'not detected';
  const visual = resolveModelVisual(displayModel);
  const styles = getToneStyles(visual.tone, isDark);

  return (
    <span
      className={mono ? 'mono' : undefined}
      style={{
        display: 'inline-flex',
        alignItems: wrap ? 'flex-start' : 'center',
        gap: 6,
        padding: wrap ? '4px 8px' : '3px 8px',
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: styles.bg,
        border: `1px solid ${styles.border}`,
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightMedium,
        lineHeight: wrap ? '18px' : undefined,
        maxWidth: '100%'
      }}
    >
      <span
        aria-hidden="true"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          color: styles.fg,
          flexShrink: 0,
          lineHeight: 0,
          marginTop: wrap ? 1 : 0
        }}
      >
        {visual.icon}
      </span>
      <span
        style={
          wrap
            ? { whiteSpace: 'normal', wordBreak: 'break-word' }
            : { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
        }
      >
        {toolPrefix ? `${toolPrefix} · ${displayModel}` : displayModel}
      </span>
    </span>
  );
}

interface ProfileBadgeProps {
  profile: string;
  isDark: boolean;
}

export function ProfileBadge({ profile, isDark }: ProfileBadgeProps) {
  const tone = resolveProfileTone(profile);
  const styles = getToneStyles(tone, isDark);

  return (
    <span
      className="tabular"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1px 7px',
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: styles.bg,
        border: `1px solid ${styles.border}`,
        color: styles.fg,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: '18px'
      }}
    >
      {profile}
    </span>
  );
}
