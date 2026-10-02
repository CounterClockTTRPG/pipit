#!/usr/bin/env python3
"""Creates repertoire songs from multi-part MIDI sources (see SONGS below).

    tools/import-songs.py

Playable parts and accompaniment are converted with midi2pipit.py from the same source and cut
from the same bar (leading bars where no playable part plays are dropped), the key is chosen by
fewest printed accidentals, and short final bars are padded with rests. Existing about/tip texts
are kept when a song is regenerated.
"""
import json, os, re, subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONV = os.path.join(ROOT, 'tools', 'midi2pipit.py')
M = os.path.join(ROOT, 'tools', 'sources', 'mutopia')
MAJOR = {0: 'C', 1: 'G', 2: 'D', 3: 'A', -1: 'F', -2: 'Bb', -3: 'Eb'}
SHARPS, FLATS = 'FCGDAEB', 'BEADGCF'
DUR = {'w': 4, 'h': 2, 'q': 1, 'e': 0.5, 's': 0.25}
REST = {6: 'R/w.', 4: 'R/w', 3: 'R/h.', 2: 'R/h', 1.5: 'R/q.', 1: 'R/q', 0.75: 'R/e.', 0.5: 'R/e', 0.25: 'R/s'}

HANDEL = 'Mutopia Project edition after Chrysander (Händel-Gesellschaft, 1879), CC BY-SA 3.0; converted with tools/import-songs.py'
TELEMANN = 'Mutopia Project edition after Der getreue Music-Meister (1728), Public Domain; converted with tools/import-songs.py'
HANDEL_ABOUT = {
    'en': "Handel wrote a handful of sonatas specifically for the alto recorder (\"flauto\") with basso continuo, in London in the 1720s. They were printed around 1730 as part of his Opus 1, and recorder players have loved them ever since.",
    'nl': "Händel schreef in de jaren 1720 in Londen een handvol sonates speciaal voor de altblokfluit (\"flauto\") met basso continuo. Ze werden rond 1730 gedrukt als deel van zijn Opus 1, en blokfluitisten zijn er sindsdien dol op.",
}
TELEMANN_ABOUT = {
    'en': "Telemann published \"Der getreue Music-Meister\" in Hamburg in 1728-29, a music magazine that came out every two weeks with new pieces for amateurs. This duet for two recorders appeared in it: both parts are equally interesting, so you can play either while the band plays the other.",
    'nl': "Telemann gaf in 1728-29 in Hamburg \"Der getreue Music-Meister\" uit, een muziektijdschrift dat om de twee weken verscheen met nieuwe stukken voor liefhebbers. Dit duet voor twee blokfluiten stond erin: beide partijen zijn even interessant, dus je kan er een spelen terwijl de band de andere speelt.",
}
PLAY_EITHER = {'en': "Pick a part: the band plays the other recorder.", 'nl': "Kies een partij: de band speelt de andere blokfluit."}
HANDEL_TIP = {'en': "Written for alto recorder; on a soprano Pipit moves it to fit. The bass line is the harpsichord's: let it carry you.",
              'nl': "Geschreven voor altblokfluit; op een sopraan verschuift Pipit het zodat het past. De baslijn is van het klavecimbel: laat je erdoor dragen."}

def handel(hwv, key, key_en, key_nl, movements):
    out = []
    for i, (mid, roman, name, time, tempo, *pickup) in enumerate(movements):
        out.append(dict(id=f'handel-hwv{hwv}-{i + 1}', midi=f'{M}/HWV{hwv}-Sonata-mids/{mid}', time=time, tempo=tempo, key=key,
            pickup=(pickup or [None])[0],
            title={'en': f'Sonata in {key_en}, HWV {hwv}: {roman}. {name}', 'nl': f'Sonate in {key_nl}, HWV {hwv}: {roman}. {name}'},
            composer='George Frideric Handel', source=HANDEL, about=HANDEL_ABOUT, tip=HANDEL_TIP,
            parts=[(1, 'Recorder', 'alto')], accompaniment=[(2, 'Basso continuo', 'harpsichord', 'bottom')]))
    return out

