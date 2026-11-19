import { useEffect, useMemo, useState } from 'react';
import {
  Badge,
  Button,
  Card,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  ToggleButton,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  Code16Regular,
  DocumentText16Regular,
  Eye16Regular,
  FolderOpen16Regular,
  Search16Regular
} from '@fluentui/react-icons';
import {
  fetchConfigDocContent,
  fetchConfigDocsCatalog,
  formatAbsoluteEn
} from '../api/dashboardClient';
import type {
  ConfigDocContentResponse,
  ConfigDocItem,
  ConfigDocsCatalogResponse,
  ConfigDocsScope
} from '../types/dashboard';
import { MarkdownReader } from './MarkdownReader';
import { RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  layout: {
    display: 'grid',
    gridTemplateColumns: 'minmax(240px, 310px) minmax(0, 1fr)',
    gap: '14px',
    alignItems: 'start',
    '@media (max-width: 959px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  sidebarCard: {
    padding: '12px',
    gap: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    maxHeight: '78vh',
    overflowY: 'auto'
  },
  contentCard: {
    padding: '16px',
    gap: '12px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    minHeight: '420px'
  },
  sidebarHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap'
  },
  groupTitle: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    marginTop: '6px'
  },
  fileList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px'
  },
  fileButton: {
    justifyContent: 'flex-start',
    width: '100%',
    padding: '6px 8px',
    height: 'auto'
  },
  docHeader: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px',
    paddingBottom: '10px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`
  },
  docTitleGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  viewToggleGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '2px',
    padding: '2px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  rawPre: {
    margin: 0,
    padding: '12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    overflowX: 'auto',
    whiteSpace: 'pre-wrap'
  },
  pathPill: {
    display: 'inline-block',
    padding: '2px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: '17px',
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'normal',
    wordBreak: 'break-word',
    maxWidth: '100%'
  }
});

interface ConfigDocsViewProps {
  scope: ConfigDocsScope;
  isDark: boolean;
  requestedPath?: string | null;
  onConsumeRequestedPath?: () => void;
}

