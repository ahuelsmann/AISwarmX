import type { ReactNode } from 'react';
import {
  Badge,
  Divider,
  Link,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  makeStyles,
  tokens
} from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: '10px',
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300
  },
  frontmatterCard: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '8px',
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  frontmatterItem: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: tokens.fontSizeBase200
  },
  h1: {
    margin: '6px 0 2px',
    fontSize: tokens.fontSizeBase600,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase600
  },
  h2: {
    margin: '10px 0 2px',
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    paddingBottom: '4px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`
  },
  h3: {
    margin: '8px 0 2px',
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold
  },
  h4: {
    margin: '6px 0 2px',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold
  },
  paragraph: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1
  },
  list: {
    margin: 0,
    paddingLeft: '22px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    fontSize: tokens.fontSizeBase300
  },
  blockquote: {
    margin: 0,
    padding: '8px 12px',
    borderLeft: `3px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: `0 ${tokens.borderRadiusMedium} ${tokens.borderRadiusMedium} 0`,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300
  },
  codeBlock: {
    margin: 0,
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    overflowX: 'auto'
  },
  inlineCode: {
    padding: '1px 5px',
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground3,
    fontSize: '0.9em'
  },
  tableWrap: {
    overflowX: 'auto',
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  }
});

interface MarkdownReaderProps {
  content: string;
}

interface FrontmatterEntry {
  key: string;
  value: string;
}

function splitFrontmatter(raw: string): { meta: FrontmatterEntry[]; body: string } {
  const normalized = raw.replace(/\r\n/g, '\n');
  if (!normalized.startsWith('---\n')) {
    return { meta: [], body: normalized };
  }

  const endIdx = normalized.indexOf('\n---\n', 4);
  if (endIdx < 0) {
    return { meta: [], body: normalized };
  }

  const yamlLines = normalized.slice(4, endIdx).split('\n');
  const meta: FrontmatterEntry[] = [];
  for (const line of yamlLines) {
    const colon = line.indexOf(':');
    if (colon <= 0) {
      continue;
    }

    const key = line.slice(0, colon).trim();
    const value = line.slice(colon + 1).trim().replace(/^['"]|['"]$/g, '');
    if (key && value) {
      meta.push({ key, value });
    }
  }

  return { meta, body: normalized.slice(endIdx + 5) };
}

function renderInline(text: string, inlineCodeClass: string): ReactNode[] {
  const tokensOut: ReactNode[] = [];
  const pattern = /(\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*]+)\*\*|\*([^*]+)\*)/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  let idx = 0;

  while ((match = pattern.exec(text)) !== null) {
    if (match.index > lastIndex) {
      tokensOut.push(text.slice(lastIndex, match.index));
    }

    if (match[2] !== undefined && match[3] !== undefined) {
      const label = match[2];
      const href = match[3].trim();
      if (/^https?:\/\//i.test(href)) {
        tokensOut.push(
          <Link key={`lnk-${idx}`} href={href} target="_blank" rel="noreferrer">
            {label}
          </Link>
        );
      } else {
        tokensOut.push(
          <code key={`rel-${idx}`} className={`${inlineCodeClass} mono`} title={href}>
            {label}
          </code>
        );
      }
    } else if (match[4] !== undefined) {
      tokensOut.push(
        <code key={`code-${idx}`} className={`${inlineCodeClass} mono`}>
          {match[4]}
        </code>
      );
    } else if (match[5] !== undefined) {
      tokensOut.push(<strong key={`b-${idx}`}>{match[5]}</strong>);
    } else if (match[6] !== undefined) {
      tokensOut.push(<em key={`i-${idx}`}>{match[6]}</em>);
    }

    idx++;
    lastIndex = pattern.lastIndex;
  }

  if (lastIndex < text.length) {
    tokensOut.push(text.slice(lastIndex));
  }

  return tokensOut;
}

function parseTableCells(line: string): string[] {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((c) => c.trim());
}

function isSeparatorRow(line: string): boolean {
  const cells = parseTableCells(line);
  return cells.length > 0 && cells.every((c) => /^:?-{2,}:?$/.test(c));
}

export function MarkdownReader({ content }: MarkdownReaderProps) {
  const styles = useStyles();
  const { meta, body } = splitFrontmatter(content);
  const lines = body.split('\n');

  const blocks: ReactNode[] = [];
  let i = 0;
  let keyCounter = 0;

  while (i < lines.length) {
    const line = lines[i];
    const trimmed = line.trim();

    if (!trimmed) {
      i++;
      continue;
    }

    // Fenced code block
    if (trimmed.startsWith('```')) {
      const lang = trimmed.slice(3).trim();
      const codeLines: string[] = [];
      i++;
      while (i < lines.length && !lines[i].trim().startsWith('```')) {
        codeLines.push(lines[i]);
        i++;
      }
      if (i < lines.length) {
        i++;
      }
      blocks.push(
        <pre key={`pre-${keyCounter++}`} className={`${styles.codeBlock} mono`} aria-label={lang || 'Code block'}>
          <code>{codeLines.join('\n')}</code>
        </pre>
      );
      continue;
    }

    // Horizontal rule
    if (/^(-{3,}|\*{3,})$/.test(trimmed)) {
      blocks.push(<Divider key={`hr-${keyCounter++}`} />);
      i++;
      continue;
    }

    // Headings
    const headingMatch = /^(#{1,4})\s+(.+)$/.exec(trimmed);
    if (headingMatch) {
      const level = headingMatch[1].length;
      const text = headingMatch[2];
      const rendered = renderInline(text, styles.inlineCode);
      if (level === 1) {
        blocks.push(<h1 key={`h-${keyCounter++}`} className={styles.h1}>{rendered}</h1>);
      } else if (level === 2) {
        blocks.push(<h2 key={`h-${keyCounter++}`} className={styles.h2}>{rendered}</h2>);
      } else if (level === 3) {
        blocks.push(<h3 key={`h-${keyCounter++}`} className={styles.h3}>{rendered}</h3>);
      } else {
        blocks.push(<h4 key={`h-${keyCounter++}`} className={styles.h4}>{rendered}</h4>);
      }
      i++;
      continue;
    }

    // GFM table
    if (trimmed.startsWith('|') && i + 1 < lines.length && isSeparatorRow(lines[i + 1].trim())) {
      const headers = parseTableCells(trimmed);
      i += 2;
      const rows: string[][] = [];
      while (i < lines.length && lines[i].trim().startsWith('|')) {
        rows.push(parseTableCells(lines[i].trim()));
        i++;
      }

      blocks.push(
        <div key={`tbl-${keyCounter++}`} className={styles.tableWrap}>
          <Table size="small">
            <TableHeader>
              <TableRow>
                {headers.map((h, hIdx) => (
                  <TableHeaderCell key={`th-${hIdx}`}>
                    {renderInline(h, styles.inlineCode)}
                  </TableHeaderCell>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((row, rIdx) => (
                <TableRow key={`tr-${rIdx}`}>
                  {headers.map((_, cIdx) => (
                    <TableCell key={`td-${rIdx}-${cIdx}`}>
                      {renderInline(row[cIdx] ?? '', styles.inlineCode)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      );
      continue;
    }

    // Blockquote
    if (trimmed.startsWith('>')) {
      const quoteLines: string[] = [];
      while (i < lines.length && lines[i].trim().startsWith('>')) {
        quoteLines.push(lines[i].trim().replace(/^>\s?/, ''));
        i++;
      }
      blocks.push(
        <blockquote key={`bq-${keyCounter++}`} className={styles.blockquote}>
          {renderInline(quoteLines.join(' '), styles.inlineCode)}
        </blockquote>
      );
      continue;
    }

    // Unordered list
    if (/^[-*]\s+/.test(trimmed)) {
      const items: string[] = [];
      while (i < lines.length && /^[-*]\s+/.test(lines[i].trim())) {
        items.push(lines[i].trim().replace(/^[-*]\s+/, ''));
        i++;
      }
      blocks.push(
        <ul key={`ul-${keyCounter++}`} className={styles.list}>
          {items.map((item, idx) => (
            <li key={`li-${idx}`}>{renderInline(item, styles.inlineCode)}</li>
          ))}
        </ul>
      );
      continue;
    }

    // Ordered list
    if (/^\d+\.\s+/.test(trimmed)) {
      const items: string[] = [];
      while (i < lines.length && /^\d+\.\s+/.test(lines[i].trim())) {
        items.push(lines[i].trim().replace(/^\d+\.\s+/, ''));
        i++;
      }
      blocks.push(
        <ol key={`ol-${keyCounter++}`} className={styles.list}>
          {items.map((item, idx) => (
            <li key={`oli-${idx}`}>{renderInline(item, styles.inlineCode)}</li>
          ))}
        </ol>
      );
      continue;
    }

    // Paragraph
    const paraLines: string[] = [];
    while (
      i < lines.length &&
      lines[i].trim() !== '' &&
      !lines[i].trim().startsWith('```') &&
      !/^#{1,4}\s+/.test(lines[i].trim()) &&
      !/^[-*]\s+/.test(lines[i].trim()) &&
      !/^\d+\.\s+/.test(lines[i].trim()) &&
      !lines[i].trim().startsWith('>') &&
      !/^(-{3,}|\*{3,})$/.test(lines[i].trim()) &&
      !(lines[i].trim().startsWith('|') && i + 1 < lines.length && isSeparatorRow(lines[i + 1].trim()))
    ) {
      paraLines.push(lines[i].trim());
      i++;
    }

    blocks.push(
      <p key={`p-${keyCounter++}`} className={styles.paragraph}>
        {renderInline(paraLines.join(' '), styles.inlineCode)}
      </p>
    );
  }

  return (
    <div className={styles.root}>
      {meta.length > 0 && (
        <div className={styles.frontmatterCard} aria-label="Document metadata">
          {meta.map((entry) => (
            <div key={entry.key} className={styles.frontmatterItem}>
              <Badge appearance="tint" color="brand" shape="rounded" size="small">
                {entry.key}
              </Badge>
              <Text size={200}>{entry.value}</Text>
            </div>
          ))}
        </div>
      )}

      {blocks}
    </div>
  );
}
