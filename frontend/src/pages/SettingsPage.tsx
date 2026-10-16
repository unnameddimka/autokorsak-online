import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useBlocker, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { CriterionSetting, LayoutColumn, SettingsInput, Tournament, TournamentSystem } from '../api/types';
import { Checkbox, Field, Notice, NotImplementedBadge, useConfirm, useToast } from '../components/common';
import { useI18n } from '../i18n';
import type { MessageKey } from '../i18n/en';
import { errorMessage } from '../format';
import { useTournament } from './TournamentLayout';

const SYSTEMS: TournamentSystem[] = ['roundRobin', 'scheveningen', 'swiss', 'mcMahon'];
const CALCULATED = new Set(['points', 'wins', 'sos', 'sodos', 'sosos']);
const ALL_CRITERIA = ['points', 'wins', 'sos', 'sodos', 'sosos', 'pointsExcludingAbsence', 'extraPoints', 'upDown', 'directEncounter'];
const ALL_COLUMNS = ['place', 'name', 'country', 'club', 'team', 'coach', 'grade', 'rank', 'rating', 'group', 'rounds', 'criteria', 'newRating'];
const BOARD_SIZES = [9, 13, 19];
/** Legacy `Kind` value confirmed for Swiss (docs/decisions.md); other legacy codes are not identified yet. */
const LEGACY_SWISS_KIND = 2;

type Tab = 'general' | 'criteria' | 'layout' | 'pairing' | 'calculations';

function defaults(): SettingsInput {
  return {
    name: '', description: '', system: 'swiss', boardSize: 19, firstMoveColor: 'black', numberOfRounds: 5,
    handicapUsed: false, allowDraws: false, rankFromRating: false, customizeCalculation: false,
    criteria: ALL_CRITERIA.map((key) => ({ key, enabled: ['wins', 'sos', 'sodos', 'sosos'].includes(key) })),
    layout: { columns: ALL_COLUMNS.map((key) => ({ key, visible: key !== 'newRating' })), showColors: false, hideEmptyColumns: true },
    pairingConstraints: { avoidSameCountry: false, avoidSameClub: false, avoidSameNationality: false, avoidSameTeam: false, avoidSameCoach: false },
    calculation: { skippedRoundScoring: 'addHalf', addNothingToOwnCoefficients: false, addMaximumToOpponentCoefficients: false, topGroupOnly: false },
  };
}

function fromTournament(t: Tournament): SettingsInput {
  const { name, description, system, boardSize, firstMoveColor, numberOfRounds, handicapUsed, allowDraws, rankFromRating,
    customizeCalculation, criteria, layout, pairingConstraints, calculation } = t;
  return structuredClone({ name, description, system, boardSize, firstMoveColor, numberOfRounds, handicapUsed, allowDraws,
    rankFromRating, customizeCalculation, criteria, layout, pairingConstraints, calculation });
}

export function NewTournamentPage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  return (
    <section className="page">
      <div className="page-header"><h1>{t('tournament.new')}</h1></div>
      <SettingsForm
        initial={defaults()}
        legacyKind={null}
        systemLocked={false}
        submitLabel={t('tournament.create')}
        onSubmit={async (input) => {
          const view = await api.createTournament(input);
          navigate(`/t/${view.tournament.id}/players`);
        }}
        onCancel={() => navigate('/')}
      />
    </section>
  );
}

export function TournamentSettingsPage() {
  const { tournament, view, run } = useTournament();
  const { t } = useI18n();
  const toast = useToast();
  return (
    <SettingsForm
      key={tournament.updatedAt}
      initial={fromTournament(tournament)}
      legacyKind={tournament.legacyKind}
      systemLocked={view.systemLocked}
      submitLabel={t('common.save')}
      onSubmit={async (input) => {
        await run(() => api.updateSettings(tournament.id, input));
        toast.success(t('settings.saved'));
      }}
    />
  );
}

