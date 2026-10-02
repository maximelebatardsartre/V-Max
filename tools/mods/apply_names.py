# -*- coding: utf-8 -*-
"""Applique les traductions FR aux mods, en se basant sur les noms RÉELLEMENT présents dans les fichiers chargés
(certains ont été renommés en anglais par l'outil « codex »). Écrit lang/fr/vmax-trad.lps dans chaque mod."""
import os, re, sys, glob, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fr_names import FR

def esc(s):
    # on n'échappe PAS « / » : une barre oblique isolée reste littérale pour LinePutScript et les clés
    # comme « \Cheer/ » doivent matcher le nom d'occupation tel quel
    return (s.replace(":|", "/stop").replace("#", "/id")
             .replace("\n", "/n").replace("\r", "/r"))

def read(p):
    try: return open(p, encoding='utf-8-sig').read()
    except UnicodeDecodeError: return open(p, encoding='gb18030', errors='replace').read()

appdata = os.environ['APPDATA']
roots = [os.path.join(appdata, 'V-Max', 'mods'), r'D:\VMAX\MOD\1920960']

written = mods_done = 0
missing = set()
for root in roots:
    if not os.path.isdir(root):
        continue
    for mod in os.listdir(root):
        md = os.path.join(root, mod)
        if not os.path.isdir(md):
            continue
        pairs = {}
        unknown = []
        # noms d'occupations (work Name) et d'aliments (food name) réellement chargés
        for lps in glob.glob(md + '/pet/*.lps'):
            if lps.endswith('.bak'):
                continue
            for line in read(lps).split('\n'):
                if not line.lower().startswith('work'):
                    continue
                m = re.search(r'Name#([^:|]*)', line)
                if m:
                    nm = m.group(1)
                    if nm in FR: pairs[nm] = FR[nm]
                    elif re.search(r'[\u4e00-\u9fff]', nm): unknown.append(nm)
        for lps in glob.glob(md + '/food/*.lps'):
            for line in read(lps).split('\n'):
                m = re.search(r'name#([^:|]*)', line)
                if m:
                    nm = m.group(1)
                    if nm in FR: pairs[nm] = FR[nm]
                    elif re.search(r'[\u4e00-\u9fff]', nm): unknown.append(nm)
        missing.update(unknown)
        if not pairs:
            continue
        mods_done += 1
        content = "".join(f"{esc(k)}#{esc(v)}:|\n" for k, v in pairs.items())
        langdir = os.path.join(md, 'lang', 'fr')
        os.makedirs(langdir, exist_ok=True)
        open(os.path.join(langdir, 'vmax-trad.lps'), 'w', encoding='utf-8').write(content)
        written += 1

print(f"{mods_done} mods (x roots) · {written} fichiers lang/fr écrits")
if missing:
    print(f"noms chinois chargés SANS traduction ({len(missing)}):")
    for n in sorted(missing): print("   ", n)
else:
    print("tous les noms chargés (occupations + aliments) sont traduits.")
