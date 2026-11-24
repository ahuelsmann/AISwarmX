/// <reference types="vite/client" />
import { describe, expect, it } from 'vitest';
import {
  resolveModelVisual,
  resolveProfileTone,
  resolveRoleVisual,
  resolveToolTone
} from '../components/RoleAndModelIcons';
import {
  ACCENT_TONES,
  DARK_TONES,
  IDENTITY_TONES,
  LIGHT_TONES,
  STATUS_TONES,
  THEME_SURFACES,
  getToneStyles,
  type AccentTone
} from '../theme/mobaFluentTheme';

type Rgb = [number, number, number];

function parseHex(hex: string): Rgb {
  const clean = hex.replace('#', '');
  return [
    parseInt(clean.slice(0, 2), 16),
    parseInt(clean.slice(2, 4), 16),
    parseInt(clean.slice(4, 6), 16)
  ];
}

function parseRgbaOrHexOverSurface(color: string, surfaceHex: string): Rgb {
  if (color.startsWith('#')) {
    return parseHex(color);
  }
  const match = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)/i.exec(color);
  if (!match) {
    throw new Error(`Unsupported color format in theme token: ${color}`);
  }
  const fg: Rgb = [Number(match[1]), Number(match[2]), Number(match[3])];
  const alpha = match[4] !== undefined ? Number(match[4]) : 1;
  const bg = parseHex(surfaceHex);
  return [
    fg[0] * alpha + bg[0] * (1 - alpha),
    fg[1] * alpha + bg[1] * (1 - alpha),
    fg[2] * alpha + bg[2] * (1 - alpha)
  ];
}

function toLinear(channel255: number): number {
  const srgb = channel255 / 255;
  return srgb <= 0.03928 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance([r, g, b]: Rgb): number {
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
}

function contrastRatio(rgbA: Rgb, rgbB: Rgb): number {
  const lumA = relativeLuminance(rgbA);
  const lumB = relativeLuminance(rgbB);
  const bright = Math.max(lumA, lumB);
  const dark = Math.min(lumA, lumB);
  return (bright + 0.05) / (dark + 0.05);
}

describe('MOBAdev 2-Axis Color System & Accessibility Gates', () => {
  it('meets WCAG AA text (>= 4.5:1) and UI (>= 3.0:1) contrast across all Light & Dark surfaces', () => {
    const failures: string[] = [];

    for (const isDark of [false, true]) {
      const themeName = isDark ? 'dark' : 'light';
      const surfaces = isDark ? THEME_SURFACES.dark : THEME_SURFACES.light;

      for (const tone of ACCENT_TONES) {
        const pair = getToneStyles(tone, isDark);
        expect(Boolean(DARK_TONES[tone] && LIGHT_TONES[tone])).toBe(true);

        for (const surface of surfaces) {
          const compositedTint = parseRgbaOrHexOverSurface(pair.bg, surface);
          const fgRgb = parseHex(pair.fg);
          const solidRgb = parseHex(pair.solid);
          const surfaceRgb = parseHex(surface);

          const textContrast = contrastRatio(fgRgb, compositedTint);
          if (textContrast < 4.5) {
            failures.push(
              `${themeName}/${tone} on ${surface} tint: ${textContrast.toFixed(2)}:1 < 4.5:1`
            );
          }

          const uiContrast = contrastRatio(solidRgb, surfaceRgb);
          if (uiContrast < 3.0) {
            failures.push(
              `${themeName}/${tone} solid on ${surface}: ${uiContrast.toFixed(2)}:1 < 3.0:1`
            );
          }
        }
      }
    }

    expect(failures).toEqual([]);
  });

  it('keeps Axis 1 (AI Identity) and Axis 2 (Semantic Status) strictly orthogonal with no collisions', () => {
    const identityValues = new Set<AccentTone>(Object.values(IDENTITY_TONES));
    const statusValues = new Set<AccentTone>(Object.values(STATUS_TONES));
    for (const tone of identityValues) {
      expect(statusValues.has(tone)).toBe(false);
    }

    // Dynamic task roles stay strictly neutral (never red/gold/green/blue)
    const neutralRoles = [
      'Final Reviewer',
      'Solution Architect',
      'Writer',
      'Senior Developer',
      'Companion Reviewer',
      'Cross-Family Verifier (TD/CPM)',
      'UI/UX Specialist',
      'Executive Assistant',
      'Domain Lead',
      'MOBAnews Editorial',
      'Rule',
      'Human (maintainer)'
    ];
    for (const role of neutralRoles) {
      expect(resolveRoleVisual(role).tone).toBe('neutral');
    }

    // Standing Triad council roles preserve exact Axis-1 identity colors
    expect(resolveRoleVisual('Decision & Steering Triad').tone).toBe('purple');
    expect(resolveRoleVisual('Chief Operating Officer (COO)').tone).toBe('orange');
    expect(resolveRoleVisual('Technical Director & CPM').tone).toBe('teal');
    expect(resolveRoleVisual('Head of AI Strategy').tone).toBe('blue');

    // P4 fast-delivery models resolve to pink (never gold/amber)
    expect(resolveProfileTone('P4')).toBe('pink');
    expect(resolveModelVisual('Claude Sonnet 5.5').tone).toBe('pink');
    expect(resolveModelVisual('Claude Haiku 4.5').tone).toBe('pink');
    expect(resolveModelVisual('Gemini 4 Hydrogen').tone).toBe('pink');
    expect(resolveModelVisual('GPT-6.2-astra').tone).toBe('teal');

    // Tool identity families
    expect(resolveToolTone('ChatGPT Codex')).toBe('teal');
    expect(resolveToolTone('Claude Code')).toBe('orange');
    expect(resolveToolTone('Antigravity')).toBe('blue');

    // `sol` word-boundary guard prevents false positives like console/resolver
    expect(resolveModelVisual('gpt-6.2-sol').tone).toBe('teal');
    expect(resolveModelVisual('Sol 6.2').tone).toBe('teal');
    expect(resolveModelVisual('console-tool').tone).toBe('neutral');
    expect(resolveModelVisual('resolver').tone).toBe('neutral');
  });

  it('forbids hardcoded hex/rgba colors and static brand-colored badges inside components', () => {
    const sources = import.meta.glob('../components/**/*.tsx', {
      query: '?raw',
      import: 'default',
      eager: true
    }) as Record<string, string>;

    const fileEntries = Object.entries(sources);
    expect(fileEntries.length).toBeGreaterThan(5);

    const violations: string[] = [];
    for (const [filePath, source] of fileEntries) {
      if (/#[0-9a-fA-F]{3,8}\b/.test(source)) {
        violations.push(`${filePath}: contains hardcoded hex color`);
      }
      if (/rgba?\s*\(/i.test(source)) {
        violations.push(`${filePath}: contains hardcoded rgb()/rgba() color`);
      }
      if (/['"]brand['"]/.test(source)) {
        violations.push(`${filePath}: contains static 'brand' badge color literal`);
      }
    }

    expect(violations).toEqual([]);
  });
});