function SettingsForm({ initial, legacyKind, systemLocked, submitLabel, onSubmit, onCancel }: {
  initial: SettingsInput;
  legacyKind: number | null;
  systemLocked: boolean;
  submitLabel: string;
  onSubmit: (input: SettingsInput) => Promise<void>;
  onCancel?: () => void;
}) {
  const { t, tk } = useI18n();
  const [form, setForm] = useState<SettingsInput>(initial);
  const [tab, setTab] = useState<Tab>('general');
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const dirty = JSON.stringify(form) !== JSON.stringify(initial);
  const confirm = useConfirm();
  const submitting = useRef(false);

  // Guard unsaved edits against in-app navigation (tabs, header links); the beforeunload handler covers reloads.
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    dirty && !submitting.current && currentLocation.pathname !== nextLocation.pathname);
  const blockerRef = useRef(blocker);
  blockerRef.current = blocker;
  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    confirm({ title: t('settings.leaveTitle'), message: t('settings.leaveMessage'), confirmLabel: t('settings.leaveConfirm'), danger: true })
      .then((leave) => { if (leave) blockerRef.current.proceed?.(); else blockerRef.current.reset?.(); });
  // Runs once per blocked navigation; re-running on a language change would open a second dialog.
  }, [blocker.state]);
  const set = <K extends keyof SettingsInput>(key: K, value: SettingsInput[K]) => setForm((f) => ({ ...f, [key]: value }));

  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  useEffect(() => {
    if (!form.customizeCalculation && tab === 'calculations') setTab('general');
  }, [form.customizeCalculation, tab]);

  async function submit() {
    setSaving(true);
    setError(null);
    try {
      submitting.current = true;
      await onSubmit(form);
    } catch (e) {
      submitting.current = false;
      setError(e);
    } finally {
      setSaving(false);
    }
  }

  const tabs: [Tab, MessageKey][] = [
    ['general', 'settings.tab.general'],
    ['criteria', 'settings.tab.criteria'],
    ['layout', 'settings.tab.layout'],
    ['pairing', 'settings.tab.pairing'],
  ];
  if (form.customizeCalculation) tabs.push(['calculations', 'settings.tab.calculations']);

  return (
    <form className="settings" onSubmit={(e) => { e.preventDefault(); submit(); }}>
      <div className="tabs secondary" role="tablist">
        {tabs.map(([id, label]) => (
          <button key={id} type="button" role="tab" aria-selected={tab === id} className={tab === id ? 'active' : ''} onClick={() => setTab(id)}>
            {t(label)}
          </button>
        ))}
      </div>

      {error !== null && <Notice kind="error">{errorMessage(error, tk)}</Notice>}

      <div className="settings-panel">
        {tab === 'general' && <GeneralTab form={form} set={set} legacyKind={legacyKind} systemLocked={systemLocked} />}
        {tab === 'criteria' && (
          <OrderedList
            intro={t('settings.criteriaIntro')}
            items={form.criteria.filter((c) => c.key !== 'points' || form.system === 'mcMahon')}
            onChange={(visibleItems) => {
              const hidden = form.criteria.filter((c) => !visibleItems.some((v) => v.key === c.key));
              set('criteria', [...visibleItems, ...hidden] as CriterionSetting[]);
            }}
            isOn={(c) => (c as CriterionSetting).enabled}
            toggle={(c, on) => ({ ...(c as CriterionSetting), enabled: on })}
            label={(c) => (
              <>
                <strong>{tk(`criterion.${c.key}.short`, 'criterion.legacy.short', { token: c.key.replace(/^legacy:/, '') })}</strong>
                {' — '}
                {tk(`criterion.${c.key}.description`, 'criterion.legacy.description', { token: c.key.replace(/^legacy:/, '') })}
                {!CALCULATED.has(c.key) && <> <NotImplementedBadge label={t('settings.notCalculated')} /></>}
              </>
            )}
          />
        )}
        {tab === 'layout' && (
          <>
            <OrderedList
              intro={t('settings.layoutIntro')}
              items={form.layout.columns}
              onChange={(columns) => set('layout', { ...form.layout, columns: columns as LayoutColumn[] })}
              isOn={(c) => (c as LayoutColumn).visible}
              toggle={(c, on) => ({ ...(c as LayoutColumn), visible: on || c.key === 'name' })}
              locked={(c) => c.key === 'name'}
              label={(c) => (
                <>
                  <strong>{t(`settings.column.${c.key}` as MessageKey)}</strong>
                  {' — '}{t(`settings.column.${c.key}.description` as MessageKey)}
                  {c.key === 'newRating' && <> <NotImplementedBadge /></>}
                </>
              )}
            />
            <div className="stack">
              <Checkbox label={t('settings.showColors')} checked={form.layout.showColors} onChange={(v) => set('layout', { ...form.layout, showColors: v })} />
              <Checkbox label={t('settings.hideEmptyColumns')} checked={form.layout.hideEmptyColumns} onChange={(v) => set('layout', { ...form.layout, hideEmptyColumns: v })} />
            </div>
          </>
        )}
        {tab === 'pairing' && (
          <div className="stack">
            <Notice>{t('settings.pairingIntro')}</Notice>
            {([
              ['avoidSameCountry', 'settings.avoidSameCountry'],
              ['avoidSameClub', 'settings.avoidSameClub'],
              ['avoidSameNationality', 'settings.avoidSameNationality'],
              ['avoidSameTeam', 'settings.avoidSameTeam'],
              ['avoidSameCoach', 'settings.avoidSameCoach'],
            ] as const).map(([key, label]) => (
              <Checkbox key={key} label={t(label)} checked={form.pairingConstraints[key]}
                onChange={(v) => set('pairingConstraints', { ...form.pairingConstraints, [key]: v })} />
            ))}
          </div>
        )}
        {tab === 'calculations' && (
          <div className="stack">
            <Notice>{t('settings.calculationsIntro')}</Notice>
            <fieldset className="field">
              <legend className="field-label">{t('settings.skippedRounds')}</legend>
              {([
                ['addHalf', 'settings.skipped.addHalf'],
                ['alternate', 'settings.skipped.alternate'],
                ['addNothing', 'settings.skipped.addNothing'],
              ] as const).map(([value, label]) => (
                <label key={value} className="checkbox">
                  <input type="radio" name="skipped" checked={form.calculation.skippedRoundScoring === value}
                    onChange={() => set('calculation', { ...form.calculation, skippedRoundScoring: value })} />
                  <span>{t(label)}</span>
                </label>
              ))}
            </fieldset>
            <Checkbox label={t('settings.addNothingToOwn')} checked={form.calculation.addNothingToOwnCoefficients}
              onChange={(v) => set('calculation', { ...form.calculation, addNothingToOwnCoefficients: v })} />
            <Checkbox label={t('settings.addMaxToOpponents')} checked={form.calculation.addMaximumToOpponentCoefficients}
              onChange={(v) => set('calculation', { ...form.calculation, addMaximumToOpponentCoefficients: v })} />
            <hr />
            <Checkbox label={t('settings.topGroupOnly')} hint={t('settings.topGroupOnlyHint')} checked={form.calculation.topGroupOnly}
              onChange={(v) => set('calculation', { ...form.calculation, topGroupOnly: v })} />
          </div>
        )}
      </div>

      <div className="form-actions">
        {dirty && <span className="muted">{t('settings.unsaved')}</span>}
        {onCancel && <button type="button" onClick={onCancel}>{t('common.cancel')}</button>}
        {!onCancel && dirty && <button type="button" onClick={() => setForm(initial)}>{t('settings.discard')}</button>}
        <button type="submit" className="primary" disabled={saving || (!dirty && !onCancel)}>{saving ? t('save.saving') : submitLabel}</button>
      </div>
    </form>
  );
}

