#!/usr/bin/env python3
"""Adds accompaniment parts to repertoire songs from the other voices of their source MIDI.

    tools/add-accompaniment.py            # all songs in CONFIG below

Each voice is converted with midi2pipit.py exactly like the player's part was (same time
signature, transposition and spelling), and cut from the same bar: songs whose player's part
had leading rest bars removed lose those bars from every voice, so the parts stay aligned.
"""
import json, os, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONV = os.path.join(ROOT, 'tools', 'midi2pipit.py')
M = os.path.join(ROOT, 'tools', 'sources', 'mutopia')
B = os.path.join(ROOT, 'tools', 'sources', 'personal-bellugi')

CONSORT = lambda tracks: [(t, f'Voice {i + 2}', 'bassoon' if i == len(tracks) - 1 else 'recorder', 'top') for i, t in enumerate(tracks)]

# song id: (midi, player's track, [(track, name, sound, voice)], transpose)
CONFIG = {
    'minuet-in-g': (f'{M}/anna-magdalena-04.mid', 1, [(2, 'Left hand', 'harpsichord', 'bottom')], 0),
    'minuet-in-g-minor': (f'{M}/anna-magdalena-05.mid', 1, [(2, 'Left hand', 'harpsichord', 'bottom')], 0),
    'minuet-in-g-anh116': (f'{M}/anna-magdalena-07.mid', 1, [(2, 'Left hand', 'harpsichord', 'bottom')], 0),
    'musette': (f'{M}/anna-magdalena-22.mid', 1, [(2, 'Left hand', 'harpsichord', 'bottom')], 3),
    'fairest-isle': (f'{M}/FairestIsle.mid', 1, [(2, 'Upper strings', 'strings', 'top'), (3, 'Lower strings', 'strings', 'top'), (4, 'Bass', 'cello', 'bottom')], 0),
    'personal/ductia': (f'{B}/ductia.mid', 1, [(2, 'Lower voice', 'recorder', 'top')], 0),
    'personal/bransle-champaigne-theme': (f'{B}/gerv_b10.mid', 2, CONSORT([3, 4, 5, 6]), 0),
    'personal/bransle-champaigne-diminutions': (f'{B}/gerv_b10.mid', 1, CONSORT([2, 3, 4, 5, 6]), 0),
    'personal/bransle-2-diminutions': (f'{B}/gerv_b2.mid', 1, CONSORT([2, 3, 4, 5, 6]), 0),
    'personal/recoupe-diminutions': (f'{B}/sus_nch.mid', 1, CONSORT([2, 3, 4, 5, 6]), 0),
    'personal/allemaigne-theme': (f'{B}/sus_allm.mid', 2, CONSORT([3, 4, 5, 6]), 0),
    'personal/allemaigne-diminutions': (f'{B}/sus_allm.mid', 1, CONSORT([2, 3, 4, 5, 6]), 0),
    'personal/bransle-gay-2-diminutions': (f'{B}/gerv_bg2.mid', 1, CONSORT([2, 3, 4, 5]), 0),
    'personal/bransle-gay-7-diminutions': (f'{B}/gerv_bg7.mid', 1, CONSORT([2, 3, 4, 5]), 0),
}

def convert(midi, track, time, transpose, flats, voice='top'):
    args = ['python3', CONV, midi, '--track', str(track), '--time', time, '--voice', voice]
    if transpose: args += ['--transpose', str(transpose)]
    if flats: args.append('--flats')
    return [b.strip() for b in subprocess.run(args, capture_output=True, text=True, check=True).stdout.strip().split(' | ')]

def is_rest_bar(bar):
    return all(t.startswith('R/') for t in bar.split())

for song_id, (midi, player, voices, transpose) in CONFIG.items():
    path = os.path.join(ROOT, 'data', 'songs', song_id + '.json')
    song = json.load(open(path, encoding='utf-8'))
    flats = song['key'] in ('F', 'Bb', 'Eb', 'Dm', 'Gm', 'Cm')
    lead = convert(midi, player, song['time'], transpose, flats)
    skip = 0
    while skip < len(lead) and is_rest_bar(lead[skip]):
        skip += 1
    # Sanity check: the song's own part must be these bars (up to the padded last bar). A
    # couple of bars may differ by hand edits (a removed grace note); they are listed.
    mine = [b.strip() for b in song['parts'][0]['music'].split(' | ')]
    theirs = lead[skip:]
    differ = [i + 1 for i, (a, b) in enumerate(zip(mine[:-1], theirs)) if a.split() != b.split()]
    if len(differ) > 2 or len(theirs) < len(mine) - 1:
        sys.exit(f"{song_id}: the song's part does not match track {player} of {os.path.basename(midi)} (bars {differ[:8]})")
    for i in differ:
        print(f'  {song_id} bar {i} edited: {theirs[i - 1]!r} -> {mine[i - 1]!r}')
    parts = []
    for track, name, sound, voice in voices:
        bars = convert(midi, track, song['time'], transpose, flats, voice)[skip:skip + len(mine)]
        if all(is_rest_bar(b) for b in bars):
            continue  # silent in this stretch (e.g. the plain top voice under the diminutions)
        parts.append({'id': f'voice{track}', 'name': name, 'sound': sound, 'music': ' | '.join(bars)})
    song['accompaniment'] = parts
    json.dump(song, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    open(path, 'a').write('\n')
    print(f'{song_id:42} +{len(parts)} voice(s), from bar {skip + 1}')
