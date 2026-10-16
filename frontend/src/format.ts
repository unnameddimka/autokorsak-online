import { ApiError } from './api/client';
import type { GameResult, Pairing, PairingStage, Participant, Tournament, TournamentSystem } from './api/types';
import type { MessageKey } from './i18n/en';

type T = (key: MessageKey, params?: Record<string, unknown>) => string;
type TK = (key: string, fallback: MessageKey, params?: Record<string, unknown>) => string;

const ROMAN: [number, string][] = [[50, 'L'], [40, 'XL'], [10, 'X'], [9, 'IX'], [5, 'V'], [4, 'IV'], [1, 'I']];

export function roman(n: number): string {
  let out = '';
  for (const [value, symbol] of ROMAN) while (n >= value) { out += symbol; n -= value; }
  return out;
}

export const RANK_OPTIONS: string[] = [
  ...Array.from({ length: 30 }, (_, i) => `${30 - i}k`),
  ...Array.from({ length: 9 }, (_, i) => `${i + 1}d`),
  ...Array.from({ length: 9 }, (_, i) => `${i + 1}p`),
];

/** Legacy rank notation ("9k", "2d", "1p") rendered in the interface language. */
export function formatRank(rank: string, t: T): string {
  const m = /^(\d+)([kdp])$/.exec(rank);
  if (!m) return rank;
  const key: MessageKey = m[2] === 'k' ? 'rank.kyu' : m[2] === 'd' ? 'rank.dan' : 'rank.pro';
  return t(key, { n: m[1] });
}

export function resultLabel(result: GameResult, t: T, legacyToken?: string | null): string {
  switch (result) {
    case 'blackWins': return '1-0';
    case 'whiteWins': return '0-1';
    case 'bothLose': return '0-0';
    case 'draw': return '½-½';
    case 'unknown': return legacyToken ?? '?';
    default: return t('result.unsetShort');
  }
}

/** Localized message for any error thrown by the API client. */
export function errorMessage(error: unknown, tk: TK): string {
  if (error instanceof ApiError) {
    const args = { ...error.args };
    if (typeof args.field === 'string') args.field = tk(`field.${args.field}`, 'field.unknown');
    return tk(`error.${error.code}`, 'error.unknown', args);
  }
  return tk('error.unknown', 'error.unknown', { key: String(error) });
}

/** The stage covering a round (display only; the server resolves stages for every rule). */
export function stageFor(t: Tournament, round: number): PairingStage {
  const stages = t.stages.length > 0 ? t.stages : [{ startRound: 1, system: t.system, frozenRosterIds: null, randomStartingNumbers: false }];
  return [...stages].reverse().find((s) => s.startRound <= round) ?? stages[0];
}

/** Mirrors the server rule: a recorded result, or a bye credited to a real player (one win and one point). */
export function hasAwardedOutcome(p: Pick<Pairing, 'result' | 'isBye' | 'blackId'>): boolean {
  return p.result !== 'unset' || (p.isBye && p.blackId !== null);
}

export type RoundParticipation = 'notJoined' | 'notPlaying' | 'plays';

/** How a participant stands in a round, for display: before the joining round they have not joined yet. */
export function roundParticipation(p: Pick<Participant, 'firstEligibleRound' | 'notPlayingRounds'>, round: number): RoundParticipation {
  if (round < p.firstEligibleRound) return 'notJoined';
  return p.notPlayingRounds.includes(round) ? 'notPlaying' : 'plays';
}

/** Rounds in which a frozen round-robin member's availability is fixed (the server refuses changes there). */
export function isLockedRosterRound(t: Tournament, participantId: number, round: number): boolean {
  return stageFor(t, round).frozenRosterIds?.includes(participantId) === true;
}

export function stageEnd(t: Tournament, stage: PairingStage): number {
  const next = t.stages.find((s) => s.startRound > stage.startRound);
  return next ? next.startRound - 1 : t.numberOfRounds;
}

export function systemLabel(system: TournamentSystem | null, tk: TK, legacyKind: number | null): string {
  return system ? tk(`system.${system}`, 'field.unknown') : tk('system.legacyUnverified', 'field.unknown', { code: legacyKind ?? '?' });
}
