import {
  createDarkTheme,
  createLightTheme,
  type BrandVariants,
  type Theme
} from '@fluentui/react-components';

const mobaBrand: BrandVariants = {
  10: '#021222',
  20: '#062644',
  30: '#0A3B66',
  40: '#0D4F88',
  50: '#0F62AA',
  60: '#106EBE',
  70: '#0078D4',
  80: '#0078D4',
  90: '#2B88D8',
  100: '#4EA0E4',
  110: '#71B6EC',
  120: '#94CAF3',
  130: '#B5DCF8',
  140: '#D4ECFC',
  150: '#EAF5FE',
  160: '#F5FAFF'
};

export const mobaLightTheme: Theme = {
  ...createLightTheme(mobaBrand),
  colorNeutralBackground1: '#FFFFFF',
  colorNeutralBackground2: '#F3F3F3',
  colorNeutralBackground3: '#EBEBEB',
  colorNeutralStroke2: '#E1E1E1',
  fontFamilyBase: "'Segoe UI Variable', 'Segoe UI', system-ui, -apple-system, sans-serif",
  fontFamilyMonospace: "'Cascadia Code', Consolas, 'Courier New', monospace"
};

export const mobaDarkTheme: Theme = {
  ...createDarkTheme(mobaBrand),
  colorNeutralBackground1: '#2C2C2C',
  colorNeutralBackground2: '#202020',
  colorNeutralBackground3: '#1F1F1F',
  colorNeutralStroke2: '#3D3D3D',
  colorBrandForeground1: '#4DA3FF',
  colorBrandForeground2: '#6FB5FF',
  fontFamilyBase: "'Segoe UI Variable', 'Segoe UI', system-ui, -apple-system, sans-serif",
  fontFamilyMonospace: "'Cascadia Code', Consolas, 'Courier New', monospace"
};

/**
 * MOBAdev Dashboard v3.7.0 — orthogonal 2-Axis Color System.
 *
 * AXIS 1 · AI IDENTITY (WHO)   -> orange (P1 Claude/Opus) · teal (P2 ChatGPT/Codex) ·
 *                                 blue (P3 Antigravity/Gemini) · pink (P4 fast delivery, +Flash glyph) ·
 *                                 purple (Decision & Steering Triad only)
 * AXIS 2 · SEMANTIC STATUS (STATE) -> green (pass/healthy/ready) · gold (= amber: warning/in-progress/pending) ·
 *                                 red (danger/blocked/error/Class H) · neutral (chrome/info/idle/unknown)
 *
 * Rules: identity tones are NEVER used for status, status tones are NEVER used for identity.
 * Fluent brand blue is reserved for interaction (tabs, links, focus, selection) — never for static badges.
 *
 * Contrast (verified for every tone x {light,dark} x {Background1,2,3}):
 *   fg text on fg@TINT_ALPHA tint >= 4.5:1, fg (solid) vs surface >= 3:1 (rings, accent bars, glyphs).
 */
export type AccentTone =
  | 'neutral'
  | 'red'
  | 'orange'
  | 'gold'
  | 'green'
  | 'teal'
  | 'blue'
  | 'purple'
  | 'pink';

export interface ToneStylePair {
  /** Text/icon color; >= 4.5:1 on its own tint over every theme surface. */
  fg: string;
  /** Translucent tint background (fg @ TINT_ALPHA). */
  bg: string;
  /** Decorative tint border (fg @ BORDER_ALPHA). Meaning is carried by text/icon, not by this border. */
  border: string;
  /** Opaque fg for rings, strokes, accent bars and arrows; >= 3:1 on every theme surface. */
  solid: string;
}

export type IdentityKey = 'p1' | 'p2' | 'p3' | 'p4' | 'triad';
export type StatusKey = 'ok' | 'warn' | 'danger' | 'neutral';

export const TINT_ALPHA = 0.12;
export const BORDER_ALPHA = 0.34;

/** Theme surfaces on which every tone is contrast-checked (Background1/2/3). */
export const THEME_SURFACES = {
  light: ['#FFFFFF', '#F3F3F3', '#EBEBEB'],
  dark: ['#2C2C2C', '#202020', '#1F1F1F']
} as const;

function hexToRgba(hex: string, alpha: number): string {
  const h = hex.replace('#', '');
  const r = parseInt(h.slice(0, 2), 16);
  const g = parseInt(h.slice(2, 4), 16);
  const b = parseInt(h.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function makeTone(fg: string): ToneStylePair {
  return {
    fg,
    solid: fg,
    bg: hexToRgba(fg, TINT_ALPHA),
    border: hexToRgba(fg, BORDER_ALPHA)
  };
}

export const ACCENT_TONES: readonly AccentTone[] = [
  'neutral',
  'red',
  'orange',
  'gold',
  'green',
  'teal',
  'blue',
  'purple',
  'pink'
] as const;

export const DARK_TONES: Record<AccentTone, ToneStylePair> = {
  neutral: makeTone('#E0E0E0'),
  red: makeTone('#FF7F8B'),
  orange: makeTone('#FF9A52'),
  gold: makeTone('#FFCB3D'),
  green: makeTone('#6CCB5F'),
  teal: makeTone('#2DD4C0'),
  blue: makeTone('#62B8FF'),
  purple: makeTone('#BA95FF'),
  pink: makeTone('#F28BDD')
};

export const LIGHT_TONES: Record<AccentTone, ToneStylePair> = {
  neutral: makeTone('#424242'),
  red: makeTone('#B10E1C'),
  orange: makeTone('#9C3C07'),
  gold: makeTone('#7F5200'),
  green: makeTone('#0C690C'),
  teal: makeTone('#00695C'),
  blue: makeTone('#0A5AAA'),
  purple: makeTone('#6B3FA0'),
  pink: makeTone('#A3238F')
};

/** Axis 1 — AI identity (WHO). P4 = pink + Flash glyph; Triad = purple, reserved for the Decision & Steering Triad. */
export const IDENTITY_TONES: Record<IdentityKey, AccentTone> = {
  p1: 'orange',
  p2: 'teal',
  p3: 'blue',
  p4: 'pink',
  triad: 'purple'
};

/** Axis 2 — semantic status (STATE). `gold` is the amber warning/in-progress tone. */
export const STATUS_TONES = {
  ok: 'green',
  warn: 'gold',
  danger: 'red',
  neutral: 'neutral'
} as const satisfies Record<StatusKey, AccentTone>;

export type StatusToneKey = (typeof STATUS_TONES)[StatusKey];

export function getToneStyles(tone: AccentTone, isDark: boolean): ToneStylePair {
  return isDark ? DARK_TONES[tone] : LIGHT_TONES[tone];
}

export function getIdentityStyles(identity: IdentityKey, isDark: boolean): ToneStylePair {
  return getToneStyles(IDENTITY_TONES[identity], isDark);
}

export function getStatusStyles(status: StatusKey, isDark: boolean): ToneStylePair {
  return getToneStyles(STATUS_TONES[status], isDark);
}
