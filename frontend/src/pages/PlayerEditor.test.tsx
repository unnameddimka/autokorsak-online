// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { api } from '../api/client';
import type { Participant, Tournament, TournamentView } from '../api/types';
import { I18nProvider } from '../i18n';
import { ConfirmProvider, ToastProvider } from '../components/common';
import { TournamentContext } from './TournamentLayout';
import { PlayerEditor } from './PlayerEditor';

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) { this.removeAttribute('open'); };
});
beforeEach(() => localStorage.setItem('autokorsak.locale', 'en'));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const player = (id: number, name: string, startingNumber: number | null = null): Participant => ({
  id, catalogEntryId: null, localFirstName: '', localSurname: name, firstName: '', surname: '', rating: 1000, rank: '',
  rankShift: 0, grade: '', country: '', club: '', nationality: '', team: '', coach: '', egdPin: 0, startScore: 0,
  preliminary: false, firstEligibleRound: 1, startingNumber, notPlayingRounds: [], comment: '', displayName: name,
});

function setup(target: Participant, frozen: boolean, system: 'roundRobin' | 'swiss' = 'roundRobin') {
  const others = [player(2, 'Bravo'), player(3, 'Charlie')];
  const tournament = {
    id: 't1', name: 'RR', system: 'roundRobin', legacyKind: null, numberOfRounds: 3, currentRound: 1, rankFromRating: false,
    participants: [target, ...others], rounds: [],
    stages: [{ startRound: 1, system, frozenRosterIds: frozen ? [1, 2, 3] : null, randomStartingNumbers: false }],
  } as unknown as Tournament;
  const view = {
    tournament, imported: false, gameCounts: { 1: frozen ? 1 : 0 }, systemLocked: frozen, defaultJoinRound: 1,
    roundRobin: {
      startRound: 1, endRound: 3, frozen, randomStartingNumbers: false,
      numbers: frozen ? { 1: 1, 2: 2, 3: 3 } : { 1: target.startingNumber, 2: null, 3: null }, eligible: [1, 2, 3], lockedBy: [],
    },
  } as unknown as TournamentView;
  const run = vi.fn(async (action: () => Promise<TournamentView>) => action());
  const update = vi.spyOn(api, 'updateParticipant').mockResolvedValue(view);
  const utils = render(
    <I18nProvider><ToastProvider><ConfirmProvider>
      <TournamentContext.Provider value={{ view, tournament, standings: null, run }}>
        <PlayerEditor participant={target} onClose={() => {}} />
      </TournamentContext.Provider>
    </ConfirmProvider></ToastProvider></I18nProvider>,
  );
  const numberInput = () => utils.getByLabelText(/^Starting number/) as HTMLInputElement;
  const save = () => fireEvent.click(utils.getByRole('button', { name: 'Save' }));
  return { ...utils, update, numberInput, save };
}

describe('player card starting number', () => {
  it('saves a newly typed positive number', async () => {
    const u = setup(player(1, 'Alpha'), false);
    fireEvent.change(u.numberInput(), { target: { value: '1' } });
    u.save();
    await waitFor(() => expect(u.update).toHaveBeenCalledTimes(1));
    expect(u.update.mock.calls[0][2]).toMatchObject({ startingNumber: 1 });
  });

  it('changes an already assigned unfrozen number', async () => {
    const u = setup(player(1, 'Alpha', 2), false);
    expect(u.numberInput().value).toBe('2');
    fireEvent.change(u.numberInput(), { target: { value: '12' } });
    u.save();
    await waitFor(() => expect(u.update).toHaveBeenCalledTimes(1));
    expect(u.update.mock.calls[0][2]).toMatchObject({ startingNumber: 12 });
  });

  it('saves an unrelated edit while the number is frozen, sending the number as unchanged', async () => {
    const u = setup(player(1, 'Alpha'), true);
    expect(u.numberInput().disabled).toBe(true);
    expect(u.numberInput().value).toBe('1');
    fireEvent.change(u.getByLabelText('Comment'), { target: { value: 'new comment' } });
    u.save();
    await waitFor(() => expect(u.update).toHaveBeenCalledTimes(1));
    expect(u.update.mock.calls[0][2]).toMatchObject({ comment: 'new comment', startingNumber: null });
  });

  it('rejects a typed 0 instead of clearing the number', async () => {
    const u = setup(player(1, 'Alpha', 3), false);
    fireEvent.change(u.numberInput(), { target: { value: '0' } });
    u.save();
    await waitFor(() => expect(u.getByText(/is not a valid starting number/)).toBeTruthy());
    expect(u.update).not.toHaveBeenCalled();
  });

  it('clears the number when the field is emptied', async () => {
    const u = setup(player(1, 'Alpha', 3), false);
    fireEvent.change(u.numberInput(), { target: { value: '' } });
    u.save();
    await waitFor(() => expect(u.update).toHaveBeenCalledTimes(1));
    expect(u.update.mock.calls[0][2]).toMatchObject({ startingNumber: 0 });
  });
});