function GeneralTab({ form, set, legacyKind, systemLocked }: {
  form: SettingsInput;
  set: <K extends keyof SettingsInput>(key: K, value: SettingsInput[K]) => void;
  legacyKind: number | null;
  systemLocked: boolean;
}) {
  const { t } = useI18n();
  const boardSizes = BOARD_SIZES.includes(form.boardSize) ? BOARD_SIZES : [...BOARD_SIZES, form.boardSize].sort((a, b) => a - b);

  function changeSystem(system: TournamentSystem | null) {
    // Total points exist only in McMahon; elsewhere the same slot is "wins".
    const criteria = form.criteria.map((c) => ({ ...c }));
    const points = criteria.find((c) => c.key === 'points');
    const wins = criteria.find((c) => c.key === 'wins');
    if (system === 'mcMahon' && points && wins && wins.enabled && !points.enabled) {
      points.enabled = true;
      wins.enabled = false;
      criteria.splice(criteria.indexOf(points), 1);
      criteria.splice(criteria.indexOf(wins), 0, points);
    }
    if (system !== 'mcMahon' && points?.enabled && wins && !wins.enabled) {
      wins.enabled = true;
      criteria.splice(criteria.indexOf(wins), 1);
      criteria.splice(criteria.indexOf(points), 0, wins);
    }
    set('system', system);
    set('criteria', criteria);
  }

  return (
    <div className="form-grid">
      <Field label={t('settings.name')} wide><input value={form.name} onChange={(e) => set('name', e.target.value)} required maxLength={200} /></Field>
      <Field label={t('settings.description')} wide><input value={form.description} onChange={(e) => set('description', e.target.value)} /></Field>

      <fieldset className="field">
        <legend className="field-label">{t('settings.system')}</legend>
        {SYSTEMS.map((s) => (
          <label key={s} className="checkbox">
            <input type="radio" name="system" checked={form.system === s} disabled={systemLocked} onChange={() => changeSystem(s)} />
            <span>{t(`system.${s}` as MessageKey)}</span>
          </label>
        ))}
        {legacyKind !== null && legacyKind !== LEGACY_SWISS_KIND && (
          <label className="checkbox">
            <input type="radio" name="system" checked={form.system === null} disabled={systemLocked} onChange={() => changeSystem(null)} />
            <span>{t('system.legacyUnverified', { code: legacyKind })}</span>
          </label>
        )}
        {systemLocked && <span className="field-hint">{t('settings.systemLocked')}</span>}
        <span className="field-hint">{form.system === null || form.system === 'swiss' ? t('settings.systemHint') : t('settings.systemExportHint')}</span>
      </fieldset>

      <div className="stack">
        <Field label={t('settings.boardSize')}>
          <select value={form.boardSize} onChange={(e) => set('boardSize', Number(e.target.value))}>
            {boardSizes.map((s) => <option key={s} value={s}>{s}×{s}</option>)}
          </select>
        </Field>
        <fieldset className="field">
          <legend className="field-label">{t('settings.firstMove')}</legend>
          <div className="inline-options">
            <label className="checkbox"><input type="radio" name="first" checked={form.firstMoveColor === 'black'} onChange={() => set('firstMoveColor', 'black')} /><span>{t('color.black')}</span></label>
            <label className="checkbox"><input type="radio" name="first" checked={form.firstMoveColor === 'white'} onChange={() => set('firstMoveColor', 'white')} /><span>{t('color.white')}</span></label>
          </div>
        </fieldset>
        <Field label={t('settings.rounds')}>
          <input type="number" min={1} max={50} value={form.numberOfRounds} onChange={(e) => set('numberOfRounds', Number(e.target.value) || 1)} />
        </Field>
      </div>

      <div className="stack field-wide">
        <Checkbox label={t('settings.handicapUsed')} hint={t('settings.handicapUsedHint')} checked={form.handicapUsed} onChange={(v) => set('handicapUsed', v)} />
        <Checkbox label={t('settings.allowDraws')} checked={form.allowDraws} onChange={(v) => set('allowDraws', v)} />
        <Checkbox label={t('settings.rankFromRating')} hint={t('settings.rankFromRatingHint')} checked={form.rankFromRating} onChange={(v) => set('rankFromRating', v)} />
        <Checkbox label={t('settings.customizeCalculation')} hint={t('settings.customizeCalculationHint')} checked={form.customizeCalculation} onChange={(v) => set('customizeCalculation', v)} />
      </div>
    </div>
  );
}

