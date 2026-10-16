import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { TournamentSummary } from '../api/types';
import { Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { errorMessage } from '../format';

export function TournamentListPage() {
  const { t, tk, formatDateTime } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const navigate = useNavigate();
  const fileInput = useRef<HTMLInputElement>(null);
  const [items, setItems] = useState<TournamentSummary[] | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [importError, setImportError] = useState<unknown>(null);
  const [importing, setImporting] = useState(false);

  const load = useCallback(() => {
    api.listTournaments().then(setItems, setLoadError);
  }, []);
  useEffect(load, [load]);

  async function onImport(file: File | undefined) {
    if (!file) return;
    setImporting(true);
    setImportError(null);
    try {
      const view = await api.importXml(file);
      toast.success(t('import.success', { name: view.tournament.name }));
      navigate(`/t/${view.tournament.id}/standings`);
    } catch (e) {
      setImportError(e);
    } finally {
      setImporting(false);
      if (fileInput.current) fileInput.current.value = '';
    }
  }

  async function onDelete(item: TournamentSummary) {
    const ok = await confirm({
      title: t('tournament.deleteTitle'),
      message: t('tournament.deleteMessage', { name: item.name }),
      confirmLabel: t('common.delete'),
      danger: true,
    });
    if (!ok) return;
    try {
      await api.deleteTournament(item.id);
      toast.success(t('tournament.deleted', { name: item.name }));
      load();
    } catch (e) {
      toast.error(e);
    }
  }

  return (
    <section className="page">
      <div className="page-header">
        <h1>{t('nav.tournaments')}</h1>
        <div className="toolbar">
          <Link className="button primary" to="/new">{t('tournament.new')}</Link>
          <button type="button" onClick={() => fileInput.current?.click()} disabled={importing}>
            {importing ? t('import.inProgress') : t('import.button')}
          </button>
          <input
            ref={fileInput}
            type="file"
            accept=".xml,application/xml,text/xml"
            className="visually-hidden"
            aria-label={t('import.button')}
            onChange={(e) => onImport(e.target.files?.[0])}
          />
        </div>
      </div>
      <p className="page-intro">{t('import.hint')}</p>

      {importError !== null && <Notice kind="error">{t('import.failed')} {errorMessage(importError, tk)}</Notice>}
      {loadError !== null && <Notice kind="error">{errorMessage(loadError, tk)}</Notice>}

      {items && items.length === 0 && (
        <div className="empty-state">
          <p>{t('tournament.listEmpty')}</p>
        </div>
      )}

      {items && items.length > 0 && (
        <table className="data-table">
          <thead>
            <tr>
              <th>{t('tournament.name')}</th>
              <th className="num">{t('tournament.playerCount')}</th>
              <th className="num">{t('tournament.roundCount')}</th>
              <th>{t('tournament.lastModified')}</th>
              <th><span className="visually-hidden">{t('common.actions')}</span></th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td><Link to={`/t/${item.id}/standings`} className="strong-link">{item.name}</Link></td>
                <td className="num">{item.playerCount}</td>
                <td className="num">{item.roundCount}</td>
                <td>{formatDateTime(item.updatedAt)}</td>
                <td className="actions">
                  <Link to={`/t/${item.id}/standings`} className="button small">{t('common.open')}</Link>
                  <button type="button" className="small danger-outline" onClick={() => onDelete(item)}>{t('common.delete')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
