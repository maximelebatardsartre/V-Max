#!/usr/bin/env python3
"""V-Max : lecture et catalogue des mods VPet du Workshop (étape 1 du chantier « mods »).

Usage :
    python tools/mods/ingest.py MOD/1920960 [--out <dossier>]

Par défaut, le catalogue est écrit dans %APPDATA%/V-Max/studio/catalog.json ; c'est ce fichier que lisent
le Studio (aperçu des animations) et l'étape de traduction. La source n'est jamais modifiée.

Pour chaque mod : nom, auteur, contenu (animations par type, occupations, nourriture, textes, plugins),
taille, catégorie, et la liste des textes à traduire avec les traductions déjà fournies par le mod (indices).

Règles particulières :
  * Les mods à caractère sexuel sont classés « Exclu » : rien n'est lu au-delà de leur fiche, ils ne sont
    ni traduits, ni proposés dans le Studio, et V-Max refuse de les charger (liste dans excluded.json).
  * Un outil précédent a renommé certains fichiers en place en laissant « *.codex-*.bak » : ces sauvegardes
    sont les fichiers d'origine, ce sont elles qui sont lues (les noms y servent de clés de traduction).
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------------------------------------------------
# Lecture du format LinePutScript (.lps)
# ---------------------------------------------------------------------------------------------------------

def unescape(s: str) -> str:
    """Inverse de Sub.TextReplace de LinePutScript (« /! » en dernier)"""
    return (s.replace("/stop", ":|").replace("/id", "#").replace("/com", ",")
             .replace("/n", "\n").replace("/r", "\r").replace("/!", "/"))


class Line:
    __slots__ = ("name", "info", "subs", "text")

    def __init__(self, name: str, info: str, subs: list[tuple[str, str]], text: str):
        self.name, self.info, self.subs, self.text = name, info, subs, text

    def get(self, key: str, default: str = "") -> str:
        k = key.lower()
        for n, v in self.subs:
            if n.lower() == k:
                return v
        return default


def parse_line(raw: str) -> Line | None:
    raw = raw.lstrip("﻿").rstrip("\r")
    if not raw.strip() or raw.lstrip().startswith("///"):
        return None
    parts = raw.split(":|")
    head = parts[0]
    name, _, info = head.partition("#")
    rest = parts[1:]
    text = ""
    if rest and not raw.endswith(":|"):
        text = rest.pop()
    elif rest and rest[-1] == "":
        rest.pop()
    subs = []
    for p in rest:
        n, _, v = p.partition("#")
        subs.append((unescape(n), unescape(v)))
    return Line(unescape(name.strip()), unescape(info), subs, unescape(text))


def read_text(path: Path) -> str:
    data = path.read_bytes()
    for enc in ("utf-8-sig", "gb18030", "utf-16"):
        try:
            return data.decode(enc)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-8", errors="replace")


def read_lps(path: Path) -> list[Line]:
    return [l for l in (parse_line(r) for r in read_text(path).split("\n")) if l is not None]


def original_of(path: Path) -> tuple[Path, bool]:
    """Fichier d'origine : la sauvegarde laissée par un renommage précédent, si elle existe"""
    for bak in sorted(path.parent.glob(path.name + ".codex-*.bak")):
        return bak, True
    return path, False

# ---------------------------------------------------------------------------------------------------------
# Animations : même logique que PetLoader.LoadGraph et GraphInfo (VPet-Simulator.Core)
# ---------------------------------------------------------------------------------------------------------

GRAPH_TYPES = ["Common", "Raised_Dynamic", "Raised_Static", "Move", "Default", "Touch_Head", "Touch_Body",
               "Idel", "Sleep", "Say", "StateONE", "StateTWO", "StartUP", "Shutdown", "Work", "Switch_Up",
               "Switch_Down", "Switch_Thirsty", "Switch_Hunger", "SideHide_Left_Main", "SideHide_Left_Rise",
               "SideHide_Right_Main", "SideHide_Right_Rise"]
GRAPH_TOKENS = [t.lower().split("_") for t in GRAPH_TYPES]
MODES = ["happy", "nomal", "poorcondition", "ill"]


def _remove(tokens: list[str], value: str) -> bool:
    if value in tokens:
        tokens.remove(value)
        return True
    return False


