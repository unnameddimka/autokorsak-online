import { ApiError } from './api/client';
import type { GameResult } from './api/types';
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
