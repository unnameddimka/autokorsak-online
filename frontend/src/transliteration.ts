/**
 * Ukrainian → Latin transliteration by the official table of the Cabinet of Ministers of Ukraine, Resolution No. 55
 * of 27 January 2010 (https://zakon.rada.gov.ua/laws/show/55-2010-%D0%BF), as required by spec 0003 §4.
 *
 * This file holds transliteration data, not interface text: the Cyrillic letters below are the source alphabet of the
 * official table (the localization test allows them here only).
 *
 * Rules applied:
 * - Є, Ї, Й, Ю, Я use their word-initial form (Ye, Yi, Y, Yu, Ya) at the start of a word and ie, i, i, iu, ia elsewhere.
 * - The sequence "зг" is written "zgh" (Згорани → Zghorany), to keep it apart from "ж" (zh).
 * - The soft sign and the apostrophe are not rendered.
 * - A word starts after any character that is not a letter or an apostrophe (space, hyphen, dot, …), so both parts of
 *   a double name are word-initial.
 * - Case follows the source: a capital letter gives a capitalized group (Щ → Shch); a fully capitalized word of two or
 *   more letters stays fully capitalized (ЩУР → SHCHUR).
 * - Characters outside the Ukrainian alphabet (e.g. Russian ы, э, ё, ъ) are kept unchanged and reported, never guessed.
 */

const TABLE: Record<string, string> = {
  а: 'a', б: 'b', в: 'v', г: 'h', ґ: 'g', д: 'd', е: 'e', є: 'ie', ж: 'zh', з: 'z', и: 'y', і: 'i', ї: 'i', й: 'i',
  к: 'k', л: 'l', м: 'm', н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'kh', ц: 'ts', ч: 'ch',
  ш: 'sh', щ: 'shch', ь: '', ю: 'iu', я: 'ia',
};

const INITIAL: Record<string, string> = { є: 'ye', ї: 'yi', й: 'y', ю: 'yu', я: 'ya' };

/** Apostrophe variants found in Ukrainian names: ASCII, right single quotation mark, modifier letter, backtick. */
const APOSTROPHES = new Set(["'", '’', 'ʼ', '`']);

const isLetter = (c: string) => /\p{L}/u.test(c);
const isCyrillic = (c: string) => /\p{Script=Cyrillic}/u.test(c);

export interface TransliterationResult {
  text: string;
  /** Distinct source characters that have no entry in the official table and were kept as they are. */
  unsupported: string[];
}

export function transliterate(source: string): TransliterationResult {
  const unsupported = new Set<string>();
  let out = '';
  // Words are processed one at a time so that capitalization can follow the whole word.
  const words = source.match(/[\p{L}'’ʼ`]+|[^\p{L}'’ʼ`]+/gu) ?? [];
  for (const word of words) {
    if (!isLetter(word[0]) && !APOSTROPHES.has(word[0])) {
      out += word; // separators (spaces, hyphens, digits, punctuation) are kept
      continue;
    }
    const letters = [...word].filter(isLetter);
    const allCaps = letters.length > 1 && letters.every((c) => c === c.toUpperCase() && c !== c.toLowerCase());
    let initial = true;
    let previous = '';
    for (const char of word) {
      if (APOSTROPHES.has(char)) continue; // not rendered; the next letter is not word-initial
      const lower = char.toLowerCase();
      let latin: string;
      if (lower === 'г' && previous === 'з') latin = 'gh';
      else if (initial && INITIAL[lower] !== undefined) latin = INITIAL[lower];
      else if (TABLE[lower] !== undefined) latin = TABLE[lower];
      else {
        if (isCyrillic(char)) unsupported.add(char);
        latin = char; // Latin letters and anything unsupported are kept unchanged
        out += latin;
        initial = false;
        previous = lower;
        continue;
      }
      const upper = char !== lower;
      out += allCaps ? latin.toUpperCase() : upper ? latin.charAt(0).toUpperCase() + latin.slice(1) : latin;
      initial = false;
      previous = lower;
    }
  }
  return { text: out, unsupported: [...unsupported] };
}
