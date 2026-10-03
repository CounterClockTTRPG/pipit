#!/usr/bin/env python3
"""Lists the interface texts that need translating.

    tools/extract-strings.py            # print them, one per line
    tools/extract-strings.py --update   # add missing ones to every data/i18n/*.json (value "")

Texts are the string literals passed to Ui.T / Ui.Label / Ui.Text / Ui.Button, items of
Ui.Options(new[] { ... }), screen titles, and the Core texts the app shows (achievements,
XP reasons, timing names, recorder sizes, fingering systems). English is the key; the test
LocalisationTests fails when one has no Dutch translation.
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# A text may be several literals joined with +, which the compiler turns into one string.
CALL = re.compile(r'Ui\.(?:T|Label|Text|Button)\(\s*((?:"(?:[^"\\]|\\.)*"\s*\+\s*)*"(?:[^"\\]|\\.)*")')
OPTIONS = re.compile(r'Ui\.Options\(\s*new\[\]\s*\{([^}]*)\}')
TITLE = re.compile(r'override string Title => "((?:[^"\\]|\\.)*)"')
TILE = re.compile(r'Tile\(\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"')
TABLE = re.compile(r'\[\d+\]\s*=\s*"((?:[^"\\]|\\.)*)"')
# Calibration headings and similar helpers, texts chosen by a condition inside Ui.T(...),
# and (name, values) tables such as the trainer's note ranges.
HELPER = re.compile(r'\bSet\(\s*((?:"(?:[^"\\]|\\.)*"\s*,?\s*)+)')
CONDITIONAL = re.compile(r'Ui\.(?:T|Button|Label|Text)\(([^;\n]*\?[^;\n]*:[^;\n]*)\)')
TUPLE = re.compile(r'\(\s*"((?:[^"\\]|\\.)*)",\s*new\[\]')
PAGE = re.compile(r'new\(PipMood\.\w+,\s*"((?:[^"\\]|\\.)*)"')
LITERAL = re.compile(r'"((?:[^"\\]|\\.)*)"')

def unescape(s):
    return s.encode().decode('unicode_escape').encode('latin-1').decode('utf-8')

def texts():
    found = set()
    for folder, _, files in os.walk(os.path.join(ROOT, 'src', 'App')):
        for f in files:
            if not f.endswith('.cs'):
                continue
            src = open(os.path.join(folder, f), encoding='utf-8').read()
            for m in CALL.finditer(src):
                found.add(''.join(unescape(x) for x in LITERAL.findall(m.group(1))))
            for m in OPTIONS.finditer(src):
                found.update(unescape(x) for x in LITERAL.findall(m.group(1)))
            for m in TITLE.finditer(src):
                found.add(unescape(m.group(1)))
            for m in TILE.finditer(src):
                found.update((unescape(m.group(1)), unescape(m.group(2))))
            for rx in (TABLE, PAGE, TUPLE):
                found.update(unescape(m.group(1)) for m in rx.finditer(src))
            for rx in (HELPER, CONDITIONAL):
                for m in rx.finditer(src):
                    found.update(unescape(x) for x in LITERAL.findall(m.group(1)))
    core = open(os.path.join(ROOT, 'src', 'Core', 'Game', 'Achievements.cs'), encoding='utf-8').read()
    for m in re.finditer(r'new Achievement\("[^"]+", "((?:[^"\\]|\\.)*)", "((?:[^"\\]|\\.)*)"', core):
        found.update((unescape(m.group(1)), unescape(m.group(2))))
    progress = open(os.path.join(ROOT, 'src', 'Core', 'Game', 'Progress.cs'), encoding='utf-8').read()
    for m in re.finditer(r'new(?: XpLine)?\("((?:[^"\\]|\\.)*)", ', progress):
        found.add(unescape(m.group(1)))
    sessions = open(os.path.join(ROOT, 'src', 'Core', 'Recording', 'GuidedSession.cs'), encoding='utf-8').read()
    for m in re.finditer(r'new GuidedSession\("[^"]+", "((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"', sessions):
        found.update((unescape(m.group(1)), unescape(m.group(2))))
    for m in re.finditer(r'LongTones\("[^"]+", "((?:[^"\\]|\\.)*)"', sessions):
        found.add(unescape(m.group(1)))
    found.update(['Entry {0}', 'Voice {0}', 'Melody', 'Melody (chorus)', 'Left hand', 'Upper strings', 'Lower strings', 'Bass', 'Lower voice',
                  'Traditional ({0})', 'France', 'England', 'Germany', 'United States', 'Anonymous', '14th century',
                  ', diminutions by {0}', '; words by {0}', 'Prélude (cello part, two octaves up)',
                  'Relaxed', 'Normal', 'Strict', 'Pipit', 'Robin', 'Blue tit', 'Goldfinch', 'Bluebird', 'Greenfinch', 'Play this note and hold it',
                  'Perfect', 'Great', 'Good', 'Early', 'Late', 'Soprano', 'Alto', 'Tenor', 'Baroque', 'German',
                  'First practice today', 'Day {0} in a row', '{0} star', '{0} stars', 'Achievement: {0}'])
    # Skip fragments of interpolated strings ("{entry.Score}"); keep format placeholders ("{0}").
    return sorted(t for t in found if any(c.isalpha() for c in t) and not t.startswith('res://') and not re.search(r'\{(?!\d)', t) and not re.search(r'\b[a-z]\w*\.[A-Z]\w*', t))

if __name__ == '__main__':
    items = texts()
    if '--update' in sys.argv:
        folder = os.path.join(ROOT, 'data', 'i18n')
        for name in sorted(os.listdir(folder)):
            if not name.endswith('.json'):
                continue
            path = os.path.join(folder, name)
            table = json.load(open(path, encoding='utf-8'))
            for t in items:
                table.setdefault(t, '')
            json.dump(dict(sorted(table.items())), open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
            print(f'{name}: {len(items)} texts, {sum(1 for t in items if not table.get(t))} untranslated')
    else:
        print('\n'.join(items))
