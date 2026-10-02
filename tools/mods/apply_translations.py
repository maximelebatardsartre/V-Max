# -*- coding: utf-8 -*-
"""Applique TOUTES les traductions FR aux mods chargés : noms (fr_names.py, à la main) + descriptions et dialogues
(translations.fr.json, produit par translate.py avec Gemini). Écrit un lang/fr/vmax-trad.lps par mod, en se basant
sur les textes RÉELLEMENT présents dans les fichiers (clé exacte), pour qu'il ne reste ni chinois ni anglais.

Usage :
    python tools/mods/translate.py          # (une fois la clé Gemini en place) traduit dialogues et descriptions
    python tools/mods/apply_translations.py  # applique tout aux mods chargés
"""
import os, re, sys, glob, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fr_names import FR

CJK = re.compile(r'[\u4e00-\u9fff]')
# mods refus\u00e9s (contenu sexuel) : jamais charg\u00e9s par V-Max, inutile de les traiter
EXCLUDED = {"3027004255", "3027542580", "3030945675", "3031981095", "3035399894", "3042568517",
            "3044723043", "3045450089", "3046644833", "3065265367", "3290665653", "3176916830"}

def unescape(s):  # inverse de l'échappement LinePutScript (comme ingest.py)
    return (s.replace("/stop", ":|").replace("/id", "#").replace("/com", ",")
             .replace("/n", "\n").replace("/r", "\r").replace("/!", "/"))

def esc_value(s):  # échappement LinePutScript, symétrique d'unescape
    # « / » est l'introducteur d'échappement LPS : il doit être protégé EN PREMIER (sinon « 20/20 », « eau/riz »…
    # seraient réinterprétés au chargement). Puis on protège les séquences qui casseraient la ligne.
    s = s.replace("/", "/!")
    return s.replace(":|", "/stop").replace("#", "/id").replace("\n", "/n").replace("\r", "/r")

def read(p):
    try: return open(p, encoding='utf-8-sig').read()
    except UnicodeDecodeError: return open(p, encoding='gb18030', errors='replace').read()

def fields(text, regex):
    for line in text.split('\n'):
        for m in re.finditer(regex, line):
            yield m.group(1), line

def main():
    appdata = os.environ['APPDATA']
    # dialogues/descriptions : d'abord ceux versionnés dans le dépôt (fr_dialogues.json), puis ceux produits
    # par translate.py (%APPDATA%\V-Max\studio\translations.fr.json) qui complètent/écrasent.
    gemini = {}
    repo = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fr_dialogues.json')
    if os.path.exists(repo):
        for mod, d in json.load(open(repo, encoding='utf-8')).items():
            gemini.setdefault(mod, {}).update(d)
    tm_path = os.path.join(appdata, 'V-Max', 'studio', 'translations.fr.json')
    if os.path.exists(tm_path):
        for mod, d in json.load(open(tm_path, encoding='utf-8')).items():
            gemini.setdefault(mod, {}).update(d)
    roots = [os.path.join(appdata, 'V-Max', 'mods'), r'D:\VMAX\MOD\1920960']

    written = mods_done = 0
    missing = {}
    for root in roots:
        if not os.path.isdir(root):
            continue
        for mod in sorted(os.listdir(root)):
            md = os.path.join(root, mod)
            if not os.path.isdir(md) or mod in EXCLUDED:
                continue
            per_mod = gemini.get(mod, {})
            def lookup(raw):
                canon = unescape(raw)
                return FR.get(raw) or FR.get(canon) or per_mod.get(canon) or per_mod.get(raw)
            pairs = {}
            unknown = []
            def handle(raw):
                if not raw:
                    return
                fr = lookup(raw)
                if fr and fr != raw:
                    pairs[raw] = fr
                elif CJK.search(raw):
                    unknown.append(unescape(raw))
            # occupations
            for lps in glob.glob(md + '/pet/*.lps'):
                if lps.endswith('.bak'):
                    continue
                for raw, line in fields(read(lps), r'Name#([^:|]*)'):
                    if line.lower().startswith('work'):
                        handle(raw)
            # aliments : nom + description
            for lps in glob.glob(md + '/food/*.lps'):
                t = read(lps)
                for raw, _ in fields(t, r'(?:^|:\|)name#([^:|]*)'):
                    handle(raw)
                for raw, _ in fields(t, r'desc#([^:|]*)'):
                    handle(raw)
            # dialogues : clic / choix / état faible
            for lps in glob.glob(md + '/text/**/*.lps', recursive=True):
                if lps.endswith('.bak'):
                    continue
                t = read(lps)
                for raw, _ in fields(t, r'Text#([^:|]*)'):
                    handle(raw)
                for raw, _ in fields(t, r'Choose#([^:|]*)'):
                    handle(raw)
            if unknown:
                missing[mod] = sorted(set(unknown))
            if not pairs:
                continue
            mods_done += 1
            content = "".join(f"{k}#{esc_value(v)}:|\n" for k, v in pairs.items())
            langdir = os.path.join(md, 'lang', 'fr')
            os.makedirs(langdir, exist_ok=True)
            open(os.path.join(langdir, 'vmax-trad.lps'), 'w', encoding='utf-8').write(content)
            written += 1

    print(f"{mods_done} dossiers mods traduits · {written} fichiers lang/fr écrits")
    total_missing = sum(len(v) for v in missing.values())
    if total_missing:
        print(f"\n⚠ {total_missing} textes chinois SANS traduction (lance d'abord translate.py avec ta clé Gemini) :")
        for mod, items in list(missing.items())[:8]:
            print(f"  {mod}: {len(items)} (ex. {items[0][:40]})")
    else:
        print("✓ aucun texte chinois restant dans les mods chargés.")

if __name__ == "__main__":
    main()
