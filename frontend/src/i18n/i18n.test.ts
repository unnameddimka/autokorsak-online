import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { en } from './en';
import { uk } from './uk';
import { LOCALES, DEFAULT_LOCALE, translate } from './index';

const placeholders = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
const srcDir = join(__dirname, '..');

function filesUnder(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}

describe('localization', () => {
  it('offers exactly Ukrainian and English, Ukrainian by default', () => {
    expect([...LOCALES]).toEqual(['uk', 'en']);
    expect(DEFAULT_LOCALE).toBe('uk');
  });

  it('has identical key sets in both languages', () => {
    expect(Object.keys(uk).sort()).toEqual(Object.keys(en).sort());
  });

  it('has no empty translations', () => {
    for (const [key, value] of [...Object.entries(en), ...Object.entries(uk)]) {
      expect(value.trim(), key).not.toBe('');
    }
  });

  it('uses the same placeholders in both languages', () => {
    for (const key of Object.keys(en) as (keyof typeof en)[]) {
      expect(placeholders(uk[key]), key).toEqual(placeholders(en[key]));
    }
  });

  it('ships no Russian locale or resource file', () => {
    const i18nFiles = readdirSync(__dirname).map((f) => f.toLowerCase());
    expect(i18nFiles.some((f) => /^ru([-_.]|$)/.test(f))).toBe(false);
    expect((LOCALES as readonly string[]).some((l) => l.toLowerCase().startsWith('ru'))).toBe(false);
  });

  it('Ukrainian strings contain no letters that exist only in Russian', () => {
    for (const [key, value] of Object.entries(uk)) {
      expect(/[ыэъёЫЭЪЁ]/.test(value), `${key}: ${value}`).toBe(false);
    }
  });

  it('components contain no hard-coded Cyrillic interface text', () => {
    const offenders = filesUnder(srcDir)
      .filter((f) => /\.tsx?$/.test(f) && !f.includes(`${join('i18n', '')}`))
      .filter((f) => /[Ѐ-ӿ]/.test(readFileSync(f, 'utf8')));
    expect(offenders).toEqual([]);
  });

  it('falls back to English, never another language, for unknown locales', () => {
    expect(translate('en', 'nav.standings')).toBe('Standings');
    expect(translate('uk', 'nav.standings')).toBe('Турнірна таблиця');
    expect(translate('uk', 'no.such.key')).toBe('no.such.key');
  });
});