def graph_info(rel: str, line: Line | None = None) -> dict:
    """Type, nom, humeur et phase d'une animation à partir de son chemin relatif au dossier du compagnon"""
    tokens = [t for t in rel.lower().replace("\\", "_").replace("/", "_").split("_") if t.strip()]
    mode = (line.get("mode") if line else "").lower()
    if mode not in MODES:
        mode = next((m for m in MODES if _remove(tokens, m)), "nomal")
    gtype = (line.get("graph") if line else "")
    match = next((t for t in GRAPH_TYPES if t.lower() == gtype.lower()), None) if gtype else None
    if match is None:
        match = "Common"
        for i, val in enumerate(GRAPH_TOKENS):
            if val[0] in tokens:
                idx = tokens.index(val[0])
                if all(tokens[idx + b] == val[b] for b in range(1, len(val)) if idx + b < len(tokens)):
                    match = GRAPH_TYPES[i]
                    del tokens[idx:idx + len(val)]
                    break
    phase = "Single"
    for name, keys in (("A_Start", ("a", "start")), ("B_Loop", ("b", "loop")), ("C_End", ("c", "end")), ("Single", ("single",))):
        if any(_remove(tokens, k) for k in keys):
            phase = name
            break
    name = line.info if line and line.info else ""
    if not name:
        while tokens and (re.fullmatch(r"-?\d+(\.\d+)?", tokens[-1]) or tokens[-1].startswith("~")):
            tokens.pop()
        name = tokens[-1] if tokens else ""
    return {"type": match, "name": name or match.lower(), "mode": mode, "phase": phase}


def frame_ms(p: Path) -> int:
    m = re.search(r"_(\d+)$", p.stem)
    return int(m.group(1)) if m else 0


def walk_graphs(di: Path, start: Path, out: list[dict]) -> None:
    info = di / "info.lps"
    if info.exists():
        for line in read_lps(info):
            if line.name.lower() not in ("pnganimation", "apnganimation", "picture", "foodanimation"):
                continue
            target = di / line.get("path") if line.get("path") else di
            files = sorted(target.glob("*.png")) if target.is_dir() else [target]
            rel = str(target.relative_to(start)) if target.is_dir() else str(target.with_suffix("").relative_to(start))
            out.append(leaf(target, rel, files, line))
        return
    subdirs = [d for d in di.iterdir() if d.is_dir()]
    if not subdirs:
        files = sorted(f for f in di.iterdir() if f.suffix.lower() in (".png", ".gif", ".jpg"))
        if files:
            out.append(leaf(di, str(di.relative_to(start)), files, None))
        return
    for d in sorted(subdirs):
        walk_graphs(d, start, out)