SONGS = [
    dict(id='brahms-lullaby', midi=f'{M}/LullabyBrahms-C-LullabyBrahms-C.mid', time='3/4', tempo=88, pickup='q',
         title={'en': "Brahms' Lullaby", 'nl': 'Wiegelied van Brahms', 'fr': 'Berceuse de Brahms'}, composer='Johannes Brahms',
         source='Mutopia Project arrangement (Public Domain); converted with tools/import-songs.py',
         about={'en': "Brahms wrote this \"Wiegenlied\" (cradle song) in 1868 as a present for a friend, Bertha Faber, when her second son was born. It became one of the best-known lullabies in the world.",
                'nl': "Brahms schreef dit \"Wiegenlied\" in 1868 als cadeau voor een vriendin, Bertha Faber, toen haar tweede zoon geboren werd. Het werd een van de bekendste slaapliedjes ter wereld."},
         tip={'en': "Soft and calm, in three. Most of it uses the low notes: very gentle air.", 'nl': "Zacht en rustig, in drie. Het meeste gebruikt de lage noten: heel zachte lucht."},
         parts=[(1, 'Melody', 'soprano')], accompaniment=[(2, 'Piano', 'piano', 'top')]),
    dict(id='japart-tmeiskin', midi=f'{M}/27-tmeiskin-27-tmeiskin.mid', time='2/2', tempo=120,
         title={'en': 'Tmeiskin was jonck', 'nl': 'Tmeiskin was jonck'}, composer='Jean Japart',
         source='Mutopia Project edition after the facsimile of the Odhecaton (Venice, 1501/1504), Public Domain; converted with tools/import-songs.py',
         about={'en': "\"The girl was young\": a Flemish song, set for four voices by the Franco-Flemish composer Jean Japart. It was printed in Venice in the Odhecaton (1501), the first book of polyphonic music ever printed with movable type. Recorder consorts played music like this.",
                'nl': "\"Het meisje was jong\": een Vlaams lied, voor vier stemmen gezet door de Franco-Vlaamse componist Jean Japart. Het werd in Venetië gedrukt in het Odhecaton (1501), het allereerste boek met meerstemmige muziek gedrukt met losse letters. Blokfluitconsorten speelden zulke muziek."},
         tip={'en': "With the band on, you play the top voice of a Renaissance consort.", 'nl': "Met de band aan speel je de bovenstem van een renaissanceconsort."},
         parts=[(1, 'Superius', 'soprano')], accompaniment=[(2, 'Altus', 'recorder', 'top'), (3, 'Tenor', 'recorder', 'top'), (4, 'Bassus', 'bassoon', 'top')]),
] + handel(360, 'Gm', 'G minor', 'g klein', [
    ('Sonata.mid', 'I', 'Larghetto', '4/4', 60), ('Sonata-1.mid', 'II', 'Andante', '3/4', 76),
    ('Sonata-2.mid', 'III', 'Adagio', '3/2', 60), ('Sonata-3.mid', 'IV', 'Presto', '4/4', 100)]) + handel(365, 'C', 'C major', 'C groot', [
    ('Sonata.mid', 'I', 'Larghetto', '4/4', 60), ('Sonata-1.mid', 'II', 'Allegro', '3/8', 90), ('Sonata-2.mid', 'III', 'Larghetto', '3/4', 66),
    ('Sonata-3.mid', 'IV', 'Tempo di Gavotta', '2/2', 112, 'h'), ('Sonata-4.mid', 'V', 'Allegro', '3/8', 90)]) + handel(369, 'F', 'F major', 'F groot', [
    ('Sonata.mid', 'I', 'Larghetto', '3/4', 66), ('Sonata-1.mid', 'II', 'Allegro', '4/4', 100),
    ('Sonata-2.mid', 'III', 'Siciliana', '12/8', 72), ('Sonata-3.mid', 'IV', 'Allegro', '12/8', 116)]) + [
    dict(id=f'telemann-twv40-107-{i + 1}', midi=f'{M}/TWV40-Duett-Telemann-mids/{mid}', time=time, tempo=tempo, key='Bb',
         title={'en': f'Duet TWV 40:107: {roman}. {name}', 'nl': f'Duet TWV 40:107: {roman}. {name}'}, composer='Georg Philipp Telemann',
         source=TELEMANN, about=TELEMANN_ABOUT, tip=PLAY_EITHER,
         parts=[(1, 'Recorder 1', 'alto'), (2, 'Recorder 2', 'alto')], accompaniment=[],
         # The grace note (\grace a4) before bar 27 of the second part took its time from the B flat.
         fix=[(1, 25, 'G5/h A5/q Bb5/e. A5/s', 'G5/h A5/q Bb5/q')] if i == 3 else [])
    for i, (mid, roman, name, time, tempo) in enumerate([
        ('Duett-Telemann.mid', 'I', 'Affettuoso', '4/4', 66), ('Duett-Telemann-1.mid', 'II', 'Allegro', '3/4', 120),
        ('Duett-Telemann-2.mid', 'III', 'Andante', '6/4', 108), ('Duett-Telemann-3.mid', 'IV', 'Presto', '2/2', 132)])
]

PICKUP = {'h': 8, 'q': 4, 'e': 2}
FIFTHS = {'C': 0, 'Am': 0, 'G': 1, 'D': 2, 'F': -1, 'Dm': -1, 'Bb': -2, 'Gm': -2, 'Eb': -3}

def convert(midi, track, time, flats, voice='top', pickup=None):
    args = ['python3', CONV, midi, '--track', str(track), '--time', time, '--voice', voice] + (['--flats'] if flats else [])
    if pickup:
        args += ['--pickup', str(PICKUP[pickup])]
    return [b.strip() for b in subprocess.run(args, capture_output=True, text=True, check=True).stdout.strip().split(' | ')]

