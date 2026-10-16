import { describe, expect, it } from 'vitest';
import type { Tournament } from './api/types';
import { isLockedRosterRound, roundParticipation } from './format';

describe('round participation markers', () => {
  it('shows rounds before the joining round as not joined, never as available', () => {
    const late = { firstEligibleRound: 6, notPlayingRounds: [7] };
    expect([1, 2, 3, 4, 5].map((r) => roundParticipation(late, r))).toEqual(Array(5).fill('notJoined'));
    expect(roundParticipation(late, 6)).toBe('plays');
    expect(roundParticipation(late, 7)).toBe('notPlaying');
  });

  it('locks availability only in rounds of a frozen round robin the player belongs to', () => {
    const t = {
      numberOfRounds: 7,
      system: 'swiss',
      stages: [
        { startRound: 1, system: 'roundRobin', frozenRosterIds: [1, 2, 3, 4], randomStartingNumbers: false },
        { startRound: 4, system: 'swiss', frozenRosterIds: null, randomStartingNumbers: false },
      ],
    } as unknown as Tournament;
    expect(isLockedRosterRound(t, 2, 3)).toBe(true);
    expect(isLockedRosterRound(t, 2, 4)).toBe(false);
    expect(isLockedRosterRound(t, 9, 2)).toBe(false);
  });
});
