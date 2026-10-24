import { describe, expect, it } from 'vitest';
import { transliterate } from './transliteration';

const tr = (s: string) => transliterate(s).text;

describe('official Ukrainian transliteration (KMU Resolution 55, 2010)', () => {
  it('matches the spec 0003 examples', () => {
    expect(tr('Олексій')).toBe('Oleksii');
    expect(tr('Стеценко')).toBe('Stetsenko');
    expect(tr('Євген')).toBe('Yevhen');
    expect(tr('Юлія')).toBe('Yuliia');
    expect(tr('Київ')).toBe('Kyiv');
    expect(tr('Згурський')).toBe('Zghurskyi');
  });

  it('matches the examples of the official table', () => {
    const table: [string, string][] = [
      ['Алушта', 'Alushta'], ['Борщагівка', 'Borshchahivka'], ['Вінниця', 'Vinnytsia'], ['Гадяч', 'Hadiach'],
      ['Ґорґани', 'Gorgany'], ['Донецьк', 'Donetsk'], ['Рівне', 'Rivne'], ['Єнакієве', 'Yenakiieve'],
      ['Наддніпрянське', 'Naddniprianske'], ['Житомир', 'Zhytomyr'], ['Закарпаття', 'Zakarpattia'], ['Медвин', 'Medvyn'],
      ['Іршава', 'Irshava'], ['Їжакевич', 'Yizhakevych'], ['Кадиївка', 'Kadyivka'], ['Йосипівка', 'Yosypivka'],
      ['Стрий', 'Stryi'], ['Лебедин', 'Lebedyn'], ['Миколаїв', 'Mykolaiv'], ['Ніжин', 'Nizhyn'], ['Одеса', 'Odesa'],
      ['Полтава', 'Poltava'], ['Ромни', 'Romny'], ['Суми', 'Sumy'], ['Тетерів', 'Teteriv'], ['Ужгород', 'Uzhhorod'],
      ['Фастів', 'Fastiv'], ['Харків', 'Kharkiv'], ['Біла Церква', 'Bila Tserkva'], ['Чернівці', 'Chernivtsi'],
      ['Шостка', 'Shostka'], ['Гоща', 'Hoshcha'], ['Юрій', 'Yurii'], ['Крюківка', 'Kriukivka'], ['Яготин', 'Yahotyn'],
      ['Ічня', 'Ichnia'], ['Згорани', 'Zghorany'], ['Розгон', 'Rozghon'],
    ];
    for (const [uk, en] of table) expect(tr(uk), uk).toBe(en);
  });

  it('drops the soft sign and apostrophes and treats the next letter as non-initial', () => {
    expect(tr('Мар\'яна')).toBe('Mariana');
    expect(tr('Мар’яна')).toBe('Mariana');
    expect(tr('Марʼяна')).toBe('Mariana');
    expect(tr('Ольга')).toBe('Olha');
  });

  it('keeps hyphens and spaces, and both parts of a double name are word-initial', () => {
    expect(tr('Яковенко-Юрченко')).toBe('Yakovenko-Yurchenko');
    expect(tr('Анна-Марія')).toBe('Anna-Mariia');
    expect(tr('Кобзар Гліб')).toBe('Kobzar Hlib'); // legacy full name in one field: transliterated as entered
  });

  it('follows the capitalization of the source', () => {
    expect(tr('ЩУР')).toBe('SHCHUR');
    expect(tr('Щур')).toBe('Shchur');
    expect(tr('ЗГУРСЬКИЙ')).toBe('ZGHURSKYI');
    expect(tr('Ю')).toBe('Yu');
    expect(tr('де')).toBe('de');
  });

  it('keeps unsupported characters unchanged and reports them', () => {
    const result = transliterate('Группа Ы2');
    expect(result.text).toBe('Hruppa Ы2'); // Ukrainian г → h; the Russian-only letter is kept
    expect(result.unsupported).toEqual(['Ы']);
    expect(transliterate('Smith').text).toBe('Smith');
    expect(transliterate('Smith').unsupported).toEqual([]);
    expect(transliterate('').text).toBe('');
  });
});
