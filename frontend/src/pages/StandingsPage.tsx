import { useEffect, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import type { Participant, Preparation, RoundCell } from '../api/types';
import { Checkbox, Notice } from '../components/common';
import { useI18n } from '../i18n';
import type { MessageKey } from '../i18n/en';
import { formatRank, roman, systemLabel } from '../format';
import { RoundNavigator, useTournament } from './TournamentLayout';
import { PlayerEditor } from './PlayerEditor';

type AttributeColumn = 'country' | 'club' | 'team' | 'coach' | 'grade' | 'rank' | 'rating' | 'group';

/** One displayed row in either mode: official standings or the pairing-preparation view. */
interface ViewRow {
  position: number;
  participantId: number;
  place?: string;
  groupScore?: number;
  values?: Record<string, number | null>;
  rounds: RoundCell[];
}

const PREP_KEY = (id: string) => `autokorsak.prepView.${id}`;

function readPreference(id: string): boolean {
  try { return localStorage.getItem(PREP_KEY(id)) === '1'; } catch { return false; }
}

/** Standings with the current-round control, which lives on this page only (spec 0003 §3). */
export function StandingsPage() {
  return (
    <>
      <RoundNavigator />
      <StandingsView />
    </>
  );
}

function StandingsView() {
  const { tournament, standings } = useTournament();
  const { t, tk, formatNumber } = useI18n();
  const [editing, setEditing] = useState<Participant | null>(null);
  const [prepMode, setPrepMode] = useState(() => readPreference(tournament.id));
  const [preparation, setPreparation] = useState<Preparation | null>(null);

  function togglePrep(on: boolean) {
    setPrepMode(on);
    try { localStorage.setItem(PREP_KEY(tournament.id), on ? '1' : '0'); } catch { /* preference only */ }
  }

  // The preparation projection is computed by the server from authoritative game data (spec 0001 §7).
  useEffect(() => {
    if (!prepMode) return;
    let cancelled = false;
    api.preparation(tournament.id).then((p) => { if (!cancelled) setPreparation(p); }, () => { if (!cancelled) setPreparation(null); });
    return () => { cancelled = true; };
  }, [prepMode, tournament.id, tournament.updatedAt]);

  if (!standings || (prepMode && !preparation)) return <div className="loading">{t('common.loading')}</div>;
  if (tournament.participants.length === 0) {
    return <div className="empty-state"><p>{t('standings.empty')}</p></div>;
  }

  const byId = new Map(tournament.participants.map((p) => [p.id, p]));
  const { layout } = tournament;
  const displayedRounds = prepMode ? preparation!.displayedRounds : standings.displayedRounds;
  const countedRounds = prepMode ? preparation!.scoredRounds : standings.countedRounds;
  const notices = prepMode ? preparation!.notices : standings.notices;
  const rows: ViewRow[] = prepMode
    ? preparation!.rows.map((r) => ({ position: r.position, participantId: r.participantId, groupScore: r.groupScore, rounds: r.rounds }))
    : standings.rows.map((r) => ({
      position: r.position, participantId: r.participantId, values: r.values, rounds: r.rounds,
      place: r.place === r.placeTo ? String(r.place) : `${r.place}–${r.placeTo}`,
    }));

  const attributeValue = (p: Participant, column: AttributeColumn): string => {
    switch (column) {
      case 'rank': return p.rank ? formatRank(p.rank, t) : '';
      case 'rating': return String(p.rating); // 0 is a real rating (spec 0003 §5.2)
      case 'group': return tournament.system === 'mcMahon' && p.startScore ? String(p.startScore) : '';
      default: return p[column];
    }
  };
  const isEmpty = (column: AttributeColumn) => tournament.participants.every((p) => attributeValue(p, column) === '');

  const headers: ReactNode[] = [];
  const cellsFor: ((row: ViewRow, p: Participant) => ReactNode)[] = [];

  if (prepMode) {
    headers.push(<th key="group" className="num" title={t('standings.prep.groupHint')}>{t('standings.prep.group')}</th>);
    cellsFor.push((row) => <td key="group" className="num strong">{formatNumber(row.groupScore ?? 0)}</td>);
  }

  for (const column of layout.columns.filter((c) => c.visible)) {
    switch (column.key) {
      case 'place':
        if (prepMode) break; // the preparation order is not an official place
        headers.push(<th key="place" className="num">{t('standings.place')}</th>);
        cellsFor.push((row) => <td key="place" className="num">{row.place}</td>);
        break;
      case 'name':
        headers.push(<th key="name">{t('standings.name')}</th>);
        cellsFor.push((_, p) => (
          <td key="name" className="name-cell">
            <button type="button" className="link-button" onClick={() => setEditing(p)}>{p.displayName}</button>
            {p.preliminary && <span className="badge badge-warning">{t('player.preliminaryShort')}</span>}
          </td>
        ));
        break;
      case 'rounds':
        for (const round of displayedRounds) {
          const counted = countedRounds.includes(round);
          const title = counted ? t('round.label', { roman: roman(round), n: round })
            : prepMode ? t('standings.prep.roundNotScored') : t('standings.roundNotCounted');
          headers.push(
            <th key={`r${round}`} className={`num round-col${counted ? '' : ' not-counted'}`} title={title}>
              {roman(round)}{!counted && '*'}
            </th>,
          );
          cellsFor.push((row) => <RoundCellView key={`r${round}`} cell={row.rounds[round - 1]} showColors={layout.showColors} />);
        }
        break;
      case 'criteria':
        if (prepMode) break; // coefficients do not order players inside a score group
        for (const criterion of standings.criteria) {
          headers.push(
            <th key={`c-${criterion.key}`} className="num" title={tk(`criterion.${criterion.key}.description`, 'criterion.legacy.description', { token: criterion.key })}>
              {tk(`criterion.${criterion.key}.short`, 'criterion.legacy.short', { token: criterion.key.replace(/^legacy:/, '') })}
              {!criterion.calculated && '†'}
            </th>,
          );
          cellsFor.push((row) => {
            const value = row.values?.[criterion.key];
            return <td key={`c-${criterion.key}`} className="num">{value === null || value === undefined ? '—' : formatNumber(value)}</td>;
          });
        }
        break;
      case 'newRating':
        break; // post-tournament rating is outside the implemented scope
      default: {
        const key = column.key as AttributeColumn;
        if (layout.hideEmptyColumns && isEmpty(key)) break;
        headers.push(<th key={key} className={key === 'rating' ? 'num' : undefined}>{t(`standings.column.${key}` as MessageKey)}</th>);
        cellsFor.push((_, p) => <td key={key} className={key === 'rating' ? 'num' : undefined}>{attributeValue(p, key)}</td>);
      }
    }
  }

  const hasNotCalculated = !prepMode && standings.criteria.some((c) => !c.calculated);
  const hasExcludedRound = displayedRounds.length !== countedRounds.length;

  return (
    <div>
      <div className="standings-toolbar">
        <p className="muted standings-caption">
          {prepMode
            ? t('standings.prep.caption', {
              roman: roman(preparation!.round),
              system: systemLabel(preparation!.system, tk, tournament.legacyKind),
            })
            : <>
              {t('standings.afterRound', { roman: roman(tournament.currentRound) })}
              {' · '}
              {hasExcludedRound ? t('standings.currentExcluded') : t('standings.currentIncluded')}
            </>}
        </p>
        <Checkbox label={t('standings.prep.toggle')} checked={prepMode} onChange={togglePrep} />
      </div>
      {prepMode && <Notice>{t('standings.prep.notice')}</Notice>}
      {notices.length > 0 && (
        <Notice kind="warning">
          <strong>{t('standings.noticesTitle')}</strong>
          <ul>
            {notices.map((n) => <li key={n}>{tk(`standings.notice.${n}`, 'standings.notice.generic', { code: n })}</li>)}
          </ul>
        </Notice>
      )}
      <div className="table-scroll">
        <table className={`data-table standings-table${prepMode ? ' prep-mode' : ''}`}>
          <thead><tr><th className="num">{prepMode ? '№' : '#'}</th>{headers}</tr></thead>
          <tbody>
            {rows.map((row) => {
              const p = byId.get(row.participantId)!;
              return (
                <tr key={row.participantId} className={p.preliminary ? 'row-muted' : undefined}>
                  <td className="num muted">{row.position}</td>
                  {cellsFor.map((render) => render(row, p))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p className="muted legend">
        {t('standings.legend')}
        {hasExcludedRound && <> {prepMode ? t('standings.prep.legendNotScored') : t('standings.legendNotCounted')}</>}
        {hasNotCalculated && <> {t('standings.legendNotCalculated')}</>}
      </p>
      {editing && <PlayerEditor participant={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function RoundCellView({ cell, showColors }: { cell: RoundCell | undefined; showColors: boolean }) {
  const { t } = useI18n();
  if (!cell || cell.kind === 'none') return <td className="num round-cell muted" title={t('standings.cell.none')}></td>;
  if (cell.kind === 'notJoined') return <td className="num round-cell muted" title={t('standings.cell.notJoined')}>·</td>;
  if (cell.kind === 'notPlaying') return <td className="num round-cell muted" title={t('standings.cell.notPlaying')}>—</td>;
  if (cell.kind === 'bye') return <td className="num round-cell outcome-win" title={t('standings.cell.bye')}>{t('standings.cell.byeShort')}</td>;

  const symbol: Record<string, string> = { win: '+', loss: '−', draw: '=', bothLose: '−', unset: '?', unknown: '?' };
  const outcomeTitle: Record<string, MessageKey> = {
    win: 'standings.outcome.win', loss: 'standings.outcome.loss', draw: 'standings.outcome.draw',
    bothLose: 'standings.outcome.bothLose', unset: 'standings.outcome.unset', unknown: 'standings.outcome.unknown',
  };
  const outcome = cell.outcome ?? 'unset';
  const color = cell.color === 'black' ? t('color.black') : t('color.white');
  return (
    <td className={`num round-cell outcome-${outcome}`}
      title={`${t(outcomeTitle[outcome])} · ${color}${cell.byReferee ? ` · ${t('pairing.byReferee')}` : ''}`}>
      {cell.opponentPosition}{symbol[outcome]}
      {cell.byReferee && '!'}
      {showColors && <span className={`stone stone-${cell.color}`} aria-label={color} />}
    </td>
  );
}
