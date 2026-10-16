import { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { GameResult, Pairing, PairingInput, Participant } from '../api/types';
import { Checkbox, Field, Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import type { MessageKey } from '../i18n/en';
import { errorMessage, roman } from '../format';
import { useTournament } from './TournamentLayout';

const FLIP: Partial<Record<GameResult, GameResult>> = { blackWins: 'whiteWins', whiteWins: 'blackWins' };

export function PairingEditor({ pairing, roundNumber, onClose }: { pairing: Pairing | null; roundNumber: number; onClose: () => void }) {
  const { tournament, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const isNew = pairing === null;
  const round = tournament.rounds.find((r) => r.number === roundNumber);

  const [form, setForm] = useState<PairingInput>(() => pairing
    ? {
      blackId: pairing.blackId, whiteId: pairing.whiteId, isBye: pairing.isBye, boardNumber: pairing.boardNumber,
      boardNumberFixed: pairing.boardNumberFixed, handicap: pairing.handicap, additionalKomi: pairing.additionalKomi,
      allowDraw: pairing.allowDraw, result: pairing.result, resultByReferee: pairing.resultByReferee, comment: pairing.comment,
      protected: pairing.protected,
    }
    : {
      blackId: null, whiteId: null, isBye: false, boardNumber: null, boardNumberFixed: false, handicap: 0, additionalKomi: 0,
      allowDraw: tournament.allowDraws, result: 'unset', resultByReferee: false, comment: '', protected: true,
    });
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const set = <K extends keyof PairingInput>(key: K, value: PairingInput[K]) => setForm((f) => ({ ...f, [key]: value }));

  // Players already in another pairing of this round, and players not eligible for a new assignment.
  const pairedElsewhere = new Set(
    (round?.pairings ?? []).filter((p) => p.id !== pairing?.id).flatMap((p) => [p.blackId, p.isBye ? null : p.whiteId]),
  );
  const original = new Set([pairing?.blackId, pairing?.isBye ? null : pairing?.whiteId]);
  const reason = (p: Participant): MessageKey | null => {
    if (pairedElsewhere.has(p.id)) return 'pairing.optionPaired';
    if (original.has(p.id)) return null;
    if (p.preliminary) return 'pairing.optionPreliminary';
    if (p.notPlayingRounds.includes(roundNumber)) return 'pairing.optionNotPlaying';
    return null;
  };
  const players = [...tournament.participants].sort((a, b) => a.displayName.localeCompare(b.displayName));

  const playerSelect = (key: 'blackId' | 'whiteId', label: string) => (
    <Field label={label}>
      <select value={form[key] ?? ''} onChange={(e) => set(key, e.target.value === '' ? null : Number(e.target.value))}>
        <option value="">{t('pairing.selectPlayer')}</option>
        {players.map((p) => {
          const why = reason(p);
          return (
            <option key={p.id} value={p.id} disabled={why !== null}>
              {p.displayName}{why ? ` — ${t(why)}` : ''}
            </option>
          );
        })}
      </select>
    </Field>
  );

  function swap() {
    setForm((f) => ({ ...f, blackId: f.whiteId, whiteId: f.blackId, result: FLIP[f.result] ?? f.result }));
  }

  async function save(confirmDiscard = false) {
    setSaving(true);
    setError(null);
    try {
      await run(() => (isNew
        ? api.addPairing(tournament.id, roundNumber, form)
        : api.updatePairing(tournament.id, pairing.id, form, confirmDiscard)));
      toast.success(t('pairing.saved'));
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'pairing.resultWouldBeDiscarded' && !confirmDiscard) {
        setSaving(false);
        const ok = await confirm({
          title: t('pairing.discardTitle'), message: t('pairing.discardMessage'), confirmLabel: t('pairing.discardConfirm'), danger: true,
        });
        if (ok) await save(true);
        return;
      }
      setError(e);
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    if (!pairing) return;
    const hasResult = pairing.result !== 'unset';
    const ok = await confirm({
      title: t('pairing.deleteTitle'),
      message: hasResult ? t('pairing.deleteWithResultMessage') : t('pairing.deleteMessage'),
      confirmLabel: t('common.delete'),
      danger: true,
    });
    if (!ok) return;
    try {
      await run(() => api.deletePairing(tournament.id, pairing.id, hasResult));
      toast.success(t('pairing.deleted'));
      onClose();
    } catch (e) {
      setError(e);
    }
  }

  const resultOptions: { value: GameResult; label: MessageKey }[] = [
    { value: 'unset', label: 'result.unset' },
    { value: 'blackWins', label: 'result.blackWins' },
    { value: 'whiteWins', label: 'result.whiteWins' },
    { value: 'bothLose', label: 'result.bothLose' },
  ];
  if (form.allowDraw || form.result === 'draw') resultOptions.push({ value: 'draw', label: 'result.draw' });

  return (
    <Modal
      wide
      title={isNew ? t('pairing.addTitle', { roman: roman(roundNumber) }) : t('pairing.editTitle', { roman: roman(roundNumber), board: pairing.boardNumber })}
      onClose={onClose}
      footer={
        <>
          {!isNew && <button type="button" className="danger-outline footer-left" onClick={remove}>{t('pairing.delete')}</button>}
          <button type="button" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="primary" onClick={() => save()} disabled={saving}>{saving ? t('save.saving') : t('common.save')}</button>
        </>
      }
    >
      {error !== null && <Notice kind="error">{errorMessage(error, tk)}</Notice>}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); save(); }}>
        <Field label={t('pairing.board')} hint={isNew ? t('pairing.boardAutoHint') : undefined}>
          <input type="number" min={1} value={form.boardNumber ?? ''} placeholder={t('pairing.boardAuto')}
            onChange={(e) => set('boardNumber', e.target.value === '' ? null : Number(e.target.value))} />
        </Field>
        <div className="field field-align-end">
          <Checkbox label={t('pairing.boardFixed')} checked={form.boardNumberFixed} onChange={(v) => set('boardNumberFixed', v)} />
        </div>

        {playerSelect('blackId', t('color.black'))}
        {form.isBye
          ? <div className="field"><span className="field-label">{t('color.white')}</span><span className="badge">{t('pairing.bye')}</span></div>
          : playerSelect('whiteId', t('color.white'))}
        <div className="field-wide inline-options">
          <button type="button" onClick={swap} disabled={form.isBye}>{t('pairing.swap')}</button>
          <Checkbox label={t('pairing.isBye')} checked={form.isBye} onChange={(v) => setForm((f) => (v ? { ...f, isBye: true, result: 'unset', resultByReferee: false } : { ...f, isBye: false }))} hint={t('pairing.byeHint')} />
        </div>

        <Field label={t('pairing.handicap')} hint={t('pairing.handicapUnit')}>
          <input type="number" min={0} max={9} value={form.handicap} onChange={(e) => set('handicap', Number(e.target.value) || 0)} />
        </Field>
        <Field label={t('pairing.additionalKomi')} hint={t('pairing.komiUnit')}>
          <input type="number" step={0.5} min={-50} max={50} value={form.additionalKomi} onChange={(e) => set('additionalKomi', Number(e.target.value) || 0)} />
        </Field>

        <Field label={t('pairing.result')}>
          <select value={form.result} disabled={form.isBye} onChange={(e) => set('result', e.target.value as GameResult)}>
            {resultOptions.map((o) => <option key={o.value} value={o.value}>{t(o.label)}</option>)}
            {form.result === 'unknown' && <option value="unknown">{t('result.unknown', { token: pairing?.legacyResultToken ?? '' })}</option>}
          </select>
        </Field>
        <div className="field field-align-end">
          <Checkbox label={t('pairing.allowDraw')} checked={form.allowDraw} onChange={(v) => set('allowDraw', v)} />
          <Checkbox label={t('pairing.byReferee')} checked={form.resultByReferee} onChange={(v) => set('resultByReferee', v)} />
        </div>

        <Field label={t('pairing.comment')} wide>
          <input value={form.comment} onChange={(e) => set('comment', e.target.value)} />
        </Field>
        <div className="field field-wide">
          <Checkbox label={t('pairing.protected')} checked={form.protected} onChange={(v) => set('protected', v)} hint={t('pairing.protectedHint')} />
        </div>
        <button type="submit" hidden />
      </form>
    </Modal>
  );
}
