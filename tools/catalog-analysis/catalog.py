"""Implementación de referencia de las reglas del catálogo (ver docs/business-rules.md).

- categorize(): asigna la categoría según backend/Data/Seed/categories.json
- display_name(): genera el nombre para mostrar según backend/Data/Seed/display-dictionary.json

El backend en C# debe producir los mismos resultados. Este archivo sirve como
especificación ejecutable y para analizar exportaciones de INVE01.
"""
import json, re, unicodedata
from pathlib import Path

SEED = Path(__file__).resolve().parents[2] / 'backend' / 'Data' / 'Seed'


def normalize(text):
    t = unicodedata.normalize('NFD', (text or '').upper())
    return ''.join(c for c in t if unicodedata.category(c) != 'Mn')


def tokens(text):
    return [t for t in re.split(r'[^A-Z0-9]+', normalize(text)) if t]


# ---------------------------------------------------------------- categorías
class Categorizer:
    def __init__(self, path=SEED / 'categories.json'):
        data = json.loads(Path(path).read_text(encoding='utf-8'))
        self.categories = data['categories']
        self.index = {}
        for c in self.categories:
            for r in c['rules']:
                kw = tuple(tokens(r['keyword']))
                rule = (kw, r['priority'], len(''.join(kw)), c['position'], c['slug'], r['keyword'])
                self.index.setdefault(kw[0], []).append(rule)

    @staticmethod
    def _word(word, kw):
        return word == kw or word == kw + 'S' or word == kw + 'ES'

    def _matches(self, toks, kw):
        n, m = len(toks), len(kw)
        return any(all(self._word(toks[i + j], kw[j]) for j in range(m)) for i in range(n - m + 1))

    def categorize(self, description):
        """Devuelve (slug, palabra_clave) o ('otros', None)."""
        toks = tokens(description)
        cands = set()
        for t in toks:
            for key in (t, t[:-1] if t.endswith('S') else None, t[:-2] if t.endswith('ES') else None):
                if key:
                    cands.update(self.index.get(key, []))
        best = None
        for r in cands:
            if self._matches(toks, r[0]):
                k = (-r[1], -r[2], r[3])
                if best is None or k < best[0]:
                    best = (k, r)
        return (best[1][4], best[1][5]) if best else ('otros', None)


# ---------------------------------------------------------------- nombre para mostrar
class DisplayNamer:
    def __init__(self, path=SEED / 'display-dictionary.json'):
        d = json.loads(Path(path).read_text(encoding='utf-8'))
        self.abbr, self.accents, self.units = d['abbreviations'], d['accents'], d['units']
        self.acronyms, self.special = set(d['acronyms']), d['special']
        self.lower = set(d['lowerWords'])
        self.fem_words, self.fem_exc = set(d['feminineWords']), set(d['feminineExceptions'])
        num = r'\d+(?:[.,/]\d+)?'
        alt = '|'.join(sorted(self.units, key=len, reverse=True))
        self.num_unit = re.compile(rf'^({num})({alt})$')
        self.number = re.compile(rf'^{num}$')

    def _feminine(self, w):
        return w in self.fem_words or (w.endswith('A') and w not in self.fem_exc)

    def _word(self, core, fem, first):
        if core in self.abbr:
            exp = self.abbr[core]
            w = exp[1] if (fem and len(exp) > 1) else exp[0]
        elif core in self.special:
            return self.special[core]
        elif core in self.acronyms or re.search(r'\d', core):
            return core
        elif core in self.accents:
            w = self.accents[core]
        else:
            w = core.lower()
        if w in self.lower and not first:
            return w
        return w[:1].upper() + w[1:] if w[:1].islower() else w

    def __call__(self, descr):
        s = (descr or '').strip().upper().replace('_', ' ')
        s = re.sub(r'\bV/[CD]\b', ' ', s)
        s = re.sub(r'\bC/U\b', ' C/U ', s)
        s = re.sub(r'\bS/R\b', ' SIN ROSCA ', s)
        for pre, word in (('P', 'PARA'), ('C', 'CON'), ('S', 'SIN'), ('D', 'DE')):
            neg = r'(?!U\b)' if pre == 'C' else ''
            s = re.sub(rf'(?:(?<=^)|(?<=[\s/])){pre}/{neg}', f' {word} ', s)
        s = re.sub(r'\s*,\s*', ', ', s)
        words = [w for w in re.split(r'\s+', s) if w]
        fem = self._feminine(next((w for w in words if w.isalpha()), ''))
        out = []
        for w in words:
            core = w.strip('.,;:/')
            tail = ',' if w.endswith(',') else ''
            if not core:
                continue
            m = self.num_unit.match(core)
            if m:
                out += [m.group(1), self.units[m.group(2)] + tail]
                continue
            if core in self.units and out and self.number.match(out[-1]):
                out.append(self.units[core] + tail)
                continue
            parts = core.split('/') if re.fullmatch(r'[A-ZÑÁÉÍÓÚ]+(?:/[A-ZÑÁÉÍÓÚ]+)+', core) else [core]
            first = not out
            out.append('/'.join(self._word(p, fem, first and i == 0) for i, p in enumerate(parts)) + tail)
        return ' '.join(out).strip(' ,')
