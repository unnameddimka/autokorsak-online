// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
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

function setup(target: Participant, frozen: boolean) {
  const others = [player(2, 'Bravo'), player(3, 'Charlie')];
  const tournament = {
    id: 't1', name: 'RR', system: 'roundRobin', legacyKind: null, numberOfRounds: 3, currentRound: 1, rankFromRating: false,
    participants: [target, ...others], rounds: [],
    stages: [{ startRound: 1, system: 'roundRobin', frozenRosterIds: frozen ? [1, 2, 3] : null, randomStartingNumbers: false }],
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
  const numberInput = () => utils.baseElement.querySelector('dialog label.field input[inputmode="numeric"]') as HTMLInputElement;
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
