import { useState } from 'react';
import type { Pairing, Participant } from '../api/types';
import { useI18n } from '../i18n';
import { formatRank, resultLabel, roman } from '../format';
import { useTournament } from './TournamentLayout';
import { PairingEditor } from './PairingEditor';

export function PairingsPage() {
  const { tournament } = useTournament();
  const { t } = useI18n();
  const [editing, setEditing] = useState<Pairing | 'new' | null>(null);
  const roundNumber = tournament.currentRound;
  const pairings = tournament.rounds.find((r) => r.number === roundNumber)?.pairings ?? [];
  const byId = new Map(tournament.participants.map((p) => [p.id, p]));

  const paired = new Set(pairings.flatMap((p) => [p.blackId, p.isBye ? null : p.whiteId]).filter((x): x is number => x !== null));
  const unpaired = tournament.participants.filter((p) => !paired.has(p.id));
  const available = unpaired.filter((p) => !p.preliminary && !p.notPlayingRounds.includes(roundNumber));
  const unavailable = unpaired.filter((p) => p.preliminary || p.notPlayingRounds.includes(roundNumber));

  return (
    <div>
      <div className="section-header">
        <h2>{t('pairings.title', { roman: roman(roundNumber) })}</h2>
        <div className="toolbar">
          <button type="button" className="primary" onClick={() => setEditing('new')}>{t('pairings.add')}</button>
          <button type="button" disabled aria-describedby="generate-hint" title={t('pairings.generateUnavailable')}>
            {t('pairings.generate')}
          </button>
        </div>
      </div>
      <p id="generate-hint" className="muted">{t('pairings.generateUnavailable')}</p>

      {pairings.length === 0 ? (
        <div className="empty-state"><p>{t('pairings.empty')}</p></div>
      ) : (
        <table className="data-table pairings-table">
          <thead>
            <tr>
              <th className="num">{t('pairing.board')}</th>
              <th>{t('color.black')}</th>
              <th>{t('color.white')}</th>
              <th className="num">{t('pairing.result')}</th>
              <th className="num">{t('pairing.handicap')}</th>
              <th>{t('pairing.comment')}</th>
              <th><span className="visually-hidden">{t('common.actions')}</span></th>
            </tr>
          </thead>
          <tbody>
            {pairings.map((p) => (
              <tr key={p.id} className="clickable" onClick={() => setEditing(p)}>
                <td className="num">
                  {p.boardNumber}
                  {p.boardNumberFixed && <span className="flag" title={t('pairing.boardFixed')}>📌</span>}
                </td>
                <td><PlayerCell player={p.blackId !== null ? byId.get(p.blackId) : undefined} winner={p.result === 'blackWins'} /></td>
                <td>
                  {p.isBye
                    ? <span className="badge">{t('pairing.bye')}</span>
                    : <PlayerCell player={p.whiteId !== null ? byId.get(p.whiteId) : undefined} winner={p.result === 'whiteWins'} />}
                </td>
                <td className="num">
                  {p.isBye ? '' : resultLabel(p.result, t, p.legacyResultToken)}
                  {p.resultByReferee && <span className="flag" title={t('pairing.byReferee')}>!</span>}
                </td>
                <td className="num">{p.handicap || ''}{p.additionalKomi ? ` (${p.additionalKomi > 0 ? '+' : ''}${p.additionalKomi})` : ''}</td>
                <td className="comment-cell">{p.comment}</td>
                <td className="actions">
                  {p.protected && <span className="badge badge-info" title={t('pairing.protectedHint')}>{t('pairing.protectedShort')}</span>}
                  <button type="button" className="small" onClick={(e) => { e.stopPropagation(); setEditing(p); }}>{t('common.edit')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="unpaired">
        <h3>{t('pairings.unpairedTitle')}</h3>
        {available.length === 0 && unavailable.length === 0 && <p className="muted">{t('pairings.allPaired')}</p>}
        {available.length > 0 && (
          <p><strong>{t('pairings.availableUnpaired')}:</strong> {available.map((p) => p.displayName).join(', ')}</p>
        )}
        {unavailable.length > 0 && (
          <p className="muted">
            <strong>{t('pairings.notEligible')}:</strong>{' '}
            {unavailable.map((p) => `${p.displayName} (${p.preliminary ? t('player.preliminaryShort') : t('player.notPlayingShort')})`).join(', ')}
          </p>
        )}
      </div>

      {editing && (
        <PairingEditor pairing={editing === 'new' ? null : editing} roundNumber={roundNumber} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}

function PlayerCell({ player, winner }: { player: Participant | undefined; winner: boolean }) {
  const { t } = useI18n();
  if (!player) return <span className="muted">{t('pairing.noPlayer')}</span>;
  return (
    <span className={winner ? 'winner' : undefined}>
      {winner && <span className="winner-star" aria-label={t('pairing.winner')}>★ </span>}
      {player.displayName}
      {player.rank && <span className="muted"> · {formatRank(player.rank, t)}</span>}
    </span>
  );
}
