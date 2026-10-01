"""Static translation coverage: keys used in C#/XAML/data files that have no entry in lang/<culture>.

Usage: python tools/i18n/coverage.py fr [--write missing.txt]
Sources scanned:
  * C#   : "literal".Translate(...)  /  LocalizeCore.Translate("literal")
  * XAML : {ll:Str literal} / {ll:Str 'literal'}
  * mod/0000_core data (*.lps outside lang/): fields that are translated at runtime
    (Text#, Choose#, name#, desc#, Name#, Describe#, petname#, intor#, tags#)
Only keys containing a CJK character are reported (other literals are usually ids).
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CORE = os.path.join(ROOT, 'VPet-Simulator.Windows', 'mod', '0000_core')
CJK = re.compile(r'[㐀-鿿]')
CS_RE = re.compile(r'(?<![@$])"((?:[^"\\\n]|\\.)*)"\s*\.Translate\(')
CS_RE2 = re.compile(r'LocalizeCore\.Translate\(\s*"((?:[^"\\\n]|\\.)*)"')
XAML_RE = re.compile(r"\{ll:Str\s+(?:'([^']*)'|([^,}]+?))\s*[,}]")
DATA_FIELDS = ('Text', 'Choose', 'name', 'desc', 'Name', 'Describe', 'petname', 'intor', 'tags')


def unescape_cs(s):
    return s.encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8') if '\\' in s else s


def xaml_unescape(s):
    return s.replace('\\&#13;', '\r').replace('&#13;', '\r').replace('&#10;', '\n').replace('&amp;', '&').replace('&lt;', '<').replace('&gt;', '>').replace('&quot;', '"')


def lang_keys(culture):
    keys = set()
    d = os.path.join(CORE, 'lang', culture)
    for f in os.listdir(d):
        for line in open(os.path.join(d, f), encoding='utf-8-sig').read().splitlines():
            line = line.rstrip()
            if line.endswith(':|') and '#' in line:
                k = line[:-2].split('#', 1)[0]
                keys.add(k.replace('\\n', '\n').replace('\\r', '\r'))
    return keys


def unlps(v):
    for a, b in (('/stop', ':|'), ('/equ', '='), ('/tab', '\t'), ('/n', '\n'), ('/r', '\r'),
                 ('/id', '#'), ('/com', ','), ('/|', '|'), ('/!', '/')):
        v = v.replace(a, b)
    return v


def used_keys():
    used = {}
    for base, _, files in os.walk(ROOT):
        if any(x in base for x in ('\\obj', '\\bin', '\\.git', '\\docs', '\\tools')):
            continue
        for f in files:
            p = os.path.join(base, f)
            rel = os.path.relpath(p, ROOT)
            if f.endswith('.cs'):
                s = open(p, encoding='utf-8-sig', errors='replace').read()
                for m in list(CS_RE.finditer(s)) + list(CS_RE2.finditer(s)):
                    try:
                        k = unescape_cs(m.group(1))
                    except Exception:
                        k = m.group(1)
                    used.setdefault(k, rel)
            elif f.endswith('.xaml'):
                s = open(p, encoding='utf-8-sig', errors='replace').read()
                for m in XAML_RE.finditer(s):
                    k = xaml_unescape((m.group(1) if m.group(1) is not None else m.group(2)).strip())
                    used.setdefault(k, rel)
            elif f.endswith('.lps') and rel.startswith(os.path.join('VPet-Simulator.Windows', 'mod')) \
                    and os.sep + 'lang' + os.sep not in rel and f != 'info.lps':
                s = open(p, encoding='utf-8-sig', errors='replace').read()
                for sub in s.replace('\r', '').replace('\n', ':|').split(':|'):
                    if '#' not in sub:
                        continue
                    n, v = sub.split('#', 1)
                    if n.strip() in DATA_FIELDS and v:
                        vals = unlps(v).split(',') if n.strip() == 'tags' else [unlps(v)]
                        for k in vals:
                            used.setdefault(k.strip() if n.strip() == 'tags' else k, rel)
    return {k: v for k, v in used.items() if CJK.search(k)}


def main():
    culture = sys.argv[1] if len(sys.argv) > 1 else 'fr'
    have = lang_keys(culture)
    used = used_keys()
    missing = sorted((v, k) for k, v in used.items() if k not in have and k.replace('\r\n', '\r') not in have)
    out = None
    if '--write' in sys.argv:
        out = open(sys.argv[sys.argv.index('--write') + 1], 'w', encoding='utf-8')
    for rel, k in missing:
        line = f'{rel}\t{k!r}'
        if out:
            out.write(line + '\n')
    print(f'{len(used)} translatable keys used, {len(missing)} missing in lang/{culture}')
    by_file = {}
    for rel, _ in missing:
        by_file[rel] = by_file.get(rel, 0) + 1
    for rel, n in sorted(by_file.items(), key=lambda x: -x[1])[:25]:
        print(f'  {n:4d}  {rel}')


if __name__ == '__main__':
    main()