ENHARMONIC = {'C#': 'Db', 'D#': 'Eb', 'F#': 'Gb', 'G#': 'Ab', 'A#': 'Bb', 'Db': 'C#', 'Eb': 'D#', 'Gb': 'F#', 'Ab': 'G#', 'Bb': 'A#'}

def respell(bars, key):
    """Black keys as the key's signature has them plus the next flat (E flat in F, A flat in G minor);
    the other black keys are sharps, as leading notes mostly are."""
    f = FIFTHS[key]
    flats = set(FLATS[:max(0, -f) + 1]) if f <= 0 else set()
    flats = {n + 'b' for n in flats} | {'Bb'}
    def fix(m):
        n = m.group(1)
        if len(n) == 2:
            want = n if (n in flats) == n.endswith('b') else ENHARMONIC[n]
            n = want
        return n + m.group(2)
    return [re.sub(r'\b([A-G][#b]?)(-?\d/)', fix, b) for b in bars]

def beats(bar):
    total = 0
    for t in bar.split():
        d = t.split('/')[1].rstrip('~,')
        total += DUR[d[0]] * (1.5 if d.endswith('.') else 1)
    return total

def is_rest(bar):
    return all(t.startswith('R/') for t in bar.split())

def key_for(bars):
    notes = [(m.group(1), {'#': 1, 'b': -1}.get(m.group(2), 0)) for b in bars for m in re.finditer(r'([A-G])(#|b)?-?\d', b)]
    def cost(f):
        alt = {s: (1 if f > 0 else -1) for s in (SHARPS[:f] if f > 0 else FLATS[:-f])}
        return sum(1 for s, a in notes if a != alt.get(s, 0))
    return min(range(-3, 4), key=lambda f: (cost(f), abs(f)))

idx_path = os.path.join(ROOT, 'data', 'songs', 'index.json')
index = json.load(open(idx_path))
for song in SONGS:
    up, low = map(int, song['time'].split('/'))
    full = up * 4 / low
    pickup = song.get('pickup')
    if 'key' in song:
        key = song['key']
    else:
        key = MAJOR[key_for(convert(song['midi'], song['parts'][0][0], song['time'], False))]
    flats = FIFTHS[key] < 0
    parts = [convert(song['midi'], t, song['time'], flats, pickup=pickup) for t, _, _ in song['parts']]
    skip = min(next((i for i, b in enumerate(p) if not is_rest(b)), 0) for p in parts)
    length = max(len(p) for p in parts) - skip
    def finish(bars, part):
        bars = respell(bars[skip:skip + length], key)
        for p, i, was, now in song.get('fix', []):
            if p == part:
                assert bars[i] == was, (song['id'], i, bars[i])
                bars[i] = now
        gap = full - beats(bars[-1])
        while gap > 1e-9:
            r = max(k for k in REST if k <= gap + 1e-9)
            bars[-1] += ' ' + REST[r]
            gap -= r
        return ' | '.join(bars)
    path = os.path.join(ROOT, 'data', 'songs', song['id'] + '.json')
    old = json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}
    out = {
        'id': song['id'], 'title': song['title'], 'composer': song['composer'], 'chapter': old.get('chapter', 0), 'tempo': song['tempo'],
        'time': song['time'], 'key': key, 'source': song['source'],
        'about': old.get('about', song['about']), 'tip': old.get('tip', song['tip']),
        **({'pickup': pickup} if pickup else {}),
        'parts': [{'id': f'part{i + 1}', 'name': name, 'instrument': instr, 'music': finish(p, i)} for i, ((t, name, instr), p) in enumerate(zip(song['parts'], parts))],
    }
    tune_last = out['parts'][0]['music'].split(' | ')[-1].split()
    while tune_last[-1].startswith('R/'):
        tune_last.pop()
    song_end = beats(' '.join(tune_last))
    acc = []
    for t, name, sound, voice in song['accompaniment']:
        bars = respell(convert(song['midi'], t, song['time'], flats, voice, pickup)[skip:skip + length], key)
        # A band part ends with the song: its last note is held until the tune's last note ends.
        last = bars[-1].split()
        gap = song_end - beats(bars[-1])
        if gap > 1e-9 and not last[-1].startswith('R/'):
            pitch, d = last[-1].split('/')
            if held := REST.get(beats('X/' + d) + gap):
                last[-1] = pitch + held[1:]
                bars[-1] = ' '.join(last)
        if not all(is_rest(b) for b in bars):
            acc.append({'id': f'voice{t}', 'name': name, 'sound': sound, 'music': ' | '.join(bars)})
    if acc:
        out['accompaniment'] = acc
    json.dump(out, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    open(path, 'a').write('\n')
    if song['id'] + '.json' not in index:
        index.append(song['id'] + '.json')
    print(f"{song['id']:28} key {key:3} bars {length:3} parts {len(parts)} band {len(acc)}")
json.dump(index, open(idx_path, 'w'), indent=2)
