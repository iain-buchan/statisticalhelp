"""Check bibliography DOI identities against authoritative Crossref metadata.

Title similarity is a review aid, never an automatic decision to edit a citation.
"""
import concurrent.futures
import datetime
import difflib
import html
import json
import pathlib
import re
import threading
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
INPUT = ROOT / 'evidence/reference-current-input.json'
OUTPUT = ROOT / 'evidence/all-reference-doi-metadata.json'
GATE = threading.Lock()
LAST_REQUEST = 0.0


def normalized(value):
    value = re.sub(r'<[^>]+>', ' ', html.unescape(value))
    value = ''.join(c for c in unicodedata.normalize('NFKD', value) if not unicodedata.combining(c))
    return ' '.join(re.findall(r'[a-z0-9]+', value.lower()))


def get(doi):
    global LAST_REQUEST
    url = 'https://api.crossref.org/works/' + urllib.parse.quote(doi, safe='')
    for attempt in range(4):
        with GATE:
            time.sleep(max(0, 0.42 - (time.monotonic() - LAST_REQUEST)))
            LAST_REQUEST = time.monotonic()
        try:
            req = urllib.request.Request(url, headers={'Accept': 'application/json', 'User-Agent': 'StatsDirectHelpReferenceReview/0.1 (https://www.statsdirect.com/)'})
            with urllib.request.urlopen(req, timeout=22) as response:
                message = json.load(response)['message']
            keys = ['DOI', 'title', 'subtitle', 'author', 'editor', 'type', 'publisher', 'container-title', 'volume', 'issue', 'page', 'article-number', 'edition-number', 'ISBN', 'ISSN', 'issued', 'published-print', 'published-online', 'URL', 'resource']
            return {'requestedDoi': doi, 'metadataUrl': url, 'status': 200, 'metadata': {k: message[k] for k in keys if k in message}}
        except urllib.error.HTTPError as error:
            if error.code in (429, 500, 502, 503, 504) and attempt < 3:
                time.sleep(2 * (attempt + 1))
                continue
            return {'requestedDoi': doi, 'metadataUrl': url, 'status': error.code, 'error': str(error)}
        except Exception as error:
            if attempt < 2:
                time.sleep(attempt + 1)
                continue
            return {'requestedDoi': doi, 'metadataUrl': url, 'status': None, 'error': str(error)}


def main():
    references = json.loads(INPUT.read_text(encoding='utf-8-sig'))
    doi_refs = {}
    for reference in references:
        for url in reference['links']:
            parts = urllib.parse.urlsplit(url)
            if parts.hostname == 'doi.org':
                doi = urllib.parse.unquote(parts.path.lstrip('/')).lower()
                doi_refs.setdefault(doi, []).append(reference)
    previous = json.loads(OUTPUT.read_text(encoding='utf-8')) if OUTPUT.exists() else {}
    cache = {row['requestedDoi']: row for row in previous.get('results', []) if row.get('status') == 200}
    remaining = [doi for doi in doi_refs if doi not in cache]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:
        for result in executor.map(get, remaining):
            cache[result['requestedDoi']] = result
            if len(cache) % 30 == 0:
                print(f'Fetched metadata for {len(cache)}/{len(doi_refs)} unique DOIs', flush=True)
                OUTPUT.write_text(json.dumps({'results': list(cache.values())}, indent=2, ensure_ascii=False), encoding='utf-8')
    rows = []
    for doi, refs in doi_refs.items():
        row = dict(cache[doi])
        row['references'] = []
        for reference in refs:
            title = ' '.join(row.get('metadata', {}).get('title', []))
            citation = reference['citation']
            # Look for the best similar window in the full citation, so author
            # initials, journal names and URL labels do not dilute a title match.
            words, target = normalized(citation).split(), normalized(title)
            n = len(target.split())
            scores = [difflib.SequenceMatcher(None, target, ' '.join(words[i:i + length])).ratio()
                      for length in range(max(1, n - 2), n + 3)
                      for i in range(max(1, len(words) - length + 1))] if target else [0]
            row['references'].append({'referenceId': reference['id'], 'citation': citation, 'sourceUrl': next(url for url in reference['links'] if 'doi.org/' in url), 'titleSimilarity': round(max(scores), 4)})
        rows.append(row)
    report = {'checkedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'uniqueDois': len(rows), 'referenceEntries': len({r['referenceId'] for row in rows for r in row['references']}),
              'retrieved': sum(row['status'] == 200 for row in rows), 'results': rows}
    OUTPUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'results'}, indent=2), flush=True)
    for row in rows:
        for ref in row['references']:
            if row['status'] != 200 or ref['titleSimilarity'] < 0.82:
                print(json.dumps({'id': ref['referenceId'], 'status': row['status'], 'score': ref['titleSimilarity'], 'cited': ref['citation'], 'metadata': row.get('metadata')}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