def leaf(path: Path, rel: str, files: list[Path], line: Line | None) -> dict:
    g = graph_info(rel, line)
    pngs = [f for f in files if f.suffix.lower() == ".png"]
    g.update({
        "path": str(path),
        "frames": len(files),
        "duration_ms": sum(frame_ms(f) for f in pngs),
        "bytes": sum(f.stat().st_size for f in files if f.is_file()),
        "preview": str(files[len(files) // 2]) if files else "",
        "loader": line.name.lower() if line else ("picture" if len(files) == 1 else "pnganimation"),
    })
    return g

# ---------------------------------------------------------------------------------------------------------
# Exclusion
# ---------------------------------------------------------------------------------------------------------

# Mods à caractère sexuel repérés à l'inventaire du 2026-10-02 (décision : jamais traités ni chargés)
EXCLUDED_IDS = {
    "3027004255", "3027542580", "3030945675", "3031981095", "3035399894", "3042568517",
    "3044723043", "3045450089", "3046644833", "3065265367", "3290665653",
    "3176916830",  # titre neutre, contenu sexuel (repéré par le contrôle des textes)
}
EXCLUDE_PATTERN = re.compile(
    r"r-?18|18\+|nsfw|porn|hentai|lewd|色情|裸|自慰|女上位|奈子|看光光|诱惑|誘惑|打屁股|涩涩|瑟瑟|成人", re.I)
# Répliques ambiguës dans un mod par ailleurs anodin : la ligne est bloquée (ni traduite, ni intégrée), pas le mod
LINE_PATTERN = re.compile(r"舔|色眯眯|变态|變態|❤")
# Contrôle du contenu (noms d'animations et textes), pour les mods dont le titre ne dit rien
CONTENT_PATTERN = re.compile(r"sex|肉棒|好深|要坏掉|要壞掉|高潮|做爱|做愛|内射|內射|小穴|乳头|乳頭|脱衣|脫衣|nude|naked", re.I)

# ---------------------------------------------------------------------------------------------------------
# Lecture d'un mod
# ---------------------------------------------------------------------------------------------------------

TEXT_LINES = {"clicktext": "click", "lowfoodtext": "low", "lowdrinktext": "low", "selecttext": "select"}
CATEGORY_OF_WORK = {"work": "Travail", "study": "Études", "play": "Loisir"}


def dir_stats(d: Path) -> tuple[int, int]:
    size = count = 0
    for root, _, files in os.walk(d):
        for f in files:
            size += os.path.getsize(os.path.join(root, f))
            count += 1
    return size, count


class Strings:
    """Textes à traduire d'un mod : clé source → type, contexte, indices de traduction existants"""

    def __init__(self):
        self.items: dict[str, dict] = {}

    def add(self, key: str, kind: str, context: str = "") -> None:
        key = key.strip()
        if not key or re.fullmatch(r"[\d\W_]+", key):
            return
        item = self.items.setdefault(key, {"key": key, "kind": kind, "context": context, "hints": {}})
        if LINE_PATTERN.search(key):
            item["blocked"] = True
        if item["kind"] == "lang" and kind != "lang":
            item["kind"], item["context"] = kind, context

    def hint(self, key: str, culture: str, value: str) -> None:
        key = key.strip()
        if not key or not value.strip():
            return
        self.add(key, "lang")
        if key in self.items:
            self.items[key]["hints"].setdefault(culture, value)


def read_mod(d: Path) -> dict:
    mod: dict = {"id": d.name, "source": str(d)}
    info_path = d / "info.lps"
    info_src, info_restored = original_of(info_path) if info_path.exists() else (info_path, False)
    info = read_lps(info_src) if info_src.exists() else []
    head = next((l for l in info if l.name.lower() == "vupmod"), None)
    def field(n: str) -> str:
        return next((l.info for l in info if l.name.lower() == n), "")
    mod.update({
        "name": head.info if head else "",
        "author": head.get("author") if head else "",
        "version": head.get("ver") if head else "",
        "game_version": head.get("gamever") if head else "",
        "intro": field("intro"),
        "workshop_url": f"https://steamcommunity.com/sharedfiles/filedetails/?id={d.name}",
    })

    # exclusion : décidée sur la fiche uniquement, rien d'autre n'est lu
    if d.name in EXCLUDED_IDS or EXCLUDE_PATTERN.search(mod["name"] + " " + mod["intro"]):
        return {"id": d.name, "category": "Exclu", "reason": "contenu à caractère sexuel", "source": str(d)}

    files = [p for p in d.iterdir()]
    if not info and all(p.is_dir() and all(f.name.endswith(".bak") for f in p.rglob("*") if f.is_file()) or p.name.endswith(".bak") for p in files):
        return {"id": d.name, "category": "Vide", "reason": "le dossier ne contient plus que des sauvegardes .bak d'un outil précédent",
                "source": str(d), "name": mod["name"]}

    size, count = dir_stats(d)
    mod.update({"bytes": size, "files": count, "restored": []})
    if info_restored:
        mod["restored"].append(str(info_src.relative_to(d)))
    strings = Strings()
    strings.add(mod["name"], "mod_name")
    strings.add(mod["intro"], "mod_intro")
    for l in info:
        if l.name.lower() in ("lang", "culturedatas"):
            for k, v in l.subs:
                strings.hint(k, l.info or "?", v)

    # compagnons, occupations, animations
    works, graphs, other_lines, pets = [], [], {}, []
    pet_dir = d / "pet"
    if pet_dir.is_dir():
        for lps in sorted(pet_dir.glob("*.lps")):
            src, restored = original_of(lps)
            if restored:
                mod["restored"].append(str(src.relative_to(d)))
            lines = read_lps(src)
            petline = next((l for l in lines if l.name.lower() == "pet"), None)
            folder = pet_dir / (petline.get("path") if petline and petline.get("path") else lps.stem)
            pets.append({"file": str(lps.relative_to(d)), "pet": petline.info if petline else lps.stem, "folder": str(folder)})
            for l in lines:
                n = l.name.lower()
                if n == "work":
                    w = {"type": l.get("Type") or "Work", "name": l.get("Name"), "graph": l.get("Graph"),
                         "money": l.get("MoneyBase"), "level": l.get("LevelLimit"), "time": l.get("Time")}
                    works.append(w)
                    strings.add(w["name"], "work", f"occupation ({w['type']})")
                elif n != "pet":
                    other_lines[n] = other_lines.get(n, 0) + 1
            if folder.is_dir():
                walk_graphs(folder, folder, graphs)
        for folder in sorted(p for p in pet_dir.iterdir() if p.is_dir()):
            if not any(p["folder"] == str(folder) for p in pets):
                pets.append({"file": "", "pet": folder.name, "folder": str(folder)})
                walk_graphs(folder, folder, graphs)

    # nourriture
    foods = []
    for lps in sorted((d / "food").glob("*.lps")) if (d / "food").is_dir() else []:
        for l in read_lps(lps):
            if l.name.lower() == "food":
                f = {"name": l.get("name"), "type": l.get("type"), "price": l.get("price"), "desc": l.get("desc")}
                foods.append(f)
                strings.add(f["name"], "food_name", f"nourriture ({f['type']})")
                strings.add(f["desc"], "food_desc", f"description de « {f['name']} »")

    # textes (clic, état faible, choix)
    texts: dict[str, int] = {}
    for lps in sorted((d / "text").rglob("*.lps")) if (d / "text").is_dir() else []:
        src, restored = original_of(lps)
        if restored:
            mod["restored"].append(str(src.relative_to(d)))
        for l in read_lps(src):
            kind = TEXT_LINES.get(l.name.lower())
            if not kind:
                continue
            texts[kind] = texts.get(kind, 0) + 1
            ctx = l.get("Working") and f"pendant « {l.get('Working')} »" or l.get("State")
            strings.add(l.get("Text"), kind, ctx)
            if kind == "select":
                strings.add(l.get("Choose"), "select_choice", ctx)

    # traductions fournies par le mod (indices pour l'étape de traduction)
    for lang_dir in sorted((d / "lang").iterdir()) if (d / "lang").is_dir() else []:
        if lang_dir.is_dir():
            for lps in sorted(lang_dir.glob("*.lps")):
                for l in read_lps(lps):
                    strings.hint(l.name, lang_dir.name, l.info)

    # plugins (code) et fichiers lus par un plugin
    plugins = []
    for dll in sorted((d / "plugin").glob("*.dll")) if (d / "plugin").is_dir() else []:
        plugins.append({"file": dll.name, "bytes": dll.stat().st_size, "sha256": hashlib.sha256(dll.read_bytes()).hexdigest()})
    plugin_texts = sorted(p.name for p in d.glob("*.txt"))

    # résumé des animations : une entrée par (type, nom), avec humeurs et phases disponibles
    groups: dict[tuple[str, str], dict] = {}
    for g in graphs:
        key = (g["type"], g["name"])
        grp = groups.setdefault(key, {"type": g["type"], "name": g["name"], "modes": set(), "phases": set(),
                                      "frames": 0, "duration_ms": 0, "bytes": 0, "preview": g["preview"], "variants": []})
        grp["modes"].add(g["mode"])
        grp["phases"].add(g["phase"])
        grp["frames"] += g["frames"]
        grp["duration_ms"] += g["duration_ms"]
        grp["bytes"] += g["bytes"]
        grp["variants"].append({k: g[k] for k in ("mode", "phase", "path", "frames", "duration_ms", "loader")})
    animations = []
    for grp in groups.values():
        grp["modes"], grp["phases"] = sorted(grp["modes"]), sorted(grp["phases"])
        grp["works"] = [w["name"] for w in works if w["graph"].lower() == grp["name"].lower()]
        animations.append(grp)
    animations.sort(key=lambda a: (a["type"], a["name"]))

    # contrôle du contenu : un mod exclu ne garde que sa fiche minimale
    hits = [a["name"] for a in animations if CONTENT_PATTERN.search(a["name"])] +            [t["key"] for t in strings.items.values() if CONTENT_PATTERN.search(t["key"])]
    if hits:
        return {"id": d.name, "category": "Exclu", "reason": "contenu à caractère sexuel (repéré dans les animations ou les textes)", "source": str(d)}

    # surcharge de textes du jeu de base (pack de langue) : nourriture déjà présente dans 0000_core
    core_foods = core_food_names()
    overrides = bool(foods) and sum(f["name"] in core_foods for f in foods) >= 0.8 * len(foods)

    # catégorie
    tags = set()
    for w in works:
        tags.add(CATEGORY_OF_WORK.get(w["type"].lower(), "Loisir"))
    if foods:
        tags.add("Nourriture")
    if any(a["type"] != "Work" and not a["works"] for a in animations):
        tags.add("Attitude")
    if plugins:
        tags.add("Système")
    if texts or plugin_texts:
        tags.add("Dialogues")
    only_lang = not (works or foods or animations or plugins or texts or plugin_texts) and (d / "lang").is_dir()
    if only_lang or (not works and not animations and (d / "lang").is_dir() and not plugins and not foods and not texts):
        tags.add("Langue")
    if overrides:
        tags.discard("Nourriture")
        tags.add("Langue")
        category = "Langue"
    elif foods and len(foods) > len(works):
        category = "Nourriture"
    elif works:
        counts: dict[str, int] = {}
        for w in works:
            c = CATEGORY_OF_WORK.get(w["type"].lower(), "Loisir")
            counts[c] = counts.get(c, 0) + 1
        category = max(counts, key=lambda c: counts[c])
    else:
        category = next((c for c in ("Nourriture", "Attitude", "Système", "Dialogues", "Langue") if c in tags), "Vide")

    en = strings.items.get(mod["name"], {}).get("hints", {}).get("en", "")
    mod.update({
        "category": category,
        "tags": sorted(tags),
        "name_en": en,
        "slug": slugify(en or mod["name"]) or f"mod-{d.name}",
        "pets": pets,
        "works": works,
        "foods": foods,
        "texts": texts,
        "plugin_texts": plugin_texts,
        "plugins": plugins,
        "other_lines": other_lines,
        "animations": animations,
        "strings": list(strings.items.values()),
    })
    return mod


_core_foods: set[str] | None = None


def core_food_names() -> set[str]:
    """Noms des aliments du jeu de base (VPet-Simulator.Windows/mod/0000_core/food)"""
    global _core_foods
    if _core_foods is None:
        core = Path(__file__).resolve().parents[2] / "VPet-Simulator.Windows" / "mod" / "0000_core" / "food"
        _core_foods = {l.get("name") for f in core.glob("*.lps") for l in read_lps(f) if l.name.lower() == "food"}
    return _core_foods


def slugify(s: str) -> str:
    s = re.sub(r"[^A-Za-z0-9]+", "-", s).strip("-").lower()
    return s[:40]

# ---------------------------------------------------------------------------------------------------------


def default_out() -> Path:
    base = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    return Path(base) / "V-Max" / "studio"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source", type=Path, help="dossier contenant un sous-dossier par mod (ex. MOD/1920960)")
    ap.add_argument("--out", type=Path, default=default_out(), help="dossier de sortie (défaut : %%APPDATA%%/V-Max/studio)")
    args = ap.parse_args()
    if not args.source.is_dir():
        print(f"Dossier introuvable : {args.source}", file=sys.stderr)
        return 1

    mods = []
    for d in sorted(p for p in args.source.resolve().iterdir() if p.is_dir()):
        try:
            mods.append(read_mod(d))
        except Exception as e:  # un mod abîmé ne doit pas bloquer les autres
            mods.append({"id": d.name, "category": "Erreur", "reason": f"{type(e).__name__}: {e}", "source": str(d)})

    args.out.mkdir(parents=True, exist_ok=True)
    # mods ajoutés à la main dans le Studio (glisser-déposer) : conservés tels quels
    previous = args.out / "catalog.json"
    if previous.exists():
        try:
            known = {m["id"] for m in mods}
            kept = [m for m in json.loads(previous.read_text(encoding="utf-8")).get("mods", [])
                    if m.get("origin") == "manuel" and m.get("id") not in known]
            mods.extend(kept)
        except (json.JSONDecodeError, OSError):
            pass
    counts: dict[str, int] = {}
    for m in mods:
        counts[m["category"]] = counts.get(m["category"], 0) + 1
    catalog = {
        "generated": dt.datetime.now().isoformat(timespec="seconds"),
        "source": str(args.source.resolve()),
        "counts": counts,
        "mods": mods,
    }
    (args.out / "catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=1), encoding="utf-8")
    excluded = sorted(m["id"] for m in mods if m["category"] == "Exclu")
    (args.out / "excluded.json").write_text(json.dumps({"workshop_ids": excluded}, indent=1), encoding="utf-8")

    print(f"{len(mods)} mods lus → {args.out / 'catalog.json'}")
    for c, n in sorted(counts.items(), key=lambda kv: -kv[1]):
        print(f"  {c:<12} {n}")
    print()
    print(f"{'id':<11} {'catégorie':<11} {'anim.':>5} {'occ.':>4} {'textes':>6} {'Mo':>6}  nom")
    for m in sorted(mods, key=lambda m: (m["category"], m["id"])):
        if m["category"] in ("Exclu", "Vide", "Erreur"):
            print(f"{m['id']:<11} {m['category']:<11} {'':>5} {'':>4} {'':>6} {'':>6}  ({m.get('reason', '')})")
            continue
        blocked = sum(1 for t in m["strings"] if t.get("blocked"))
        print(f"{m['id']:<11} {m['category']:<11} {len(m['animations']):>5} {len(m['works']):>4} {len(m['strings']) - blocked:>6} "
              f"{m['bytes'] / 1e6:>6.1f}  {m['name']}" + (f" / {m['name_en']}" if m['name_en'] else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
