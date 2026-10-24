// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import type { Participant, Standings, Tournament, TournamentView } from '../api/types';
import { I18nProvider } from '../i18n';
import { ConfirmProvider, ToastProvider } from '../components/common';
import { TournamentContext } from './TournamentLayout';
import { PlayersPage } from './PlayersPage';
import { StandingsPage } from './StandingsPage';
import { PairingsPage } from './PairingsPage';

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) { this.removeAttribute('open'); };
});
beforeEach(() => localStorage.setItem('autokorsak.locale', 'en'));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const player = (id: number, name: string, rating: number): Participant => ({
  id, catalogEntryId: null, localFirstName: '', localSurname: name, firstName: '', surname: '', rating, rank: '',
  rankShift: 0, grade: '', country: '', club: '', nationality: '', team: '', coach: '', egdPin: 0, startScore: 0,
  preliminary: false, firstEligibleRound: 1, startingNumber: null, notPlayingRounds: [], comment: '', displayName: name,
});

function setup(page: ReactNode, system: 'roundRobin' | 'swiss' = 'roundRobin') {
  const tournament = {
    id: 't1', name: 'T', system, legacyKind: null, numberOfRounds: 3, currentRound: 2, includeCurrentRound: true,
    allowDraws: false, updatedAt: '2026-10-24T00:00:00Z',
    participants: [player(1, 'Alpha', 0), player(2, 'Bravo', 1500)],
    rounds: [],
    stages: [{ startRound: 1, system, frozenRosterIds: null, randomStartingNumbers: false }],
    layout: { columns: ['place', 'name', 'rating', 'rounds', 'criteria'].map((key) => ({ key, visible: true })), showColors: false, hideEmptyColumns: true },
  } as unknown as Tournament;
  const standings: Standings = {
    displayedRounds: [1, 2], countedRounds: [1, 2], criteria: [{ key: 'wins', calculated: true }], notices: [],
    rows: [1, 2].map((id, i) => ({
      position: i + 1, place: i + 1, placeTo: i + 1, participantId: id, values: { wins: 0 },
      rounds: [1, 2].map((round) => ({ round, kind: 'none', opponentId: null, opponentPosition: null, outcome: null, color: null, byReferee: false })),
    })),
  };
  const view = {
    tournament, imported: false, gameCounts: {}, systemLocked: false, defaultJoinRound: 1,
    roundRobin: system === 'roundRobin'
      ? { startRound: 1, endRound: 3, frozen: false, randomStartingNumbers: false, numbers: { 1: null, 2: null }, eligible: [1, 2], lockedBy: [] }
      : null,
  } as unknown as TournamentView;
  const run = vi.fn(async (action: () => Promise<TournamentView>) => action());
  return render(
    <I18nProvider><ToastProvider><ConfirmProvider>
      <TournamentContext.Provider value={{ view, tournament, standings, run }}>{page}</TournamentContext.Provider>
    </ConfirmProvider></ToastProvider></I18nProvider>,
  );
}

const ratingCell = (table: HTMLElement, name: string) => {
  const headers = [...table.querySelectorAll('thead th')].map((th) => th.textContent);
  const row = within(table).getByText(name).closest('tr')!;
  return row.querySelectorAll('td')[headers.indexOf('Rating')].textContent;
};

describe('rating 0 is displayed as 0 (spec 0003 §5.2)', () => {
  it('on the Players page', () => {
    const u = setup(<PlayersPage />);
    const table = u.container.querySelector('table') as HTMLElement;
    expect(ratingCell(table, 'Alpha')).toBe('0');
    expect(ratingCell(table, 'Bravo')).toBe('1500');
  });

  it('in the standings', () => {
    const u = setup(<StandingsPage />);
    const table = u.container.querySelector('table') as HTMLElement;
    expect(ratingCell(table, 'Alpha')).toBe('0');
  });
});

describe('players list in a pure round robin (spec 0003 §2.3)', () => {
  it('has no joining-round or availability columns', () => {
    const headers = () => [...document.querySelectorAll('thead th')].map((th) => th.textContent);
    setup(<PlayersPage />);
    expect(headers()).not.toContain('From round');
    expect(headers()).not.toContain('Plays in rounds');
    expect(headers()).toContain('Registration');
    cleanup();
    setup(<PlayersPage />, 'swiss');
    expect(headers()).toContain('From round');
    expect(headers()).toContain('Plays in rounds');
  });
});

describe('current-round control placement (spec 0003 §3)', () => {
  it('is on the Standings page', () => {
    const u = setup(<StandingsPage />);
    const nav = u.getByRole('group', { name: 'Round navigation' });
    expect((within(nav).getByRole('combobox') as HTMLSelectElement).value).toBe('2');
  });

  it('is not on the Pairings or Players pages; Pairings names the round in its heading', () => {
    const pairings = setup(<PairingsPage />);
    expect(pairings.queryByRole('group', { name: 'Round navigation' })).toBeNull();
    expect(pairings.getByRole('heading', { name: 'Pairings — round II' })).toBeTruthy();
    cleanup();
    const players = setup(<PlayersPage />);
    expect(players.queryByRole('group', { name: 'Round navigation' })).toBeNull();
  });
});
