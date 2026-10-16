import { useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { ClearResult, GenerationResult, Participant, StartingNumberSource } from '../api/types';
import { Checkbox, Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { parseStartingNumber, roman } from '../format';
import { useTournament } from './TournamentLayout';

/** Structured server error for invalid entered starting numbers (spec 0002 §2.2). */
interface NumbersError {
  max: number;
  missing: { id: number; name: string }[];
  outOfRange: { id: number; name: string; value: number }[];
  duplicates: { number: number; ids: number[]; names: string }[];
  gaps: number[];
}

type Choice = 'entered' | 'random' | null;

/**
 * Round-robin controls on the Pairings screen (specs 0001/0002): the starting-number table (editable before the
 * draw, batch save), generation with an explicit number source and ordered confirmations, and Clear round-robin draw.
 */
export function RoundRobinPanel() {
  const { tournament, view, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const rr = view.roundRobin!;
  const round = tournament.currentRound;

  const [drafts, setDrafts] = useState<Record<number, string>>({});
  const [numbersError, setNumbersError] = useState<NumbersError | null>(null);
  const [allRounds, setAllRounds] = useState(false);
  const [random, setRandom] = useState(false);
  const [choiceOpen, setChoiceOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  // Reset local edits whenever the server state changes (save, draw, clear).
  useEffect(() => { setDrafts({}); }, [tournament.updatedAt]);

  const eligible = useMemo(() => new Set(rr.eligible), [rr.eligible]);
  const rows = useMemo(() => {
    const number = (p: Participant) => rr.numbers[String(p.id)] ?? null;
    return [...tournament.participants].sort((a, b) =>
      Number(eligible.has(b.id)) - Number(eligible.has(a.id))
      || (number(a) ?? 1e9) - (number(b) ?? 1e9)
      || a.displayName.localeCompare(b.displayName));
  }, [tournament.participants, rr.numbers, eligible]);

  const dirty = Object.keys(drafts).length > 0;
  const locked = rr.lockedBy.length > 0;
  const lockedGames = rr.lockedBy.map((g) => `${roman(g.round)}/${g.board}`).join(', ');
  const errorIds = new Set<number>([
    ...(numbersError?.missing.map((m) => m.id) ?? []),
    ...(numbersError?.outOfRange.map((m) => m.id) ?? []),
    ...(numbersError?.duplicates.flatMap((d) => d.ids) ?? []),
  ]);

  const valueOf = (p: Participant) => drafts[p.id] ?? (rr.numbers[String(p.id)]?.toString() ?? '');
  const inactiveReason = (p: Participant) =>
    p.preliminary ? t('player.preliminaryShort') : t('player.joinsFrom', { roman: roman(p.firstEligibleRound) });

  async function saveNumbers() {
    const entries: { participantId: number; number: number | null }[] = [];
    for (const [id, text] of Object.entries(drafts)) {
      // Empty clears (null); a literal 0 or any other non-1–999 value is invalid and nothing is saved.
      const parsed = parseStartingNumber(text);
      if (parsed === 'invalid') {
        toast.error(new ApiError('startingNumbers.invalidValue', { value: text.trim(), max: 999 }, 400));
        return;
      }
      entries.push({ participantId: Number(id), number: parsed });
    }
    try {
      await run(() => api.saveStartingNumbers(tournament.id, entries));
      setNumbersError(null);
      toast.success(t('startingNumbers.saved'));
    } catch (e) { toast.error(e); }
  }

  /** Step 1 of the fixed order: the number source (spec 0002 §3). */
  function startGenerate() {
    // Frozen: the server reuses the frozen order. Unchecked: entered numbers are validated (no silent fallback).
    if (rr.frozen || !random) { void generate('entered'); return; }
    const anyEntered = rr.eligible.some((id) => rr.numbers[String(id)] !== null && rr.numbers[String(id)] !== undefined);
    if (anyEntered) setChoiceOpen(true);
    else void generate('random');
  }

  function choose(choice: Choice) {
    setChoiceOpen(false);
    if (choice) void generate(choice);
  }

  /** Step 2: round-count adjustment and replacement confirmations, then one final request. */
  async function generate(source: StartingNumberSource, roundCount: number | null = null, confirmReplace = false): Promise<void> {
    setBusy(true);
    try {
      const next = await run(() => api.generate(tournament.id, round, { allRounds, source, roundCount, confirmReplace }));
      const r = next.result as GenerationResult;
      setNumbersError(null);
      toast.success(t('generate.done', { created: r.created, replaced: r.replaced, kept: r.kept }));
    } catch (e) {
      setBusy(false);
      if (e instanceof ApiError && e.code === 'generate.roundCountAdjustment') {
        const ok = await confirm({
          title: t('generate.adjustTitle'),
          message: t('generate.adjustMessage', e.args),
          confirmLabel: t('generate.adjustConfirm', e.args),
        });
        if (ok) await generate(source, Number(e.args.required), confirmReplace);
        return;
      }
      if (e instanceof ApiError && e.code === 'generate.replaceUnplayed') {
        const ok = await confirm({
          title: t('generate.replaceTitle'),
          message: tk('error.generate.replaceUnplayed', 'error.unknown', e.args),
          confirmLabel: t('generate.replaceConfirm'),
          danger: true,
        });
        if (ok) await generate(source, roundCount, true);
        return;
      }
      if (e instanceof ApiError && e.code === 'generate.startingNumbersInvalid') {
        setNumbersError(e.args as unknown as NumbersError);
        return;
      }
      toast.error(e);
    } finally {
      setBusy(false);
    }
  }

  async function clearDraw() {
    try {
      await api.clearRoundRobin(tournament.id, round, false); // preflight: always answers with a confirmation request
    } catch (e) {
      if (!(e instanceof ApiError) || e.code !== 'clear.confirm') { toast.error(e); return; }
      const ok = await confirm({
        title: t('clear.title'),
        message: t('clear.message', { ...e.args, rounds: e.args.rounds || '—' }),
        confirmLabel: t('clear.confirmButton'),
        danger: true,
      });
      if (!ok) return;
      try {
        const next = await run(() => api.clearRoundRobin(tournament.id, round, true));
        const r = next.result as ClearResult;
        toast.success(t('clear.done', { count: r.deleted }));
        if (r.settingsUnlocked) toast.warning(t('clear.settingsUnlocked'));
      } catch (inner) { toast.error(inner); }
    }
  }

  return (
    <section className="rr-panel" aria-label={t('startingNumbers.title')}>
      <div className="generate-panel">
        <button type="button" className="primary" onClick={startGenerate} disabled={busy || locked || dirty}>{t('pairings.generate')}</button>
        <Checkbox label={t('generate.allRounds')} checked={allRounds} onChange={setAllRounds} disabled={locked} />
        {!rr.frozen && <Checkbox label={t('generate.randomNumbers')} hint={t('generate.randomNumbersHint')} checked={random} onChange={setRandom} disabled={locked} />}
        {rr.frozen && <span className="muted">{rr.randomStartingNumbers ? t('generate.numbersFrozenRandom') : t('generate.numbersFrozenOrdered')}</span>}
        <button type="button" className="danger-outline" onClick={clearDraw} disabled={locked || dirty}>{t('clear.button')}</button>
      </div>
      {locked && <Notice kind="warning">{t('generate.lockedHint', { games: lockedGames })}</Notice>}
      {/* Unsaved table edits block both Generate and Clear, so a server update can never silently discard them. */}
      {!locked && dirty && <p className="muted">{t('startingNumbers.generateNeedsSave')}</p>}
      {!locked && !allRounds && <p className="muted">{t('generate.singleRoundWarning')}</p>}

      {numbersError && <NumbersErrorSummary error={numbersError} />}

      <details className="starting-numbers" open={!rr.frozen}>
        <summary>{t('startingNumbers.title')}{rr.frozen ? ` — ${t('startingNumbers.frozenHint')}` : ''}</summary>
        <table className="data-table compact">
          <thead>
            <tr>
              <th className="num">{t('field.startingNumber')}</th>
              <th>{t('standings.name')}</th>
              <th>{t('startingNumbers.status')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((p) => {
              const active = eligible.has(p.id);
              const editable = active && !rr.frozen;
              return (
                <tr key={p.id} className={`${active ? '' : 'row-muted'}${errorIds.has(p.id) ? ' row-error' : ''}`}>
                  <td className="num">
                    <input
                      className="number-input"
                      inputMode="numeric"
                      aria-label={`${t('field.startingNumber')}: ${p.displayName}`}
                      aria-invalid={errorIds.has(p.id) || undefined}
                      value={valueOf(p)}
                      disabled={!editable}
                      onChange={(e) => setDrafts((d) => ({ ...d, [p.id]: e.target.value }))}
                    />
                  </td>
                  <td>{p.displayName}</td>
                  <td className="muted">{active ? t('startingNumbers.eligible') : `${t('startingNumbers.inactive')} (${inactiveReason(p)})`}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {!rr.frozen && (
          <div className="form-actions">
            {dirty && <span className="muted">{t('settings.unsaved')}</span>}
            {dirty && <button type="button" onClick={() => setDrafts({})}>{t('settings.discard')}</button>}
            <button type="button" className="primary" onClick={saveNumbers} disabled={!dirty}>{t('startingNumbers.save')}</button>
          </div>
        )}
        {!rr.frozen && <p className="muted">{t('startingNumbers.inactiveHint')}</p>}
      </details>

      {choiceOpen && (
        <Modal
          title={t('generate.choiceTitle')}
          onClose={() => choose(null)}
          footer={
            <>
              <button type="button" onClick={() => choose(null)}>{t('common.cancel')}</button>
              <button type="button" onClick={() => choose('entered')}>{t('generate.useEntered')}</button>
              <button type="button" className="danger" onClick={() => choose('random')}>{t('generate.overwriteRandom')}</button>
            </>
          }
        >
          <p className="confirm-message">{t('generate.choiceMessage')}</p>
        </Modal>
      )}
    </section>
  );
}

function NumbersErrorSummary({ error }: { error: NumbersError }) {
  const { t, tk } = useI18n();
  const lines: string[] = [
    ...error.missing.map((m) => t('startingNumbers.err.missing', { name: m.name })),
    ...error.duplicates.map((d) => t('startingNumbers.err.duplicate', { number: d.number, names: d.names })),
    ...error.outOfRange.map((o) => t('startingNumbers.err.outOfRange', { name: o.name, value: o.value, max: error.max })),
    ...(error.gaps.length > 0 ? [t('startingNumbers.err.gaps', { numbers: error.gaps.join(', '), max: error.max })] : []),
  ];
  return (
    <Notice kind="error">
      <strong>{tk('error.generate.startingNumbersInvalid', 'error.unknown', { max: error.max })}</strong>
      <ul>{lines.map((l) => <li key={l}>{l}</li>)}</ul>
    </Notice>
  );
}