export function ConfigDocsView({
  scope,
  isDark,
  requestedPath,
  onConsumeRequestedPath
}: ConfigDocsViewProps) {
  const styles = useStyles();
  const [catalog, setCatalog] = useState<ConfigDocsCatalogResponse | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [loadingCatalog, setLoadingCatalog] = useState(true);
  const [filterText, setFilterText] = useState('');

  const [selectedPath, setSelectedPath] = useState<string | null>(requestedPath ?? null);
  const [contentCache, setContentCache] = useState<Record<string, ConfigDocContentResponse>>({});
  const [loadingPath, setLoadingPath] = useState<string | null>(null);
  const [contentError, setContentError] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<'formatted' | 'raw'>('formatted');

  useEffect(() => {
    const controller = new AbortController();
    setLoadingCatalog(true);
    setCatalogError(null);

    fetchConfigDocsCatalog(scope, controller.signal)
      .then((res) => {
        setCatalog(res);
        setLoadingCatalog(false);
        if (res.files.length > 0) {
          const initial =
            requestedPath && res.files.some((f) => f.relativePath === requestedPath)
              ? requestedPath
              : res.files[0].relativePath;
          setSelectedPath((prev) => prev ?? initial);
        }
      })
      .catch((err) => {
        if (controller.signal.aborted) {
          return;
        }
        setCatalogError(err instanceof Error ? err.message : 'Unable to load catalog.');
        setLoadingCatalog(false);
      });

    return () => controller.abort();
  }, [scope]);

  useEffect(() => {
    if (!requestedPath || !catalog) {
      return;
    }

    const found = catalog.files.find((f) => f.relativePath === requestedPath);
    if (found) {
      setSelectedPath(found.relativePath);
    }
    onConsumeRequestedPath?.();
  }, [requestedPath, catalog, onConsumeRequestedPath]);

  useEffect(() => {
    if (!selectedPath || contentCache[selectedPath]) {
      return;
    }

    const controller = new AbortController();
    setLoadingPath(selectedPath);
    setContentError(null);

    fetchConfigDocContent(scope, selectedPath, controller.signal)
      .then((doc) => {
        setContentCache((prev) => ({ ...prev, [selectedPath]: doc }));
        setLoadingPath(null);
      })
      .catch((err) => {
        if (controller.signal.aborted) {
          return;
        }
        setContentError(err instanceof Error ? err.message : 'Unable to load document.');
        setLoadingPath(null);
      });

    return () => controller.abort();
  }, [scope, selectedPath, contentCache]);

  const groupedFiles = useMemo(() => {
    const query = filterText.trim().toLowerCase();
    const files = (catalog?.files ?? []).filter(
      (f) =>
        !query ||
        f.fileName.toLowerCase().includes(query) ||
        f.relativePath.toLowerCase().includes(query) ||
        f.group.toLowerCase().includes(query)
    );

    const groups = new Map<string, ConfigDocItem[]>();
    for (const file of files) {
      const existing = groups.get(file.group);
      if (existing) {
        existing.push(file);
      } else {
        groups.set(file.group, [file]);
      }
    }

    return Array.from(groups.entries());
  }, [catalog, filterText]);

  const activeDoc = selectedPath ? contentCache[selectedPath] : undefined;
  const headingLabel = 'MOBAdev Config Files';

  return (
    <div className={styles.layout}>
      {/* Left Sidebar: File Catalog */}
      <Card className={styles.sidebarCard} appearance="filled" aria-label={`${headingLabel} catalog`}>
        <div className={styles.sidebarHeader}>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <FolderOpen16Regular aria-hidden="true" />
            <Text weight="semibold">{headingLabel}</Text>
          </div>
          {catalog && (
            <Badge appearance="tint" color="subtle" shape="rounded">
              <span className="tabular">{catalog.files.length} Files</span>
            </Badge>
          )}
        </div>

        <Input
          size="small"
          value={filterText}
          onChange={(_, data) => setFilterText(data.value)}
          placeholder="Filter Markdown files…"
          aria-label="Filter Markdown files"
          contentBefore={<Search16Regular aria-hidden="true" />}
        />

        {loadingCatalog && <Spinner size="small" label="Loading catalog…" />}

        {catalogError && (
          <MessageBar intent="error">
            <MessageBarBody>{catalogError}</MessageBarBody>
          </MessageBar>
        )}

        {catalog && catalog.source.status !== 'ok' && catalog.source.notice && (
          <MessageBar intent="warning">
            <MessageBarBody>{catalog.source.notice}</MessageBarBody>
          </MessageBar>
        )}

        {groupedFiles.map(([group, items]) => (
          <div key={group} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span className={styles.groupTitle}>{group}</span>
            <div className={styles.fileList}>
              {items.map((item) => {
                const isSelected = item.relativePath === selectedPath;
                const roleSlug = item.fileName.replace(/\.md$/i, '');

                return (
                  <Button
                    key={item.relativePath}
                    size="small"
                    appearance={isSelected ? 'primary' : 'subtle'}
                    className={styles.fileButton}
                    onClick={() => setSelectedPath(item.relativePath)}
                    title={item.relativePath}
                  >
                    <span
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: 6,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap'
                      }}
                    >
                      {item.group.includes('Agent') ? (
                        <RoleBadge role={roleSlug} label={item.fileName} isDark={isDark} compact />
                      ) : (
                        <>
                          <DocumentText16Regular aria-hidden="true" />
                          <span>{item.fileName}</span>
                        </>
                      )}
                    </span>
                  </Button>
                );
              })}
            </div>
          </div>
        ))}
      </Card>

      {/* Right Pane: Read-Only Markdown Viewer */}
      <Card className={styles.contentCard} appearance="filled" aria-label="Read-only Markdown viewer">
        {!selectedPath && !loadingCatalog && (
          <Text style={{ color: tokens.colorNeutralForeground2 }}>
            Select a Markdown configuration file on the left to inspect it read-only.
          </Text>
        )}

        {loadingPath === selectedPath && <Spinner size="small" label="Loading document…" />}

        {contentError && (
          <MessageBar intent="error">
            <MessageBarBody>{contentError}</MessageBarBody>
          </MessageBar>
        )}

        {activeDoc && (
          <>
            <div className={styles.docHeader}>
              <div className={styles.docTitleGroup}>
                <Text size={400} weight="semibold">
                  {activeDoc.fileName}
                </Text>
                <Badge appearance="tint" color="brand" shape="rounded" icon={<Eye16Regular />}>
                  Read-Only
                </Badge>
                <Badge appearance="tint" color="subtle" shape="rounded" className="mono">
                  {activeDoc.repository}
                </Badge>
                <span className={`${styles.pathPill} mono`}>{activeDoc.relativePath}</span>
              </div>

              <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                <Text
                  size={200}
                  className="tabular"
                  style={{ color: tokens.colorNeutralForeground2 }}
                  title={formatAbsoluteEn(activeDoc.lastModifiedUtc)}
                >
                  {activeDoc.lineCount} lines
                </Text>

                <div className={styles.viewToggleGroup} role="group" aria-label="Document view mode">
                  <ToggleButton
                    size="small"
                    appearance={viewMode === 'formatted' ? 'primary' : 'subtle'}
                    checked={viewMode === 'formatted'}
                    onClick={() => setViewMode('formatted')}
                    icon={<DocumentText16Regular />}
                  >
                    Formatted
                  </ToggleButton>
                  <ToggleButton
                    size="small"
                    appearance={viewMode === 'raw' ? 'primary' : 'subtle'}
                    checked={viewMode === 'raw'}
                    onClick={() => setViewMode('raw')}
                    icon={<Code16Regular />}
                  >
                    Raw
                  </ToggleButton>
                </div>
              </div>
            </div>

            {activeDoc.truncated && (
              <MessageBar intent="info">
                <MessageBarBody>Document truncated for browser preview limit.</MessageBarBody>
              </MessageBar>
            )}

            {viewMode === 'formatted' ? (
              <MarkdownReader content={activeDoc.content} />
            ) : (
              <pre className={`${styles.rawPre} mono`}>{activeDoc.content}</pre>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
