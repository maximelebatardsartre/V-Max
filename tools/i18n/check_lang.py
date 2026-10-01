"""Validate a translated V-Max language folder against the English reference.

Usage:  python tools/i18n/check_lang.py fr [file1.lps file2.lps ...]

Checks, for each file (or every file in lang/en when none given):
  * same keys (left of the first '#') in the same order as lang/en
  * every line ends with ':|' and no stray ':|' inside the value
  * placeholders ({0}, {0:f2}, {name}, {hostname}, ...) identical to the English value
  * escaped newlines (\\n and /n) count matches the English value
  * UTF-8 without BOM, CRLF line endings
Exit code 1 when any problem is found.
"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'VPet-Simulator.Windows', 'mod', '0000_core', 'lang')
PH = re.compile(r'\{[A-Za-z0-9_]+(?::[^}]*)?\}')


def split(line):
    if not line.endswith(':|'):
        return None, None
    body = line[:-2]
    if '#' not in body:
        return body, ''
    k, v = body.split('#', 1)
    return k, v


def check(lang, name):
    errs = []
    en_path = os.path.join(ROOT, 'en', name)
    tr_path = os.path.join(ROOT, lang, name)
    if not os.path.exists(tr_path):
        return [f'{name}: missing']
    raw = open(tr_path, 'rb').read()
    if raw.startswith(b'\xef\xbb\xbf'):
        errs.append(f'{name}: has BOM')
    text = raw.decode('utf-8')
    if '\n' in text.replace('\r\n', ''):
        errs.append(f'{name}: LF-only line endings (need CRLF)')
    en = [l.rstrip() for l in open(en_path, encoding='utf-8-sig').read().splitlines()]
    tr = [l.rstrip() for l in text.lstrip('﻿').splitlines()]
    en_ent = [(i, l) for i, l in enumerate(en) if l.strip()]
    tr_ent = [(i, l) for i, l in enumerate(tr) if l.strip()]
    if len(en_ent) != len(tr_ent):
        errs.append(f'{name}: {len(tr_ent)} non-empty lines, expected {len(en_ent)}')
    for (ei, el), (ti, tl) in zip(en_ent, tr_ent):
        ek, ev = split(el)
        tk, tv = split(tl)
        if ek is None:
            if el != tl:
                errs.append(f'{name}:{ti + 1}: non-entry line changed')
            continue
        if tk is None:
            errs.append(f'{name}:{ti + 1}: line does not end with ":|"')
            continue
        if ek != tk:
            errs.append(f'{name}:{ti + 1}: key changed\n    en: {ek[:80]}\n    fr: {tk[:80]}')
            continue
        if ':|' in tv:
            errs.append(f'{name}:{ti + 1}: raw ":|" inside value')
        if sorted(PH.findall(ev)) != sorted(PH.findall(tv)):
            errs.append(f'{name}:{ti + 1}: placeholders {PH.findall(ev)} -> {PH.findall(tv)}')
        for esc in ('\\n', '/n'):
            if ev.count(esc) != tv.count(esc):
                errs.append(f'{name}:{ti + 1}: "{esc}" count {ev.count(esc)} -> {tv.count(esc)}')
        if tv.strip() == '' and ev.strip() != '':
            errs.append(f'{name}:{ti + 1}: empty translation')
    return errs


def main():
    lang = sys.argv[1]
    files = sys.argv[2:] or sorted(os.listdir(os.path.join(ROOT, 'en')))
    all_errs = []
    for f in files:
        all_errs += check(lang, os.path.basename(f))
    for e in all_errs:
        print(e)
    print(f'{len(files)} file(s) checked, {len(all_errs)} problem(s)')
    sys.exit(1 if all_errs else 0)


if __name__ == '__main__':
    main()
