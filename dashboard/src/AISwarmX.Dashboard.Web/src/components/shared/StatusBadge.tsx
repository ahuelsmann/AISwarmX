import type { ReactNode } from 'react';
import { Badge, type BadgeProps } from '@fluentui/react-components';
import type { StatusKey } from '../../theme/mobaFluentTheme';

const STATUS_BADGE_COLORS: Record<StatusKey, 'success' | 'warning' | 'danger' | 'subtle'> = {
  ok: 'success',
  warn: 'warning',
  danger: 'danger',
  neutral: 'subtle'
};

export interface StatusBadgeProps {
  tone: StatusKey;
  children: ReactNode;
  icon?: BadgeProps['icon'];
  appearance?: 'tint' | 'outline';
  size?: 'tiny' | 'extra-small' | 'small' | 'medium' | 'large' | 'extra-large';
  className?: string;
  title?: string;
}

/**
 * Axis-2 semantic status badge mapping strictly to success / warning / danger / subtle
 * so Fluent brand blue is never overloaded for static status pills.
 */
export function StatusBadge({
  tone,
  children,
  icon,
  appearance = 'tint',
  size = 'medium',
  className,
  title
}: StatusBadgeProps) {
  return (
    <Badge
      appearance={appearance}
      color={STATUS_BADGE_COLORS[tone]}
      shape="rounded"
      size={size}
      icon={icon}
      className={className}
      title={title}
    >
      {children}
    </Badge>
  );
}
