// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { api, ApiError } from '../api/client';
import type { Tournament, TournamentView } from '../api/types';
import { I18nProvider } from '../i18n';
import { ConfirmProvider, ToastProvider } from '../components/common';
import { TournamentContext } from './TournamentLayout';
import { RoundRobinPanel } from './RoundRobinPanel';

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) { this.removeAttribute('open'); };
});
beforeEach(() => localStorage.setItem('autokorsak.locale', 'en'));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const player = (id: number, name: string) => ({
  id, catalogEntryId: null, localFirstName: '', localSurname: name, firstName: '', surname: '', rating: 1000, rank: '',
  rankShift: 0, grade: '', country: '', club: '', nationality: '', team: '', coach: '', egdPin: 0, startScore: 0,
  preliminary: false, firstEligibleRound: 1, startingNumber: null, notPlayingRounds: [], comment: '', displayName: name,
});

function setup() {
  const tournament = {
    id: 't1', name: 'RR', system: 'roundRobin', legacyKind: null, numberOfRounds: 5, currentRound: 1, allowDraws: false,
    updatedAt: '2026-10-16T00:00:00Z',
    participants: [player(1, 'Alpha'), player(2, 'Bravo'), player(3, 'Charlie'), player(4, 'Delta')],
    rounds: [], stages: [{ startRound: 1, system: 'roundRobin', frozenRosterIds: null, randomStartingNumbers: false }],
  } as unknown as Tournament;
  const view = {
    tournament, imported: false, gameCounts: {}, systemLocked: false, defaultJoinRound: 1,
    roundRobin: { startRound: 1, endRound: 5, frozen: false, randomStartingNumbers: false, numbers: { 1: 2, 2: null, 3: null, 4: null }, eligible: [1, 2, 3, 4], lockedBy: [] },
  } as unknown as TournamentView;
  const run = vi.fn(async (action: () => Promise<TournamentView>) => action());
  const utils = render(
    <I18nProvider><ToastProvider><ConfirmProvider>
      <TournamentContext.Provider value={{ view, tournament, standings: null, run }}>
        <RoundRobinPanel />
      </TournamentContext.Provider>
    </ConfirmProvider></ToastProvider></I18nProvider>,
  );
  const openDialog = (text: string) => waitFor(() => {
    const d = [...utils.baseElement.querySelectorAll('dialog[open]')].find((x) => x.textContent?.includes(text));
    if (!d) throw new Error(`dialog "${text}" not shown`);
    return d as HTMLElement;
  });
  return { ...utils, view, openDialog };
}

const adjustment = new ApiError('generate.roundCountAdjustment', { players: 4, required: 3, current: 5 }, 409);

describe('round-robin generation dialogs', () => {
  it('asks for the number source first, then the round count, then sends one final request', async () => {
    const u = setup();
    const generate = vi.spyOn(api, 'generate')
      .mockRejectedValueOnce(adjustment)
      .mockResolvedValueOnce({ ...u.view, result: { created: 6, replaced: 0, kept: 0 } } as TournamentView);
    fireEvent.click(u.getByLabelText(/Assign starting numbers randomly/));
    fireEvent.click(u.getByRole('button', { name: 'Generate pairings' }));

    const choice = await u.openDialog('replaces ALL entered numbers');
    expect(generate).not.toHaveBeenCalled(); // nothing is sent before the number choice
    fireEvent.click(within(choice).getByRole('button', { name: 'Draw randomly and overwrite numbers' }));

    const adjust = await u.openDialog('needs 3 rounds, but 5 are configured');
    expect(generate).toHaveBeenCalledTimes(1); // the server only reported the needed confirmation; nothing written
    fireEvent.click(within(adjust).getByRole('button', { name: 'Set 3 rounds and draw' }));

    await waitFor(() => expect(generate).toHaveBeenCalledTimes(2));
    expect(generate.mock.calls[1][2]).toMatchObject({ source: 'random', roundCount: 3 });
  });

  it('cancelling either dialog sends no committing request', async () => {
    const u = setup();
    const generate = vi.spyOn(api, 'generate').mockRejectedValue(adjustment);
    fireEvent.click(u.getByLabelText(/Assign starting numbers randomly/));
    fireEvent.click(u.getByRole('button', { name: 'Generate pairings' }));
    fireEvent.click(within(await u.openDialog('replaces ALL entered numbers')).getByRole('button', { name: 'Cancel' }));
    expect(generate).not.toHaveBeenCalled();

    fireEvent.click(u.getByRole('button', { name: 'Generate pairings' }));
    fireEvent.click(within(await u.openDialog('replaces ALL entered numbers')).getByRole('button', { name: 'Use entered numbers' }));
    fireEvent.click(within(await u.openDialog('needs 3 rounds')).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(generate).toHaveBeenCalledTimes(1));
    expect(generate.mock.calls[0][2]).toMatchObject({ source: 'entered', roundCount: null });
  });

  it('saves several table edits in one batch request', async () => {
    const u = setup();
    const save = vi.spyOn(api, 'saveStartingNumbers').mockResolvedValue(u.view);
    fireEvent.change(u.getByLabelText('Starting number: Bravo'), { target: { value: '2' } });
    fireEvent.change(u.getByLabelText('Starting number: Charlie'), { target: { value: '2' } }); // temporary duplicate is fine
    fireEvent.click(u.getByRole('button', { name: 'Save starting numbers' }));
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][1]).toEqual([{ participantId: 2, number: 2 }, { participantId: 3, number: 2 }]);
  });
});
