import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Participant, UpsertDecision, UpsertOutcome, UpsertPlan } from '../api/types';
import { Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { errorMessage, formatRank, roman } from '../format';
import { useTournament } from './TournamentLayout';
import { PlayerEditor } from './PlayerEditor';

export function PlayersPage() {
  const { tournament, view, run } = useTournament();
  const { t } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const [editing, setEditing] = useState<Participant | 'new' | null>(null);
  const [upsertOpen, setUpsertOpen] = useState(false);
  const preliminaryCount = tournament.participants.filter((p) => p.preliminary).length;
  const players = [...tournament.participants].sort((a, b) => a.displayName.localeCompare(b.displayName));

  async function finalizeAll() {
    const ok = await confirm({
      title: t('players.finalizeAllTitle'),
      message: t('players.finalizeAllMessage', { count: preliminaryCount }),
      confirmLabel: t('players.finalizeAll'),
    });
    if (!ok) return;
    try {
      await run(() => api.finalizePreliminary(tournament.id));
      toast.success(t('players.finalizedDone', { count: preliminaryCount }));
    } catch (e) { toast.error(e); }
  }

  async function deleteAll() {
    const ok = await confirm({
      title: t('players.deleteAllTitle'),
      message: t('players.deleteAllMessage', { count: preliminaryCount }),
      confirmLabel: t('common.delete'),
      danger: true,
    });
    if (!ok) return;
    try {
      const next = await run(() => api.deletePreliminary(tournament.id));
      const result = next.result as { deleted: number; skippedWithGames: number[] };
      toast.success(t('players.deletedDone', { count: result.deleted }));
      if (result.skippedWithGames.length > 0) {
        const names = result.skippedWithGames.map((id) => tournament.participants.find((p) => p.id === id)?.displayName ?? id);
        toast.warning(t('players.deleteSkipped', { names: names.join(', ') }));
      }
    } catch (e) { toast.error(e); }
  }

  return (
    <div>
      <div className="section-header">
        <h2>{t('players.title', { count: tournament.participants.length })}</h2>
        <div className="toolbar">
          <button type="button" className="primary" onClick={() => setEditing('new')}>{t('players.add')}</button>
          <button type="button" onClick={finalizeAll} disabled={preliminaryCount === 0}>{t('players.finalizeAll')}</button>
          <button type="button" className="danger-outline" onClick={deleteAll} disabled={preliminaryCount === 0}>{t('players.deleteAll')}</button>
          <button type="button" onClick={() => setUpsertOpen(true)} disabled={tournament.participants.length === 0}>{t('catalog.upsertButton')}</button>
        </div>
      </div>

      {players.length === 0 ? (
        <div className="empty-state"><p>{t('players.empty')}</p></div>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th className="num">{t('players.localId')}</th>
              <th>{t('standings.name')}</th>
              <th>{t('players.englishName')}</th>
              <th>{t('field.rank')}</th>
              <th className="num">{t('field.rating')}</th>
              <th>{t('field.club')}</th>
              <th>{t('players.registration')}</th>
              <th>{t('field.availability')}</th>
              <th className="num">{t('players.games')}</th>
              <th>{t('field.comment')}</th>
            </tr>
          </thead>
          <tbody>
            {players.map((p) => (
              <tr key={p.id} className="clickable" onClick={() => setEditing(p)}>
                <td className="num muted">{p.id}</td>
                <td>
                  <button type="button" className="link-button" onClick={(e) => { e.stopPropagation(); setEditing(p); }}>{p.displayName}</button>
                  {p.catalogEntryId && <span className="badge badge-muted" title={t('player.linkedToCatalog')}>{t('players.linkedShort')}</span>}
                </td>
                <td lang="en">{[p.surname, p.firstName].filter(Boolean).join(' ')}</td>
                <td>{p.rank && formatRank(p.rank, t)}</td>
                <td className="num">{p.rating || ''}</td>
                <td>{p.club}</td>
                <td>{p.preliminary
                  ? <span className="badge badge-warning">{t('player.registrationPreliminary')}</span>
                  : <span className="badge badge-ok">{t('player.registrationFinal')}</span>}</td>
                <td className="availability">
                  {Array.from({ length: tournament.numberOfRounds }, (_, i) => i + 1).map((r) => {
                    const plays = !p.notPlayingRounds.includes(r);
                    return (
                      <span key={r} className={plays ? 'avail yes' : 'avail no'}
                        title={`${t('round.label', { roman: roman(r), n: r })}: ${plays ? t('player.plays') : t('player.notPlayingShort')}`}>
                        {plays ? '●' : '○'}
                      </span>
                    );
                  })}
                </td>
                <td className="num">{view.gameCounts[String(p.id)] ?? 0}</td>
                <td className="comment-cell">{p.comment}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editing && <PlayerEditor participant={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {upsertOpen && <CatalogUpsertDialog onClose={() => setUpsertOpen(false)} />}
    </div>
  );
}

function CatalogUpsertDialog({ onClose }: { onClose: () => void }) {
  const { tournament, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const [plan, setPlan] = useState<UpsertPlan | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [decisions, setDecisions] = useState<Record<number, UpsertDecision>>({});
  const [busy, setBusy] = useState(false);

  // Namesake conflicts start undecided: the referee must choose explicitly for each one.
  useEffect(() => { api.upsertPlan(tournament.id).then(setPlan, setLoadError); }, [tournament.id]);

  const conflicts = plan?.items.filter((i) => i.action === 'needsDecision') ?? [];
  const undecided = conflicts.filter((i) => !decisions[i.participantId]).length;

  async function apply() {
    setBusy(true);
    try {
      const next = await run(() => api.applyUpsert(tournament.id, Object.values(decisions)));
      const r = next.result as UpsertOutcome;
      toast.success(t('catalog.upsertDone', { created: r.created, updated: r.updated, skipped: r.skipped }));
      onClose();
    } catch (e) {
      setLoadError(e);
    } finally {
      setBusy(false);
    }
  }

  const decide = (participantId: number, decision: UpsertDecision['decision'], catalogEntryId: string | null = null) =>
    setDecisions((d) => ({ ...d, [participantId]: { participantId, decision, catalogEntryId } }));

  return (
    <Modal
      wide
      title={t('catalog.upsertTitle')}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="primary" onClick={apply} disabled={!plan || undecided > 0 || busy}>
            {t('catalog.upsertApply')}
          </button>
        </>
      }
    >
      {loadError !== null && <Notice kind="error">{errorMessage(loadError, tk)}</Notice>}
      {!plan && loadError === null && <p>{t('common.loading')}</p>}
      {plan && (
        <>
          <p>{t('catalog.upsertIntro')}</p>
          <ul className="summary-list">
            <li>{t('catalog.toCreate', { count: plan.toCreate })}</li>
            <li>{t('catalog.toUpdate', { count: plan.toUpdate })}</li>
            <li>{t('catalog.needsDecision', { count: plan.needsDecision })}</li>
          </ul>
          <Notice kind="warning">{t('catalog.overwriteWarning')}</Notice>
          {conflicts.length > 0 && (
            <div className="conflicts">
              <h3>{t('catalog.conflictsTitle')}</h3>
              {conflicts.map((item) => {
                const current = decisions[item.participantId];
                const name = `upsert-${item.participantId}`;
                return (
                  <fieldset key={item.participantId} className="conflict">
                    <legend>{item.participantName}</legend>
                    {item.candidates.map((c) => (
                      <label key={c.id} className="checkbox">
                        <input type="radio" name={name} checked={current?.decision === 'link' && current.catalogEntryId === c.id}
                          onChange={() => decide(item.participantId, 'link', c.id)} />
                        <span>{t('catalog.linkTo', { name: c.displayName, details: [c.rank && formatRank(c.rank, t), c.rating || '', c.club].filter(Boolean).join(', ') })}</span>
                      </label>
                    ))}
                    <label className="checkbox">
                      <input type="radio" name={name} checked={current?.decision === 'create'} onChange={() => decide(item.participantId, 'create')} />
                      <span>{t('catalog.createNew')}</span>
                    </label>
                    <label className="checkbox">
                      <input type="radio" name={name} checked={current?.decision === 'skip'} onChange={() => decide(item.participantId, 'skip')} />
                      <span>{t('catalog.skip')}</span>
                    </label>
                  </fieldset>
                );
              })}
              {undecided > 0 && <p className="muted">{t('catalog.undecided', { count: undecided })}</p>}
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
