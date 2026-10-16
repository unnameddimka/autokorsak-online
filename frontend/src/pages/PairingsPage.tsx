import { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { GenerationResult, Pairing, Participant, TournamentSystem } from '../api/types';
import { Checkbox, Field, Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { errorMessage, formatRank, resultLabel, roman, stageEnd, stageFor, systemLabel } from '../format';
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
  const eligible = (p: Participant) => !p.preliminary && p.firstEligibleRound <= roundNumber && !p.notPlayingRounds.includes(roundNumber);
  const available = unpaired.filter(eligible);
  const unavailable = unpaired.filter((p) => !eligible(p));
  const reason = (p: Participant) =>
    p.preliminary ? t('player.preliminaryShort')
      : p.firstEligibleRound > roundNumber ? t('player.joinsFrom', { roman: roman(p.firstEligibleRound) })
        : t('player.notPlayingShort');

  return (
    <div>
      <StagePanel />
      <div className="section-header">
        <h2>{t('pairings.title', { roman: roman(roundNumber) })}</h2>
        <div className="toolbar">
          <button type="button" onClick={() => setEditing('new')}>{t('pairings.add')}</button>
        </div>
      </div>
      <GeneratePanel />

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
                <td><PlayerCell player={p.blackId !== null ? byId.get(p.blackId) : undefined} winner={p.result === 'blackWins' || p.isBye} /></td>
                <td>
                  {p.isBye
                    ? <span className="badge">{t('pairing.bye')}</span>
                    : <PlayerCell player={p.whiteId !== null ? byId.get(p.whiteId) : undefined} winner={p.result === 'whiteWins'} />}
                </td>
                <td className="num">
                  {p.isBye ? '+1' : resultLabel(p.result, t, p.legacyResultToken)}
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
            {unavailable.map((p) => `${p.displayName} (${reason(p)})`).join(', ')}
          </p>
        )}
      </div>

      {editing && (
        <PairingEditor pairing={editing === 'new' ? null : editing} roundNumber={roundNumber} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}

/** Pairing stages: which system governs which rounds, frozen round-robin starting numbers, and the change action. */
function StagePanel() {
  const { tournament } = useTournament();
  const { t, tk } = useI18n();
  const [changing, setChanging] = useState(false);
  const current = stageFor(tournament, tournament.currentRound);
  const byId = new Map(tournament.participants.map((p) => [p.id, p]));
  const roster = current.system === 'roundRobin' ? current.frozenRosterIds : null;
  const lastStart = Math.max(...tournament.stages.map((s) => s.startRound), 1);
  const canChange = lastStart + 1 <= tournament.numberOfRounds;

  return (
    <section className="stage-panel" aria-label={t('stage.title')}>
      <div className="stage-header">
        <div>
          <span className="muted">{t('stage.effectiveFor', { roman: roman(tournament.currentRound) })}</span>{' '}
          <strong>{systemLabel(current.system, tk, tournament.legacyKind)}</strong>
        </div>
        <button type="button" className="small" onClick={() => setChanging(true)} disabled={!canChange}>{t('stage.change')}</button>
      </div>
      {tournament.stages.length > 1 && (
        <ol className="stage-list">
          {tournament.stages.map((s) => {
            const end = stageEnd(tournament, s);
            return (
              <li key={s.startRound} className={s === current ? 'current' : undefined}>
                {t('stage.rounds', { from: roman(s.startRound), to: roman(end) })}: {systemLabel(s.system, tk, tournament.legacyKind)}
              </li>
            );
          })}
        </ol>
      )}
      {roster && (
        <details className="starting-numbers">
          <summary>
            {current.randomStartingNumbers ? t('generate.numbersFrozenRandom') : t('generate.numbersFrozenOrdered')}
          </summary>
          <ol>
            {roster.map((id) => <li key={id}>{byId.get(id)?.displayName ?? id}</li>)}
          </ol>
        </details>
      )}
      {changing && <ChangeSystemDialog minRound={lastStart + 1} onClose={() => setChanging(false)} />}
    </section>
  );
}

function GeneratePanel() {
  const { tournament, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const [allRounds, setAllRounds] = useState(false);
  const [randomNumbers, setRandomNumbers] = useState(false);
  const [busy, setBusy] = useState(false);
  const round = tournament.currentRound;
  const stage = stageFor(tournament, round);
  const isRoundRobin = stage.system === 'roundRobin';
  const frozen = stage.frozenRosterIds !== null;

  async function generate(confirmReplace = false): Promise<void> {
    setBusy(true);
    try {
      const view = await run(() => api.generate(tournament.id, round, { allRounds, randomStartingNumbers: randomNumbers, confirmReplace }));
      const r = view.result as GenerationResult;
      toast.success(t('generate.done', { created: r.created, replaced: r.replaced, kept: r.kept }));
    } catch (e) {
      if (e instanceof ApiError && e.code === 'generate.replaceUnplayed' && !confirmReplace) {
        setBusy(false);
        const ok = await confirm({
          title: t('generate.replaceTitle'),
          message: tk('error.generate.replaceUnplayed', 'error.unknown', e.args),
          confirmLabel: t('generate.replaceConfirm'),
          danger: true,
        });
        if (ok) await generate(true);
        return;
      }
      toast.error(e);
    } finally {
      setBusy(false);
    }
  }

  if (!isRoundRobin) {
    return (
      <div className="generate-panel">
        <button type="button" disabled aria-describedby="generate-hint">{t('pairings.generate')}</button>
        <span id="generate-hint" className="muted">{t('pairings.generateUnavailable')}</span>
      </div>
    );
  }

  return (
    <div className="generate-panel">
      <button type="button" className="primary" onClick={() => generate()} disabled={busy}>{t('pairings.generate')}</button>
      <Checkbox label={t('generate.allRounds')} checked={allRounds} onChange={setAllRounds} />
      {frozen
        ? <span className="muted">{stage.randomStartingNumbers ? t('generate.numbersFrozenRandom') : t('generate.numbersFrozenOrdered')}</span>
        : <Checkbox label={t('generate.randomNumbers')} hint={t('generate.randomNumbersHint')} checked={randomNumbers} onChange={setRandomNumbers} />}
    </div>
  );
}

function ChangeSystemDialog({ minRound, onClose }: { minRound: number; onClose: () => void }) {
  const { tournament, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const [startRound, setStartRound] = useState(Math.max(minRound, tournament.currentRound));
  const [system, setSystem] = useState<TournamentSystem>('swiss');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const systems: TournamentSystem[] = ['swiss', 'mcMahon', 'scheveningen'];

  async function apply(confirmDelete = false): Promise<void> {
    setBusy(true);
    setError(null);
    try {
      await run(() => api.changeSystem(tournament.id, startRound, system, confirmDelete));
      toast.success(t('stage.changed', { roman: roman(startRound), system: tk(`system.${system}`, 'field.unknown') }));
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'stage.unplayedPairingsWillBeDeleted' && !confirmDelete) {
        setBusy(false);
        const ok = await confirm({
          title: t('stage.deleteTitle'),
          message: tk('error.stage.unplayedPairingsWillBeDeleted', 'error.unknown', e.args),
          confirmLabel: t('stage.deleteConfirm'),
          danger: true,
        });
        if (ok) await apply(true);
        return;
      }
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  const rounds = Array.from({ length: tournament.numberOfRounds - minRound + 1 }, (_, i) => minRound + i);
  return (
    <Modal
      title={t('stage.change')}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="primary" onClick={() => apply()} disabled={busy}>{t('stage.apply')}</button>
        </>
      }
    >
      <p>{t('stage.intro')}</p>
      {error !== null && <Notice kind="error">{errorMessage(error, tk)}</Notice>}
      <div className="form-grid">
        <Field label={t('stage.startRound')}>
          <select value={startRound} onChange={(e) => setStartRound(Number(e.target.value))}>
            {rounds.map((r) => <option key={r} value={r}>{t('round.label', { roman: roman(r), n: r })}</option>)}
          </select>
        </Field>
        <Field label={t('stage.newSystem')}>
          <select value={system} onChange={(e) => setSystem(e.target.value as TournamentSystem)}>
            {systems.map((s) => <option key={s} value={s}>{tk(`system.${s}`, 'field.unknown')}</option>)}
          </select>
        </Field>
      </div>
      <p className="muted">{t('stage.generatorNote')}</p>
    </Modal>
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
