#!/usr/bin/env python3
"""V-Max : traduction en français des textes des mods du catalogue (étape 4 du chantier « mods »).

Usage :
    python tools/mods/translate.py --dry-run                 # compte les textes et estime le coût, sans appel
    python tools/mods/translate.py                           # traduit tout ce qui ne l'est pas encore
    python tools/mods/translate.py --verdicts garder,integrer # seulement les mods gardés dans le Studio
    python tools/mods/translate.py --mods 3060511588,3133902323

Lit %APPDATA%/V-Max/studio/catalog.json (tools/mods/ingest.py) et écrit :
  * tm.fr.json            mémoire de traduction (texte source → français) : une reprise ne repaie rien
  * translations.fr.json  { identifiant du mod : { texte source : français } }, lu par le Studio

Clé : celle que V-Max utilise déjà (Gestionnaire d'identifiants Windows, « V-Max/GeminiApiKey »), ou les variables
GEMINI_API_KEY / VMAX_GEMINI_APIKEY. La clé part dans l'en-tête x-goog-api-key, jamais dans l'URL ni dans un fichier.
Les répliques marquées « blocked » par l'ingestion ne sont jamais envoyées.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

MODEL = "gemini-flash-latest"
BATCH = 40
HIDDEN = {"Exclu", "Vide", "Erreur", "Langue"}

SYSTEM = """Tu es traductrice-adaptatrice pour V-Max, un compagnon de bureau haut de gamme. Tu produis un français
IRRÉPROCHABLE : naturel, fluide, oral, vivant — jamais une traduction littérale, scolaire ou robotique. Tu adaptes
le sens et l'intention, pas les mots.

Le personnage s'appelle MAXINE : une jeune femme animée, pétillante, familière et un brin taquine.
- Remplace toujours 萝莉斯, 萝莉丝, 蘿莉斯, 萝莉, Lolis, Loris, Lolisi, Max, vup par « Maxine ». Quand elle parle d'elle, accorde au féminin.
- 主人 / master / 主人公 / 主人们 = l'utilisateur : tutoie-le directement (« tu », « toi »), appelle-le « mon humain » si besoin. N'écris JAMAIS « maître ».
- Garde la personnalité de Maxine : enjouée, complice, un peu culottée, jamais plate. Les répliques doivent sonner comme ELLE, pas comme un assistant.