describe('player card rating (spec 0003 §5)', () => {
  const ratingInput = (u: ReturnType<typeof setup>) => u.getByLabelText(/^Rating/) as HTMLInputElement;

  it('shows 0, and an edit from 0 to 50 sends exactly 50', async () => {
    const u = setup({ ...player(1, 'Alpha'), rating: 0 }, false);
    expect(ratingInput(u).value).toBe('0');
    fireEvent.change(ratingInput(u), { target: { value: '' } }); // the referee clears the field first
    expect(ratingInput(u).value).toBe(''); // no zero is reinserted while typing
    fireEvent.change(ratingInput(u), { target: { value: '50' } });
    expect(ratingInput(u).value).toBe('50');
    u.save();
    await waitFor(() => expect(u.update).toHaveBeenCalledTimes(1));
    expect(u.update.mock.calls[0][2]).toMatchObject({ rating: 50 });
  });

  it('keeps negative ratings and refuses an empty or invalid rating with a message', async () => {
    const u = setup({ ...player(1, 'Alpha'), rating: -200 }, false);
    expect(ratingInput(u).value).toBe('-200');
    fireEvent.change(ratingInput(u), { target: { value: '' } });
    u.save();
    await waitFor(() => expect(u.getAllByText(/Rating must be between/).length).toBeGreaterThan(0));
    expect(ratingInput(u).getAttribute('aria-invalid')).toBe('true');
    fireEvent.change(ratingInput(u), { target: { value: '12a' } });
    u.save();
    expect(u.update).not.toHaveBeenCalled();
  });
});

describe('player card in a pure round robin (spec 0003 §2.3)', () => {
  it('shows no joining-round or availability controls', () => {
    const u = setup(player(1, 'Alpha'), false);
    expect(u.queryByLabelText(/^Joins from round/)).toBeNull();
    expect(u.queryByText('Plays in rounds')).toBeNull();
  });

  it('keeps them for other systems', () => {
    const u = setup(player(1, 'Alpha'), false, 'swiss');
    expect(u.getByLabelText(/^Joins from round/)).toBeTruthy();
    expect(u.getByText('Plays in rounds')).toBeTruthy();
  });
});

describe('app-only explanation (spec 0003 §6)', () => {
  it('opens from a button (mouse, touch or keyboard) and closes with Escape', () => {
    const u = setup(player(1, 'Alpha'), false);
    const buttons = u.getAllByRole('button', { name: /^App only/ });
    expect(buttons.length).toBe(6); // rank shift, country, club, nationality, team, coach
    expect(u.getByLabelText('Country').tagName).toBe('INPUT'); // the badge never takes over the field's label
    expect(u.queryByRole('note')).toBeNull();
    fireEvent.click(buttons[1]);
    expect(buttons[1].getAttribute('aria-expanded')).toBe('true');
    expect(u.getByRole('note').textContent).toMatch(/not carried over to the XML file/);
    expect(u.getByRole('note').textContent).toMatch(/stays saved in this application/);
    fireEvent.keyDown(buttons[1], { key: 'Escape' });
    expect(u.queryByRole('note')).toBeNull();
  });
});

describe('player card transliteration (spec 0003 §4)', () => {
  // Ukrainian names are unicode escapes (Stetsenko Oleksii, Kobzar Hlib): the localization test keeps Cyrillic out of
  // components and their tests; the transliteration table itself is tested in transliteration.test.ts.
  const named = (localSurname: string, localFirstName: string, surname = '', firstName = ''): Participant =>
    ({ ...player(1, 'Alpha'), localSurname, localFirstName, surname, firstName });
  const value = (u: ReturnType<typeof setup>, label: RegExp) => (u.getByLabelText(label) as HTMLInputElement).value;
  const click = (u: ReturnType<typeof setup>) => fireEvent.click(u.getByRole('button', { name: 'Transliterate' }));

  it('fills empty English fields on click, without saving', async () => {
    const u = setup(named('\u0421\u0442\u0435\u0446\u0435\u043d\u043a\u043e', '\u041e\u043b\u0435\u043a\u0441\u0456\u0439'), false);
    click(u);
    await waitFor(() => expect(value(u, /^Surname \(English\)/)).toBe('Stetsenko'));
    expect(value(u, /^First name \(English\)/)).toBe('Oleksii');
    expect(value(u, /^Surname \(local\)/)).toBe('\u0421\u0442\u0435\u0446\u0435\u043d\u043a\u043e');
    expect(u.update).not.toHaveBeenCalled();
  });

  it('asks before overwriting either English field; cancel changes nothing, confirm replaces both', async () => {
    const u = setup(named('\u0421\u0442\u0435\u0446\u0435\u043d\u043a\u043e', '\u041e\u043b\u0435\u043a\u0441\u0456\u0439', 'Stecenko', ''), false);
    click(u);
    const dialog = () => [...u.baseElement.querySelectorAll('dialog[open]')].find((d) => d.textContent?.includes('Replace the English name?')) as HTMLElement;
    await waitFor(() => expect(dialog()).toBeTruthy());
    fireEvent.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(dialog()).toBeUndefined());
    expect(value(u, /^Surname \(English\)/)).toBe('Stecenko');
    expect(value(u, /^First name \(English\)/)).toBe('');

    click(u);
    await waitFor(() => expect(dialog()).toBeTruthy());
    fireEvent.click(within(dialog()).getByRole('button', { name: 'Replace' }));
    await waitFor(() => expect(value(u, /^Surname \(English\)/)).toBe('Stetsenko'));
    expect(value(u, /^First name \(English\)/)).toBe('Oleksii');
  });

  it('transliterates a full name kept in one field as entered and leaves the other English field alone', async () => {
    const u = setup(named('\u041a\u043e\u0431\u0437\u0430\u0440 \u0413\u043b\u0456\u0431', '', '', 'Hlib'), false);
    click(u);
    await waitFor(() => expect(value(u, /^Surname \(English\)/)).toBe('Kobzar Hlib'));
    expect(value(u, /^First name \(English\)/)).toBe('Hlib');
  });

  it('explains an empty source and changes nothing', async () => {
    const u = setup(named('', '', 'Smith', 'John'), false);
    click(u);
    await waitFor(() => expect(u.getByText(/Enter the Ukrainian surname or first name first/)).toBeTruthy());
    expect(value(u, /^Surname \(English\)/)).toBe('Smith');
  });
});
