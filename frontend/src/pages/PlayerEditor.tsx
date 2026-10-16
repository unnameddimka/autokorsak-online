import { useEffect, useState, type ChangeEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { CatalogEntry, Participant, ParticipantInput } from '../api/types';
import { Checkbox, Field, Modal, Notice, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import { errorMessage, formatRank, RANK_OPTIONS, roman } from '../format';
import { useTournament } from './TournamentLayout';

const EMPTY: ParticipantInput = {
  localFirstName: '', localSurname: '', firstName: '', surname: '', rating: 0, rank: '', rankShift: 0, grade: '',
  country: '', club: '', nationality: '', team: '', coach: '', preliminary: false, notPlayingRounds: [], comment: '',
  catalogEntryId: null,
};

function toInput(p: Participant): ParticipantInput {
  const { localFirstName, localSurname, firstName, surname, rating, rank, rankShift, grade, country, club, nationality,
    team, coach, preliminary, notPlayingRounds, comment, catalogEntryId } = p;
  return { localFirstName, localSurname, firstName, surname, rating, rank, rankShift, grade, country, club, nationality,
    team, coach, preliminary, notPlayingRounds: [...notPlayingRounds], comment, catalogEntryId };
}

/** Adds or edits a tournament participant. Changes never reach the shared catalog until an explicit upsert. */
export function PlayerEditor({ participant, onClose }: { participant: Participant | null; onClose: () => void }) {
  const { tournament, view, run } = useTournament();
  const { t, tk } = useI18n();
  const toast = useToast();
  const confirm = useConfirm();
  const [form, setForm] = useState<ParticipantInput>(() => (participant ? toInput(participant) : EMPTY));
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const isNew = participant === null;
  const games = participant ? view.gameCounts[String(participant.id)] ?? 0 : 0;
  // A finally registered player with pairings cannot become preliminary (the server enforces this too).
  const preliminaryBlocked = !isNew && !participant.preliminary && games > 0;

  const set = <K extends keyof ParticipantInput>(key: K, value: ParticipantInput[K]) => setForm((f) => ({ ...f, [key]: value }));
  const text = (key: 'localSurname' | 'localFirstName' | 'surname' | 'firstName' | 'grade' | 'country' | 'club' | 'nationality' | 'team' | 'coach' | 'comment') => ({
    value: form[key],
    onChange: (e: ChangeEvent<HTMLInputElement>) => set(key, e.target.value),
  });

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await run(() => (isNew ? api.addParticipant(tournament.id, form) : api.updateParticipant(tournament.id, participant.id, form)));
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
  }

  const toggleRound = (round: number, plays: boolean) =>
    set('notPlayingRounds', plays ? form.notPlayingRounds.filter((r) => r !== round) : [...form.notPlayingRounds, round].sort((a, b) => a - b));

  const appOnly = <span className="app-only" title={t('player.appOnlyHint')}>{t('player.appOnly')}</span>;
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
        <Field label={t('field.surname')}><input {...text('surname')} lang="en" /></Field>
        <Field label={t('field.firstName')}><input {...text('firstName')} lang="en" /></Field>

        <Field label={t('field.rating')}>
          <input type="number" value={form.rating} onChange={(e) => set('rating', Number(e.target.value) || 0)} />
        </Field>
        <Field label={t('field.rank')} hint={tournament.rankFromRating ? t('player.rankFromRatingPending') : undefined}>
          <select value={form.rank} onChange={(e) => set('rank', e.target.value)}>
            <option value="">{t('player.rankNone')}</option>
            {rankOptions.map((r) => <option key={r} value={r}>{formatRank(r, t)}</option>)}
          </select>
        </Field>
        <Field label={<>{t('field.rankShift')} {appOnly}</>}>
          <input type="number" min={-30} max={30} value={form.rankShift} onChange={(e) => set('rankShift', Number(e.target.value) || 0)} />
        </Field>
        <Field label={t('field.grade')}><input {...text('grade')} /></Field>

        <Field label={<>{t('field.country')} {appOnly}</>}><input {...text('country')} /></Field>
        <Field label={<>{t('field.club')} {appOnly}</>}><input {...text('club')} /></Field>
        <Field label={<>{t('field.nationality')} {appOnly}</>}><input {...text('nationality')} /></Field>
        <Field label={<>{t('field.team')} {appOnly}</>}><input {...text('team')} /></Field>
        <Field label={<>{t('field.coach')} {appOnly}</>}><input {...text('coach')} /></Field>

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

        <fieldset className="field field-wide">
          <legend className="field-label">{t('field.availability')}</legend>
          <div className="inline-options">
            {Array.from({ length: tournament.numberOfRounds }, (_, i) => i + 1).map((round) => (
              <Checkbox key={round} label={roman(round)} checked={!form.notPlayingRounds.includes(round)} onChange={(v) => toggleRound(round, v)} />
            ))}
          </div>
        </fieldset>

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
                  <span className="muted"> {entry.rank && formatRank(entry.rank, t)} {entry.rating || ''} {entry.club}</span>
                </li>
              ))}
            </ul>
          )
      )}
    </div>
  );
}