Exigences :
- Aucun caractère chinois ni mot anglais ne doit subsister dans ta sortie (sauf noms de marque officiels : Coca-Cola, McDonald's, Genshin, Oreo…).
- Adapte les mèmes, jeux de mots et expressions chinoises en équivalents français savoureux et compréhensibles.
- Conserve À L'IDENTIQUE les variables et échappements : {0}, {1}, {name}, {0:f1}, /n, \\n, /com, les émoticônes et la ponctuation expressive (~, ♪, !, …, ❤).
- Retire les étiquettes d'identifiant entre parenthèses en fin de texte, comme « (StudyingAtSchool) ».
- Noms d'occupations et d'aliments : courts, naturels, majuscule initiale seulement (noms de plats réels quand ils existent).
- Une ligne « en » éventuelle est une traduction anglaise existante : simple indice, traduis depuis la source et fais MIEUX.
- N'ajoute rien, ne censure rien, ne commente rien. Réponds uniquement avec le tableau JSON demandé."""

KIND_LABEL = {
    "mod_name": "nom du mod", "mod_intro": "description du mod", "work": "nom d'une occupation", "food_name": "nom d'un aliment",
    "food_desc": "description d'un aliment", "click": "réplique quand on clique sur Max", "select": "réplique de dialogue",
    "select_choice": "choix de dialogue proposé à l'utilisateur", "low": "réplique quand Max a faim ou soif", "lang": "texte de l'interface du mod",
}


def studio_dir() -> Path:
    base = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    return Path(base) / "V-Max" / "studio"


def read_key() -> str | None:
    for var in ("GEMINI_API_KEY", "VMAX_GEMINI_APIKEY"):
        if os.environ.get(var):
            return os.environ[var]
    if sys.platform != "win32":
        return None
    import ctypes
    from ctypes import wintypes

    class CREDENTIAL(ctypes.Structure):
        _fields_ = [("Flags", wintypes.DWORD), ("Type", wintypes.DWORD), ("TargetName", wintypes.LPWSTR),
                    ("Comment", wintypes.LPWSTR), ("LastWritten", wintypes.FILETIME), ("CredentialBlobSize", wintypes.DWORD),
                    ("CredentialBlob", ctypes.POINTER(ctypes.c_char)), ("Persist", wintypes.DWORD), ("AttributeCount", wintypes.DWORD),
                    ("Attributes", ctypes.c_void_p), ("TargetAlias", wintypes.LPWSTR), ("UserName", wintypes.LPWSTR)]

    advapi = ctypes.WinDLL("advapi32", use_last_error=True)
    ptr = ctypes.POINTER(CREDENTIAL)()
    if not advapi.CredReadW("V-Max/GeminiApiKey", 1, 0, ctypes.byref(ptr)):
        return None
    try:
        c = ptr.contents
        return ctypes.string_at(c.CredentialBlob, c.CredentialBlobSize).decode("utf-16-le")
    finally:
        advapi.CredFree(ptr)


def placeholders(s: str) -> list[str]:
    return sorted(re.findall(r"\{[^{}]*\}|/n|\\n|/com", s))


def valid(src: str, fr: str) -> str | None:
    """Raison du refus, ou None si la traduction est acceptable"""
    if not fr.strip():
        return "vide"
    if placeholders(src) != placeholders(fr):
        return f"variables {placeholders(src)} ≠ {placeholders(fr)}"
    if len(fr) > 6 * len(src) + 40:
        return "beaucoup trop longue"
    return None


def call_gemini(key: str, model: str, items: list[dict], mod_context: str) -> tuple[dict[str, str], dict]:
    base = (os.environ.get("VMAX_GEMINI_BASEURL") or "https://generativelanguage.googleapis.com/v1beta/").rstrip("/") + "/"
    payload = {"contexte": mod_context, "textes": items}
    body = {
        "systemInstruction": {"parts": [{"text": SYSTEM}]},
        "contents": [{"role": "user", "parts": [{"text": "Traduis chaque texte en français. Réponds par un tableau JSON [{\"id\":…,\"fr\":…}].\n"
                                                         + json.dumps(payload, ensure_ascii=False)}]}],
        "generationConfig": {
            "temperature": 0.3,
            "responseMimeType": "application/json",
            "responseSchema": {"type": "ARRAY", "items": {"type": "OBJECT", "properties": {"id": {"type": "STRING"}, "fr": {"type": "STRING"}}, "required": ["id", "fr"]}},
        },
    }
    req = urllib.request.Request(f"{base}models/{model}:generateContent", data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key}, method="POST")
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                data = json.loads(r.read().decode("utf-8"))
            break
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 503) and attempt < 3:
                time.sleep(4 * (attempt + 1))
                continue
            raise RuntimeError(f"Gemini a répondu {e.code} : {e.read().decode('utf-8', 'replace')[:300]}") from None
    text = "".join(p.get("text", "") for p in data["candidates"][0]["content"]["parts"])
    out = {str(x["id"]): x["fr"] for x in json.loads(text) if isinstance(x, dict) and "id" in x and "fr" in x}
    return out, data.get("usageMetadata", {})


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true", help="compte et estime, sans appel à l'API")
    ap.add_argument("--mods", default="", help="identifiants séparés par des virgules")
    ap.add_argument("--verdicts", default="", help="ne traduire que les mods ayant ces verdicts dans le Studio (ex. garder,integrer)")
    ap.add_argument("--model", default=os.environ.get("VMAX_TRANSLATE_MODEL", MODEL))
    ap.add_argument("--batch", type=int, default=BATCH)
    ap.add_argument("--studio", type=Path, default=studio_dir(), help="dossier du catalogue (défaut : %%APPDATA%%/V-Max/studio)")
    args = ap.parse_args()

    catalog = json.loads((args.studio / "catalog.json").read_text(encoding="utf-8"))
    mods = [m for m in catalog["mods"] if m["category"] not in HIDDEN]
    if args.mods:
        wanted = {x.strip() for x in args.mods.split(",") if x.strip()}
        mods = [m for m in mods if m["id"] in wanted]
    if args.verdicts:
        vpath = args.studio / "verdicts.json"
        verdicts = json.loads(vpath.read_text(encoding="utf-8")) if vpath.exists() else {}
        keep = {x.strip() for x in args.verdicts.split(",")}
        mods = [m for m in mods if verdicts.get(m["id"], {}).get("verdict") in keep]

    tm_path = args.studio / "tm.fr.json"
    tm: dict[str, str] = json.loads(tm_path.read_text(encoding="utf-8")) if tm_path.exists() else {}

    todo_per_mod: list[tuple[dict, list[dict]]] = []
    total_chars = total = blocked = 0
    for m in mods:
        items = []
        for s in m.get("strings", []):
            if s.get("blocked"):
                blocked += 1
                continue
            total += 1
            if s["key"] in tm:
                continue
            items.append(s)
            total_chars += len(s["key"]) + len(s.get("hints", {}).get("en", ""))
        if items:
            todo_per_mod.append((m, items))
    n_todo = sum(len(i) for _, i in todo_per_mod)
    # estimation grossière : ~1 jeton pour 1,5 caractère chinois ou 4 caractères latins, + consignes par lot
    est_in = int(total_chars / 1.5) + (n_todo // max(1, args.batch) + len(todo_per_mod)) * 450
    est_out = int(n_todo * 30 + total_chars * 1.2)
    print(f"{len(mods)} mods · {total} textes ({blocked} écartés) · {total - n_todo} déjà traduits · {n_todo} à traduire")
    print(f"Estimation : ~{est_in:_} jetons envoyés et ~{est_out:_} jetons reçus avec {args.model}".replace("_", " "))
    if args.dry_run or n_todo == 0:
        write_output(args.studio, catalog, tm)
        return 0

    key = read_key()
    if not key:
        print("Aucune clé Gemini : enregistre-la dans V-Max (Paramètres › IA) ou définis GEMINI_API_KEY.", file=sys.stderr)
        return 2

    used_in = used_out = 0
    rejected: list[str] = []
    for m, items in todo_per_mod:
        done = 0
        context = f"Mod « {m.get('name', '')} » ({m.get('category', '')}). {m.get('intro', '')[:200]}"
        for i in range(0, len(items), args.batch):
            chunk = items[i:i + args.batch]
            req = [{"id": str(j), "type": KIND_LABEL.get(s["kind"], s["kind"]), "contexte": s.get("context", ""), "source": s["key"],
                    **({"en": s["hints"]["en"]} if s.get("hints", {}).get("en") else {})} for j, s in enumerate(chunk)]
            try:
                out, usage = call_gemini(key, args.model, req, context)
            except Exception as e:
                print(f"  {m['id']} : lot ignoré ({e})", file=sys.stderr)
                continue
            used_in += usage.get("promptTokenCount", 0)
            used_out += usage.get("candidatesTokenCount", 0)
            for j, s in enumerate(chunk):
                fr = out.get(str(j), "")
                why = valid(s["key"], fr)
                if why:
                    rejected.append(f"{m['id']} | {s['key'][:60]} | {why}")
                    continue
                tm[s["key"]] = fr
                done += 1
            tm_path.write_text(json.dumps(tm, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"  {m['id']} {m.get('name', '')} : {done}/{len(items)} traduits")
    write_output(args.studio, catalog, tm)
    print(f"Jetons utilisés : {used_in:_} envoyés et {used_out:_} reçus".replace("_", " "))
    if rejected:
        print(f"{len(rejected)} traduction(s) refusée(s) (à relancer) :")
        for r in rejected[:30]:
            print("  " + r)
    return 0


def write_output(studio: Path, catalog: dict, tm: dict[str, str]) -> None:
    result: dict[str, dict[str, str]] = {}
    for m in catalog["mods"]:
        if m["category"] in HIDDEN:
            continue
        per = {s["key"]: tm[s["key"]] for s in m.get("strings", []) if not s.get("blocked") and s["key"] in tm}
        if per:
            result[m["id"]] = per
    (studio / "translations.fr.json").write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