interface Keyed { key: string }

/** Enable/disable and reorder items by drag-and-drop or with accessible move up/down buttons. */
function OrderedList({ intro, items, onChange, isOn, toggle, label, locked }: {
  intro: string;
  items: Keyed[];
  onChange: (items: Keyed[]) => void;
  isOn: (item: Keyed) => boolean;
  toggle: (item: Keyed, on: boolean) => Keyed;
  label: (item: Keyed) => ReactNode;
  locked?: (item: Keyed) => boolean;
}) {
  const { t } = useI18n();
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const move = (from: number, to: number) => {
    if (to < 0 || to >= items.length || from === to) return;
    const next = [...items];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    onChange(next);
  };
  return (
    <div>
      <p className="muted">{intro}</p>
      <ol className="ordered-list">
        {items.map((item, i) => (
          <li
            key={item.key}
            draggable
            onDragStart={() => setDragIndex(i)}
            onDragOver={(e) => e.preventDefault()}
            onDrop={() => { if (dragIndex !== null) move(dragIndex, i); setDragIndex(null); }}
            className={dragIndex === i ? 'dragging' : undefined}
          >
            <span className="drag-handle" aria-hidden="true">⋮⋮</span>
            <label className="checkbox grow">
              <input type="checkbox" checked={isOn(item)} disabled={locked?.(item)}
                onChange={(e) => onChange(items.map((x) => (x.key === item.key ? toggle(x, e.target.checked) : x)))} />
              <span>{label(item)}</span>
            </label>
            <button type="button" className="icon-button" onClick={() => move(i, i - 1)} disabled={i === 0} aria-label={t('settings.moveUp')}>↑</button>
            <button type="button" className="icon-button" onClick={() => move(i, i + 1)} disabled={i === items.length - 1} aria-label={t('settings.moveDown')}>↓</button>
          </li>
        ))}
      </ol>
    </div>
  );
}
