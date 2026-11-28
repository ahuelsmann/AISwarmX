import type { ReactNode } from 'react';
import { Badge, type BadgeProps } from '@fluentui/react-components';

export interface ContextChipProps {
  children: ReactNode;
  icon?: BadgeProps['icon'];
  mono?: boolean;
  appearance?: 'outline' | 'tint';
  size?: 'tiny' | 'extra-small' | 'small' | 'medium' | 'large' | 'extra-large';
  className?: string;
  title?: string;
}

/**
 * Neutral chrome/context tag (repository, read-only bind mode, board column, PR kind)
 * kept strictly on neutral/subtle tones instead of Fluent brand blue.
 */
export function ContextChip({
  children,
  icon,
  mono = false,
  appearance = 'outline',
  size = 'medium',
  className,
  title
}: ContextChipProps) {
  const combinedClass = [mono ? 'mono' : '', className ?? ''].filter(Boolean).join(' ') || undefined;

  return (
    <Badge
      appearance={appearance}
      color="subtle"
      shape="rounded"
      size={size}
      icon={icon}
      className={combinedClass}
      title={title}
    >
      {children}
    </Badge>
  );
}
