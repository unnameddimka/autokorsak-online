import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { en, type MessageKey } from './en';
import { uk } from './uk';

/** The only supported interface languages. Adding Russian is explicitly prohibited (spec 0000 §2.1). */
export const LOCALES = ['uk', 'en'] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = 'uk';

export const dictionaries: Record<Locale, Record<MessageKey, string>> = { uk, en };

const STORAGE_KEY = 'autokorsak.locale';

function readStoredLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return (LOCALES as readonly string[]).includes(stored ?? '') ? (stored as Locale) : DEFAULT_LOCALE;
  } catch {
    return DEFAULT_LOCALE;
  }
}

export type Params = Record<string, unknown>;

export function format(template: string, params?: Params): string {
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name] ?? '') : match));
}

/** Looks up a message; missing keys fall back to English (never another language), then to the key itself. */
export function translate(locale: Locale, key: string, params?: Params): string {
  const dict = dictionaries[locale] as Record<string, string>;
  const template = dict[key] ?? (en as Record<string, string>)[key];
  return template === undefined ? key : format(template, params);
}

export function hasKey(key: string): key is MessageKey {
  return key in en;
}

interface I18n {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (key: MessageKey, params?: Params) => string;
  /** For keys built at runtime (error codes, criteria): falls back to `fallback` when unknown. */
  tk: (key: string, fallback: MessageKey, params?: Params) => string;
  formatDateTime: (iso: string) => string;
  formatNumber: (value: number) => string;
}

const I18nContext = createContext<I18n | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(readStoredLocale);

  useEffect(() => {
    document.documentElement.lang = locale === 'uk' ? 'uk' : 'en';
  }, [locale]);

  const setLocale = useCallback((next: Locale) => {
    setLocaleState(next);
    try { localStorage.setItem(STORAGE_KEY, next); } catch { /* preference only */ }
  }, []);

  const value = useMemo<I18n>(() => {
    const intlLocale = locale === 'uk' ? 'uk-UA' : 'en-GB';
    const dateFormat = new Intl.DateTimeFormat(intlLocale, { dateStyle: 'medium', timeStyle: 'short' });
    const numberFormat = new Intl.NumberFormat(intlLocale, { maximumFractionDigits: 2 });
    return {
      locale,
      setLocale,
      t: (key, params) => translate(locale, key, params),
      tk: (key, fallback, params) => translate(locale, hasKey(key) ? key : fallback, { key, ...params }),
      formatDateTime: (iso) => dateFormat.format(new Date(iso)),
      formatNumber: (value) => numberFormat.format(value),
    };
  }, [locale, setLocale]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n must be used inside I18nProvider');
  return ctx;
}
