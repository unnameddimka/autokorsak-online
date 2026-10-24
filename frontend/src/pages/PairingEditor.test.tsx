// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { api } from '../api/client';
import type { Pairing, Tournament, TournamentView } from '../api/types';
import { I18nProvider } from '../i18n';
import { ConfirmProvider, ToastProvider } from '../components/common';
import { TournamentContext } from './TournamentLayout';
import { PairingEditor } from './PairingEditor';

beforeAll(() => {
  // jsdom does not implement modal dialogs.
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) { this.removeAttribute('open'); };
});

beforeEach(() => localStorage.setItem('autokorsak.locale', 'en'));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const player = (id: number, name: string) => ({
  id, catalogEntryId: null, localFirstName: '', localSurname: name, firstName: '', surname: '', rating: 1000, rank: '',
  rankShift: 0, grade: '', country: '', club: '', nationality: '', team: '', coach: '', egdPin: 0, startScore: 0,
  preliminary: false, firstEligibleRound: 1, notPlayingRounds: [], comment: '', displayName: name,
});

function renderEditor(pairing: Pairing) {
  const tournament = {
    id: 't1', name: 'T', system: 'swiss', legacyKind: null, numberOfRounds: 5, currentRound: 1, allowDraws: false,
    participants: [player(1, 'Alpha'), player(2, 'Bravo')],
    rounds: [{ number: 1, pairings: [pairing] }],
    stages: [{ startRound: 1, system: 'swiss', frozenRosterIds: null, randomStartingNumbers: false }],
  } as unknown as Tournament;
  const view = { tournament, imported: false, gameCounts: {}, systemLocked: true, defaultJoinRound: 1 } as TournamentView;
  const run = vi.fn(async (action: () => Promise<TournamentView>) => action());
  const deletePairing = vi.spyOn(api, 'deletePairing').mockResolvedValue(view);
  const utils = render(
    <I18nProvider>
      <ToastProvider>
        <ConfirmProvider>
          <TournamentContext.Provider value={{ view, tournament, standings: null, run }}>
            <PairingEditor pairing={pairing} roundNumber={1} onClose={() => {}} />
          </TournamentContext.Provider>
        </ConfirmProvider>
      </ToastProvider>
    </I18nProvider>,
  );
  return { ...utils, deletePairing };
}

const base: Pairing = {
  id: 7, blackId: 1, whiteId: null, isBye: true, boardNumber: 1, boardNumberFixed: false, handicap: 0, additionalKomi: 0,
  allowDraw: false, result: 'unset', legacyResultToken: null, resultByReferee: false, comment: '', protected: false,
};

async function deleteWithConfirmation(utils: ReturnType<typeof renderEditor>, expectedText: string) {
  fireEvent.click(utils.getByRole('button', { name: 'Delete pairing' }));
  const dialog = await waitFor(() => {
    const open = [...utils.baseElement.querySelectorAll('dialog[open]')].find((d) => d.textContent?.includes('Delete pairing?'));
    if (!open) throw new Error('confirmation not shown');
    return open as HTMLElement;
  });
  expect(dialog.textContent).toContain(expectedText);
  fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
}

describe('pairing editor deletion', () => {
  it('sends the confirmation flag when deleting a credited bye after the referee confirms', async () => {
    const utils = renderEditor(base);
    await deleteWithConfirmation(utils, 'already been credited as one win and one point');
    await waitFor(() => expect(utils.deletePairing).toHaveBeenCalledWith('t1', 7, true));
  });

  it('does not send the flag for an unplayed game', async () => {
    const utils = renderEditor({ ...base, isBye: false, whiteId: 2 });
    await deleteWithConfirmation(utils, 'removed from this round');
    await waitFor(() => expect(utils.deletePairing).toHaveBeenCalledWith('t1', 7, false));
  });
});

describe('pairing editor result list (spec 0003 §7)', () => {
  const game: Pairing = { ...base, isBye: false, whiteId: 2 };
  const resultSelect = (u: ReturnType<typeof renderEditor>) => u.getByLabelText('Result') as HTMLSelectElement;
  const codes = (u: ReturnType<typeof renderEditor>) => [...resultSelect(u).options].map((o) => o.textContent!.split(' ')[0]);

  it('puts both colour-labelled players and the result first, other settings after them', () => {
    const u = renderEditor(game);
    const form = u.baseElement.querySelector('dialog form')!;
    const primary = form.firstElementChild as HTMLElement;
    expect(primary.getAttribute('aria-label')).toBe('Players and result');
    expect(within(primary).getByLabelText(/Black/)).toBeTruthy();
    expect(within(primary).getByLabelText(/White/)).toBeTruthy();
    expect(within(primary).getByLabelText('Result')).toBeTruthy();
    expect(within(primary).queryByLabelText(/^Board/)).toBeNull();
    expect(u.getByLabelText(/^Board/)).toBeTruthy(); // secondary settings stay operable
  });

  it('offers the legacy codes in order, with 1-1, and ½-½ only when draws are allowed', () => {
    const u = renderEditor(game);
    expect(codes(u)).toEqual(['?-?', '1-0', '0-1', '1-1', '0-0']);
    cleanup();
    const withDraws = renderEditor({ ...game, allowDraw: true });
    expect(codes(withDraws)).toEqual(['?-?', '1-0', '0-1', '1-1', '0-0', '½-½']);
  });

  it('keeps a recorded draw selectable after draws were disallowed, and keeps 1-1 on a colour swap', () => {
    const draw = renderEditor({ ...game, result: 'draw' });
    expect(codes(draw)).toContain('½-½');
    expect(resultSelect(draw).value).toBe('draw');
    cleanup();
    const both = renderEditor({ ...game, result: 'bothWin' });
    fireEvent.click(both.getByRole('button', { name: 'Swap colors' }));
    expect(resultSelect(both).value).toBe('bothWin');
  });

  it('sends bothWin when 1-1 is chosen', async () => {
    const u = renderEditor(game);
    const update = vi.spyOn(api, 'updatePairing').mockResolvedValue({} as TournamentView);
    fireEvent.change(resultSelect(u), { target: { value: 'bothWin' } });
    fireEvent.click(u.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update.mock.calls[0][2]).toMatchObject({ result: 'bothWin' });
  });
});
