import { useState, type ReactNode } from 'react';
import type { Participant, RoundCell, Standings } from '../api/types';
import { Notice } from '../components/common';
import { useI18n } from '../i18n';
import type { MessageKey } from '../i18n/en';
import { formatRank, roman } from '../format';
import { useTournament } from './TournamentLayout';
import { PlayerEditor } from './PlayerEditor';

type AttributeColumn = 'country' | 'club' | 'team' | 'coach' | 'grade' | 'rank' | 'rating' | 'group';

export function StandingsPage() {
  const { tournament, standings } = useTournament();
  const { t, tk, formatNumber } = useI18n();
  const [editing, setEditing] = useState<Participant | null>(null);

  if (!standings) return <div className="loading">{t('common.loading')}</div>;
  if (tournament.participants.length === 0) {
    return <div className="empty-state"><p>{t('standings.empty')}</p></div>;
  }

  const byId = new Map(tournament.participants.map((p) => [p.id, p]));
  const { layout } = tournament;

  const attributeValue = (p: Participant, column: AttributeColumn): string => {
    switch (column) {
      case 'rank': return p.rank ? formatRank(p.rank, t) : '';
      case 'rating': return p.rating ? String(p.rating) : '';
      case 'group': return tournament.system === 'mcMahon' && p.startScore ? String(p.startScore) : '';
      default: return p[column];
    }
  };
  const isEmpty = (column: AttributeColumn) => tournament.participants.every((p) => attributeValue(p, column) === '');

  const headers: ReactNode[] = [];
  const cellsFor: ((row: Standings['rows'][number], p: Participant) => ReactNode)[] = [];

  for (const column of layout.columns.filter((c) => c.visible)) {
    switch (column.key) {
      case 'place':
        headers.push(<th key="place" className="num">{t('standings.place')}</th>);
        cellsFor.push((row) => <td key="place" className="num">{row.place === row.placeTo ? row.place : `${row.place}–${row.placeTo}`}</td>);
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
        for (const round of standings.displayedRounds) {
          const counted = standings.countedRounds.includes(round);
          headers.push(
            <th key={`r${round}`} className={`num round-col${counted ? '' : ' not-counted'}`}
              title={counted ? t('round.label', { roman: roman(round), n: round }) : t('standings.roundNotCounted')}>
              {roman(round)}{!counted && '*'}
            </th>,
          );
          cellsFor.push((row) => <RoundCellView key={`r${round}`} cell={row.rounds[round - 1]} showColors={layout.showColors} />);
        }
        break;
      case 'criteria':
        for (const criterion of standings.criteria) {
          headers.push(
            <th key={`c-${criterion.key}`} className="num" title={tk(`criterion.${criterion.key}.description`, 'criterion.legacy.description', { token: criterion.key })}>
              {tk(`criterion.${criterion.key}.short`, 'criterion.legacy.short', { token: criterion.key.replace(/^legacy:/, '') })}
              {!criterion.calculated && '†'}
            </th>,
          );
          cellsFor.push((row) => {
            const value = row.values[criterion.key];
            return <td key={`c-${criterion.key}`} className="num">{value === null || value === undefined ? '—' : formatNumber(value)}</td>;
          });
        }
        break;
      case 'newRating':
        break; // post-tournament rating is outside P0
      default: {
        const key = column.key as AttributeColumn;
        if (layout.hideEmptyColumns && isEmpty(key)) break;
        headers.push(<th key={key} className={key === 'rating' ? 'num' : undefined}>{t(`standings.column.${key}` as MessageKey)}</th>);
        cellsFor.push((_, p) => <td key={key} className={key === 'rating' ? 'num' : undefined}>{attributeValue(p, key)}</td>);
      }
    }
  }

  const hasNotCalculated = standings.criteria.some((c) => !c.calculated);
  const hasExcludedRound = standings.displayedRounds.length !== standings.countedRounds.length;

  return (
    <div>
      <p className="muted standings-caption">
        {t('standings.afterRound', { roman: roman(tournament.currentRound) })}
        {' · '}
        {hasExcludedRound ? t('standings.currentExcluded') : t('standings.currentIncluded')}
      </p>
      {standings.notices.length > 0 && (
        <Notice kind="warning">
          <strong>{t('standings.noticesTitle')}</strong>
          <ul>
            {standings.notices.map((n) => <li key={n}>{tk(`standings.notice.${n}`, 'standings.notice.generic', { code: n })}</li>)}
          </ul>
        </Notice>
      )}
      <div className="table-scroll">
        <table className="data-table standings-table">
          <thead><tr><th className="num">#</th>{headers}</tr></thead>
          <tbody>
            {standings.rows.map((row) => {
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
        {hasExcludedRound && <> {t('standings.legendNotCounted')}</>}
        {hasNotCalculated && <> {t('standings.legendNotCalculated')}</>}
      </p>
      {editing && <PlayerEditor participant={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function RoundCellView({ cell, showColors }: { cell: RoundCell | undefined; showColors: boolean }) {
  const { t } = useI18n();
  if (!cell || cell.kind === 'none') return <td className="num round-cell muted" title={t('standings.cell.none')}></td>;
  if (cell.kind === 'notPlaying') return <td className="num round-cell muted" title={t('standings.cell.notPlaying')}>—</td>;
  if (cell.kind === 'bye') return <td className="num round-cell" title={t('standings.cell.bye')}>{t('standings.cell.byeShort')}</td>;

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
