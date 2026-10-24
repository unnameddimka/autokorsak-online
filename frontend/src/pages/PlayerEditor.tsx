import { useEffect, useState, type ChangeEvent, type ReactNode } from 'react';
import { api, ApiError } from '../api/client';
import type { CatalogEntry, Participant, ParticipantInput } from '../api/types';
import { AppOnlyBadge, Checkbox, Field, Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import {
  errorMessage, formatRank, isLockedRosterRound, isPureRoundRobin, parseRating, parseStartingNumber, RANK_OPTIONS, RATING_MAX, RATING_MIN, roman,
} from '../format';
import { transliterate } from '../transliteration';
import { useTournament } from './TournamentLayout';

const EMPTY: ParticipantInput = {
  localFirstName: '', localSurname: '', firstName: '', surname: '', rating: 0, rank: '', rankShift: 0, grade: '',
  country: '', club: '', nationality: '', team: '', coach: '', preliminary: false, notPlayingRounds: [], comment: '',
  catalogEntryId: null, firstEligibleRound: null,
};

function toInput(p: Participant): ParticipantInput {
  const { localFirstName, localSurname, firstName, surname, rating, rank, rankShift, grade, country, club, nationality,
    team, coach, preliminary, notPlayingRounds, comment, catalogEntryId } = p;
  return { localFirstName, localSurname, firstName, surname, rating, rank, rankShift, grade, country, club, nationality,
    team, coach, preliminary, notPlayingRounds: [...notPlayingRounds], comment, catalogEntryId,
    firstEligibleRound: p.firstEligibleRound };
}

/** Adds or edits a tournament participant. Changes never reach the shared catalog until an explicit upsert. */
export function PlayerEditor({ participant, onClose }: { participant: Participant | null; onClose: () => void }) {
  const { tournament, view, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const [form, setForm] = useState<ParticipantInput>(() =>
    (participant ? toInput(participant) : { ...EMPTY, firstEligibleRound: view.defaultJoinRound }));
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const isNew = participant === null;
  const games = participant ? view.gameCounts[String(participant.id)] ?? 0 : 0;
  // A finally registered player with pairings cannot become preliminary (the server enforces this too).
  const preliminaryBlocked = !isNew && !participant.preliminary && games > 0;
  // Spec 0002 §2: the starting number (round-robin tournaments only). Frozen numbers come from the drawn roster.
  const rr = view.roundRobin;
  const initialNumber = participant ? rr?.numbers[String(participant.id)] ?? null : null;
  const [numberText, setNumberText] = useState(initialNumber?.toString() ?? '');
  // Spec 0003 §2.3: a pure round robin has no joining round or per-round availability (the server ignores them).
  const pureRoundRobin = isPureRoundRobin(tournament);
  const joinRound = pureRoundRobin ? 1 : form.firstEligibleRound ?? 1;
  const numberEditable = !!rr && !rr.frozen && !form.preliminary && joinRound <= rr.startRound;
  // Spec 0003 §5.1: the rating is edited as text and converted on save, so an emptied field stays empty while typing.
  const [ratingText, setRatingText] = useState(() => String(participant?.rating ?? EMPTY.rating));
  const [ratingInvalid, setRatingInvalid] = useState(false);
  const [transliterationNote, setTransliterationNote] = useState<string | null>(null);

  const set = <K extends keyof ParticipantInput>(key: K, value: ParticipantInput[K]) => setForm((f) => ({ ...f, [key]: value }));
  const text = (key: 'localSurname' | 'localFirstName' | 'surname' | 'firstName' | 'grade' | 'country' | 'club' | 'nationality' | 'team' | 'coach' | 'comment') => ({
    value: form[key],
    onChange: (e: ChangeEvent<HTMLInputElement>) => set(key, e.target.value),
  });

  async function save() {
    // Only a changed number is validated and sent; an unchanged (e.g. frozen, read-only) number never blocks other edits.
    const trimmed = numberText.trim();
    const numberChanged = trimmed !== (initialNumber?.toString() ?? '');
    const parsed = parseStartingNumber(trimmed);
    if (numberChanged && parsed === 'invalid') {
      setError(new ApiError('startingNumbers.invalidValue', { value: trimmed, max: 999 }, 400));
      return;
    }
    // Card payload: null = unchanged, 0 = clear (internal sentinel for an emptied field), n = set.
    const startingNumber = !numberChanged ? null : parsed === null ? 0 : (parsed as number);
    const rating = parseRating(ratingText);
    setRatingInvalid(rating === 'invalid');
    if (rating === 'invalid') {
      setError(new ApiError('participant.invalidRating', { min: RATING_MIN, max: RATING_MAX }, 400));
      return;
    }
    const input = { ...form, rating, startingNumber };
    setSaving(true);
    setError(null);
    try {
      await run(() => (isNew ? api.addParticipant(tournament.id, input) : api.updateParticipant(tournament.id, participant.id, input)));
      toast.success(isNew ? t('player.added') : t('player.saved'));
      onClose();
    } catch (e) {
      setError(e);
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    if (!participant) return;
    if (games > 0) {
      setError(new ApiError('participant.hasGames', { count: games }, 409));
      return;
    }
    const ok = await confirm({
      title: t('player.deleteTitle'),
      message: t('player.deleteMessage', { name: participant.displayName }),
      confirmLabel: t('common.delete'),
      danger: true,
    });
    if (!ok) return;
    try {
      await run(() => api.deleteParticipant(tournament.id, participant.id));
      toast.success(t('player.deleted'));
      onClose();
    } catch (e) {
      setError(e);
    }
  }

  function applyCatalogEntry(entry: CatalogEntry) {
    setForm((f) => ({
      ...f,
      localFirstName: entry.localFirstName, localSurname: entry.localSurname, firstName: entry.firstName, surname: entry.surname,
      rating: entry.rating, rank: entry.rank, grade: entry.grade, country: entry.country, club: entry.club,
      nationality: entry.nationality, team: entry.team, coach: entry.coach, catalogEntryId: entry.id,
    }));
    setRatingText(String(entry.rating));
    setRatingInvalid(false);
  }

  /**
   * Spec 0003 §4: fills the English name from the Ukrainian one on an explicit click only. A destination is written only
   * when its source has text, and overwriting any existing English value needs one confirmation for both fields.
   */
  async function transliterateNames() {
    const sources = { surname: form.localSurname, firstName: form.localFirstName } as const;
    const next: Partial<Record<'surname' | 'firstName', string>> = {};
    const unsupported = new Set<string>();
    for (const key of ['surname', 'firstName'] as const) {
      if (sources[key].trim() === '') continue;
      const result = transliterate(sources[key]);
      next[key] = result.text;
      result.unsupported.forEach((c) => unsupported.add(c));
    }
    if (next.surname === undefined && next.firstName === undefined) {
      setTransliterationNote(t('player.transliterateEmpty'));
      return;
    }
    const overwritten = (['surname', 'firstName'] as const).filter((key) => next[key] !== undefined && form[key].trim() !== '' && form[key] !== next[key]);
    if (overwritten.length > 0) {
      const ok = await confirm({
        title: t('player.transliterateTitle'),
        message: t('player.transliterateMessage', {
          current: [form.surname, form.firstName].filter(Boolean).join(' '),
          next: [next.surname ?? form.surname, next.firstName ?? form.firstName].filter(Boolean).join(' '),
        }),
        confirmLabel: t('player.transliterateConfirm'),
      });
      if (!ok) return;
    }
    setForm((f) => ({ ...f, ...next }));
    setTransliterationNote(unsupported.size > 0 ? t('player.transliterateUnsupported', { chars: [...unsupported].join(' ') }) : null);
  }

  const toggleRound = (round: number, plays: boolean) =>
    set('notPlayingRounds', plays ? form.notPlayingRounds.filter((r) => r !== round) : [...form.notPlayingRounds, round].sort((a, b) => a - b));

  // The badge sits next to the field, outside its <label>: a button inside the label would take the label from the input.
  const appOnly = (label: string, input: ReactNode) => (
    <div className="field-with-aside">
      <Field label={label}>{input}</Field>
      <AppOnlyBadge />
    </div>
  );
  const rankOptions = form.rank && !RANK_OPTIONS.includes(form.rank) ? [form.rank, ...RANK_OPTIONS] : RANK_OPTIONS;

  return (
    <Modal
      wide
      title={isNew ? t('player.addTitle') : t('player.editTitle', { name: participant.displayName })}
      onClose={onClose}
      footer={
        <>
          {!isNew && <button type="button" className="danger-outline footer-left" onClick={remove}>{t('player.delete')}</button>}
          <button type="button" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="primary" onClick={save} disabled={saving}>{saving ? t('save.saving') : t('common.save')}</button>
        </>
      }
    >
      {isNew && <CatalogSearch onPick={applyCatalogEntry} />}
      {form.catalogEntryId && (
        <Notice>
          {t('player.linkedToCatalog')}{' '}
          {isNew && <button type="button" className="link-button" onClick={() => set('catalogEntryId', null)}>{t('player.unlink')}</button>}
        </Notice>
      )}
      {error !== null && <Notice kind="error">{errorMessage(error, tk)}</Notice>}

      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); save(); }}>
        <Field label={t('field.localSurname')} hint={t('player.localSurnameHint')}><input {...text('localSurname')} autoFocus={!isNew} /></Field>
        <Field label={t('field.localFirstName')}><input {...text('localFirstName')} /></Field>
        <div className="transliterate-row">
          <button type="button" onClick={() => void transliterateNames()}>{t('player.transliterate')}</button>
          <span className="field-hint">{transliterationNote ?? t('player.transliterateHint')}</span>
        </div>
        <Field label={t('field.surname')}><input {...text('surname')} lang="en" /></Field>
        <Field label={t('field.firstName')}><input {...text('firstName')} lang="en" /></Field>

        <Field label={t('field.rating')} hint={ratingInvalid ? t('error.participant.invalidRating') : undefined}>
          <input inputMode="numeric" value={ratingText} aria-invalid={ratingInvalid || undefined}
            onChange={(e) => { setRatingText(e.target.value); setRatingInvalid(false); }} />
        </Field>
        <Field label={t('field.rank')} hint={tournament.rankFromRating ? t('player.rankFromRatingPending') : undefined}>
          <select value={form.rank} onChange={(e) => set('rank', e.target.value)}>
            <option value="">{t('player.rankNone')}</option>
            {rankOptions.map((r) => <option key={r} value={r}>{formatRank(r, t)}</option>)}
          </select>
        </Field>
        {appOnly(t('field.rankShift'),
          <input type="number" min={-30} max={30} value={form.rankShift} onChange={(e) => set('rankShift', Number(e.target.value) || 0)} />)}
        <Field label={t('field.grade')}><input {...text('grade')} /></Field>

        {appOnly(t('field.country'), <input {...text('country')} />)}
        {appOnly(t('field.club'), <input {...text('club')} />)}
        {appOnly(t('field.nationality'), <input {...text('nationality')} />)}
        {appOnly(t('field.team'), <input {...text('team')} />)}
        {appOnly(t('field.coach'), <input {...text('coach')} />)}

        <fieldset className="field field-wide">
          <legend className="field-label">{t('field.preliminary')}</legend>
          <div className="inline-options">
            <label className={`checkbox${preliminaryBlocked ? ' disabled' : ''}`} title={preliminaryBlocked ? t('player.preliminaryBlocked') : undefined}>
              <input type="radio" name="registration" checked={form.preliminary} disabled={preliminaryBlocked} onChange={() => set('preliminary', true)} />
              <span>{t('player.registrationPreliminary')}</span>
            </label>
            <label className="checkbox"><input type="radio" name="registration" checked={!form.preliminary} onChange={() => set('preliminary', false)} /><span>{t('player.registrationFinal')}</span></label>
          </div>
          <span className="field-hint">{t('player.registrationHint')}{preliminaryBlocked && <> {t('player.preliminaryBlocked')}</>}</span>
        </fieldset>

        {rr && (
          <Field label={t('field.startingNumber')}
            hint={rr.frozen ? t('player.startingNumberFrozen') : !numberEditable ? t('player.startingNumberInactive') : t('player.startingNumberHint')}>
            <input inputMode="numeric" value={numberText} disabled={!numberEditable} onChange={(e) => setNumberText(e.target.value)} />
          </Field>
        )}
        {!pureRoundRobin && (
          <>
            <Field label={t('field.firstEligibleRound')} hint={t('player.joinRoundHint')}>
              <select value={form.firstEligibleRound ?? ''}
                onChange={(e) => set('firstEligibleRound', e.target.value === '' ? null : Number(e.target.value))}>
                {form.firstEligibleRound === null && <option value="">{t('player.joinRoundChoose')}</option>}
                {Array.from({ length: tournament.numberOfRounds }, (_, i) => i + 1).map((r) => (
                  <option key={r} value={r}>{t('round.label', { roman: roman(r), n: r })}</option>
                ))}
              </select>
            </Field>

            <fieldset className="field field-wide">
              <legend className="field-label">{t('field.availability')}</legend>
              {!isNew && tournament.stages.some((s) => s.frozenRosterIds?.includes(participant.id)) && (
                <span className="field-hint">{t('player.rosterRoundsLocked')}</span>
              )}
              <div className="inline-options">
                {Array.from({ length: tournament.numberOfRounds }, (_, i) => i + 1).map((round) => (
                  <Checkbox key={round} label={roman(round)}
                    disabled={round < (form.firstEligibleRound ?? 1) || (!isNew && isLockedRosterRound(tournament, participant.id, round))}
                    checked={round >= (form.firstEligibleRound ?? 1) && !form.notPlayingRounds.includes(round)} onChange={(v) => toggleRound(round, v)} />
                ))}
              </div>
            </fieldset>
          </>
        )}

        <Field label={t('field.comment')} wide><input {...text('comment')} /></Field>
        <button type="submit" hidden />
      </form>
      {!isNew && <p className="muted">{t('player.gamesCount', { count: games })} · {t('player.catalogHint')}</p>}
    </Modal>
  );
}

function CatalogSearch({ onPick }: { onPick: (entry: CatalogEntry) => void }) {
  const { t } = useI18n();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<CatalogEntry[]>([]);

  useEffect(() => {
    if (query.trim().length < 2) { setResults([]); return; }
    const handle = setTimeout(() => { api.searchCatalog(query).then(setResults, () => setResults([])); }, 250);
    return () => clearTimeout(handle);
  }, [query]);

  return (
    <div className="catalog-search">
      <Field label={t('catalog.searchLabel')} hint={t('catalog.searchHint')} wide>
        <input type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('catalog.searchPlaceholder')} autoFocus />
      </Field>
      {query.trim().length >= 2 && (
        results.length === 0
          ? <p className="muted">{t('catalog.noResults')}</p>
          : (
            <ul className="catalog-results">
              {results.map((entry) => (
                <li key={entry.id}>
                  <button type="button" className="link-button" onClick={() => { onPick(entry); setQuery(''); }}>
                    {entry.displayName}
                  </button>
                  <span className="muted"> {entry.rank && formatRank(entry.rank, t)} {entry.rating} {entry.club}</span>
                </li>
              ))}
            </ul>
          )
      )}
    </div>
  );
}
