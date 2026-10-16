import { createContext, useCallback, useContext, useEffect, useState, useSyncExternalStore } from 'react';
import { Link, NavLink, Outlet, useLocation, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { ExportReport, Standings, Tournament, TournamentView } from '../api/types';
import { Modal, Notice, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { errorMessage, roman } from '../format';

// ---------- header title store ----------

let headerName: string | null = null;
const headerListeners = new Set<() => void>();
function setHeaderName(name: string | null) {
  headerName = name;
  headerListeners.forEach((l) => l());
}
export function useTournamentHeader() {
  return useSyncExternalStore(
    (l) => { headerListeners.add(l); return () => { headerListeners.delete(l); }; },
    () => headerName,
  );
}

// ---------- tournament context ----------

interface TournamentContextValue {
  view: TournamentView;
  tournament: Tournament;
  standings: Standings | null;
  /** Runs a mutation; on success replaces the tournament state with the server response. Errors are rethrown. */
  run: (action: () => Promise<TournamentView>) => Promise<TournamentView>;
}

const TournamentContext = createContext<TournamentContextValue | null>(null);

export function useTournament(): TournamentContextValue {
  const ctx = useContext(TournamentContext);
  if (!ctx) throw new Error('useTournament must be used inside TournamentLayout');
  return ctx;
}

export function TournamentLayout() {
  const { id = '' } = useParams();
  const { t, tk } = useI18n();
  const location = useLocation();
  const [view, setView] = useState<TournamentView | null>(null);
  const [standings, setStandings] = useState<Standings | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);

  useEffect(() => {
    setView(null);
    setLoadError(null);
    api.getTournament(id).then(setView, setLoadError);
  }, [id]);

  useEffect(() => {
    setHeaderName(view?.tournament.name ?? null);
    return () => setHeaderName(null);
  }, [view?.tournament.name]);

  // Standings are derived on the server; refresh whenever the tournament changes.
  const updatedAt = view?.tournament.updatedAt;
  useEffect(() => {
    if (!updatedAt) return;
    let cancelled = false;
    api.standings(id).then((s) => { if (!cancelled) setStandings(s); }, () => { if (!cancelled) setStandings(null); });
    return () => { cancelled = true; };
  }, [id, updatedAt]);

  const run = useCallback(async (action: () => Promise<TournamentView>) => {
    const next = await action();
    setView(next);
    return next;
  }, []);

  if (loadError) {
    return (
      <div className="page">
        <Notice kind="error">{errorMessage(loadError, tk)}</Notice>
        <Link to="/">{t('nav.backToTournaments')}</Link>
      </div>
    );
  }
  if (!view) return <div className="page loading">{t('common.loading')}</div>;

  const tournament = view.tournament;
  const showRoundNav = !location.pathname.endsWith('/settings');

  return (
    <TournamentContext.Provider value={{ view, tournament, standings, run }}>
      <section className="page">
        <div className="page-header">
          <div>
            <h1 className="tournament-title">{tournament.name}</h1>
            {tournament.description && <p className="page-intro">{tournament.description}</p>}
          </div>
          <div className="toolbar">
            <ExportButton tournamentId={tournament.id} name={tournament.name} />
          </div>
        </div>
        <nav className="tabs" aria-label={t('nav.tournamentSections')}>
          <NavLink to="standings">{t('nav.standings')}</NavLink>
          <NavLink to="pairings">{t('nav.pairings')}</NavLink>
          <NavLink to="players">{t('nav.players')}</NavLink>
          <NavLink to="settings">{t('nav.settings')}</NavLink>
        </nav>
        {showRoundNav && <RoundNavigator />}
        <Outlet />
      </section>
    </TournamentContext.Provider>
  );
}

function RoundNavigator() {
  const { tournament, run } = useTournament();
  const { t } = useI18n();
  const toast = useToast();
  const { currentRound, numberOfRounds, includeCurrentRound } = tournament;

  async function update(round: number, include: boolean) {
    try {
      await run(() => api.setRound(tournament.id, round, include));
    } catch (e) {
      toast.error(e);
    }
  }

  return (
    <div className="round-nav" role="group" aria-label={t('round.navigation')}>
      <button type="button" className="small" disabled={currentRound <= 1} onClick={() => update(currentRound - 1, includeCurrentRound)}
        aria-label={t('round.previous')}>‹</button>
      <label className="round-select">
        <span>{t('round.current')}</span>
        <select value={currentRound} onChange={(e) => update(Number(e.target.value), includeCurrentRound)}>
          {Array.from({ length: numberOfRounds }, (_, i) => i + 1).map((n) => (
            <option key={n} value={n}>{t('round.label', { roman: roman(n), n })}</option>
          ))}
        </select>
      </label>
      <button type="button" className="small" disabled={currentRound >= numberOfRounds} onClick={() => update(currentRound + 1, includeCurrentRound)}
        aria-label={t('round.next')}>›</button>
      <label className="checkbox round-include">
        <input type="checkbox" checked={includeCurrentRound} onChange={(e) => update(currentRound, e.target.checked)} />
        <span>{t('round.includeCurrent')}</span>
      </label>
    </div>
  );
}

function ExportButton({ tournamentId, name }: { tournamentId: string; name: string }) {
  const { t, tk } = useI18n();
  const toast = useToast();
  const [report, setReport] = useState<ExportReport | null>(null);
  const [busy, setBusy] = useState(false);

  function download() {
    const a = document.createElement('a');
    a.href = api.exportUrl(tournamentId);
    a.download = `${name}.xml`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setReport(null);
  }

  async function start() {
    setBusy(true);
    try {
      const r = await api.exportReport(tournamentId);
      if (!r.blocked && r.limitations.length === 0) download();
      else setReport(r);
    } catch (e) {
      toast.error(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button type="button" className="primary" onClick={start} disabled={busy}>{t('export.button')}</button>
      {report && (
        <Modal
          title={t('export.title')}
          onClose={() => setReport(null)}
          footer={
            <>
              <button type="button" onClick={() => setReport(null)}>{t('common.cancel')}</button>
              {!report.blocked && <button type="button" className="primary" onClick={download}>{t('export.downloadAnyway')}</button>}
            </>
          }
        >
          {report.blocked ? (
            <Notice kind="error">
              {tk(`error.${report.blockingCode}`, 'error.unknown', {
                ...report.blockingArgs,
                system: report.blockingArgs?.system ? tk(`system.${String(report.blockingArgs.system).replace(/^./, (c) => c.toLowerCase())}`, 'field.unknown') : '',
              })}
            </Notice>
          ) : (
            <>
              <p>{t('export.limitationsIntro')}</p>
              <ul className="limitations">
                {report.limitations.map((l) => (
                  <li key={`${l.scope}.${l.field}`}>
                    <strong>{tk(`field.${l.field}`, 'field.unknown')}</strong>
                    {' — '}
                    {tk(`export.scope.${l.scope}`, 'field.unknown', { count: l.count })}
                    {l.examples.length > 0 && <span className="muted"> ({l.examples.join(', ')})</span>}
                  </li>
                ))}
              </ul>
              <p className="muted">{t('export.limitationsFooter')}</p>
            </>
          )}
        </Modal>
      )}
    </>
  );
}
