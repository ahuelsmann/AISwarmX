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

export type AccentTone =
  | 'neutral'
  | 'red'
  | 'orange'
  | 'gold'
  | 'green'
  | 'blue'
  | 'purple'
  | 'pink';

export interface ToneStylePair {
  fg: string;
  bg: string;
  border: string;
}

const DARK_TONES: Record<AccentTone, ToneStylePair> = {
  neutral: {
    fg: '#EAEAEA',
    bg: 'rgba(234, 234, 234, 0.12)',
    border: 'rgba(234, 234, 234, 0.28)'
  },
  red: {
    fg: '#FF6B6B',
    bg: 'rgba(255, 107, 107, 0.14)',
    border: 'rgba(255, 107, 107, 0.34)'
  },
  orange: {
    fg: '#FF8C42',
    bg: 'rgba(255, 140, 66, 0.14)',
    border: 'rgba(255, 140, 66, 0.34)'
  },
  gold: {
    fg: '#FFD147',
    bg: 'rgba(255, 209, 71, 0.14)',
    border: 'rgba(255, 209, 71, 0.34)'
  },
  green: {
    fg: '#3FD67E',
    bg: 'rgba(63, 214, 126, 0.14)',
    border: 'rgba(63, 214, 126, 0.34)'
  },
  blue: {
    fg: '#4DA3FF',
    bg: 'rgba(77, 163, 255, 0.14)',
    border: 'rgba(77, 163, 255, 0.34)'
  },
  purple: {
    fg: '#B085FF',
    bg: 'rgba(176, 133, 255, 0.14)',
    border: 'rgba(176, 133, 255, 0.34)'
  },
  pink: {
    fg: '#FF7EB6',
    bg: 'rgba(255, 126, 182, 0.14)',
    border: 'rgba(255, 126, 182, 0.34)'
  }
};

const LIGHT_TONES: Record<AccentTone, ToneStylePair> = {
  neutral: {
    fg: '#424242',
    bg: 'rgba(66, 66, 66, 0.08)',
    border: 'rgba(66, 66, 66, 0.22)'
  },
  red: {
    fg: '#C42B1C',
    bg: 'rgba(196, 43, 28, 0.09)',
    border: 'rgba(196, 43, 28, 0.26)'
  },
  orange: {
    fg: '#B84B08',
    bg: 'rgba(184, 75, 8, 0.09)',
    border: 'rgba(184, 75, 8, 0.26)'
  },
  gold: {
    fg: '#8F5F00',
    bg: 'rgba(143, 95, 0, 0.10)',
    border: 'rgba(143, 95, 0, 0.28)'
  },
  green: {
    fg: '#107C41',
    bg: 'rgba(16, 124, 65, 0.09)',
    border: 'rgba(16, 124, 65, 0.26)'
  },
  blue: {
    fg: '#0067B8',
    bg: 'rgba(0, 103, 184, 0.09)',
    border: 'rgba(0, 103, 184, 0.26)'
  },
  purple: {
    fg: '#6B3FA0',
    bg: 'rgba(107, 63, 160, 0.09)',
    border: 'rgba(107, 63, 160, 0.26)'
  },
  pink: {
    fg: '#B83270',
    bg: 'rgba(184, 50, 112, 0.09)',
    border: 'rgba(184, 50, 112, 0.26)'
  }
};

export function getToneStyles(tone: AccentTone, isDark: boolean): ToneStylePair {
  return isDark ? DARK_TONES[tone] : LIGHT_TONES[tone];
}
