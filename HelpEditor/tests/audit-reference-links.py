"""Read-only external-link audit; blocked/time-out responses are not dead links."""
import concurrent.futures
import datetime
import json
import pathlib
import re
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'HelpSource/Content/references/reference_list.htm'
OUTPUT = ROOT / 'evidence/reference-link-audit.json'
HEADERS = {'User-Agent': 'StatsDirectHelpReferenceCheck/0.1 (bibliographic link validation)',
           'Accept': 'text/html,application/json,application/pdf;q=0.8,*/*;q=0.5'}


def request(url):
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=14) as response:
            content = response.read(65536)
            return {'status': response.status, 'url': response.url,
                    'type': response.headers.get('Content-Type', ''),
                    'text': content.decode('utf-8', errors='replace')}
    except urllib.error.HTTPError as error:
        return {'status': error.code, 'url': error.url, 'text': error.read(8192).decode('utf-8', errors='replace')}
    except Exception as error:
        return {'status': None, 'url': url, 'error': str(error), 'text': ''}


def audit(item):
    url, citations = item
    response = request(url)
    status, text = response['status'], response.pop('text')
    title = re.search(r'<title[^>]*>(.*?)</title>', text, re.I | re.S)
    row = {'sourceUrl': url, 'references': citations, **response,
           'title': re.sub(r'\s+', ' ', title.group(1)).strip() if title else None}
    if row['title'] and re.search(r'client challenge|checking your browser|just a moment|captcha|access denied|robot check|human verification|attention required', row['title'], re.I):
        row['classification'] = 'unverified-blocked-or-unreachable'
    elif status in (404, 410) or 'DOI Not Found' in text:
        row['classification'] = 'confirmed-http-error'
    elif status and 200 <= status < 400:
        row['classification'] = 'reachable'
    else:
        row['classification'] = 'unverified-blocked-or-unreachable'
    if urllib.parse.urlsplit(url).hostname == 'doi.org':
        # SICI-era DOIs contain escaped angle brackets. Decode the existing URL
        # before quoting it for the Handle API, rather than turning %3C into
        # %253C and falsely reporting a missing DOI.
        doi = urllib.parse.unquote(urllib.parse.urlsplit(url).path.lstrip('/'))
        handle = request('https://doi.org/api/handles/' + urllib.parse.quote(doi, safe='/') + '?type=URL')
        try:
            registry = json.loads(handle['text'])
            row['doiRegistryCode'] = registry.get('responseCode')
            row['doiRegisteredUrls'] = [entry.get('data', {}).get('value') for entry in registry.get('values', []) if entry.get('type') == 'URL']
            if row['doiRegistryCode'] == 100:
                row['classification'] = 'doi-not-registered'
        except (ValueError, TypeError):
            row['doiRegistryCheck'] = {'status': handle['status'], 'error': handle.get('error', 'Non-JSON response')}
    return row


def main():
    references = []
    for paragraph in ET.parse(SOURCE).getroot().iter('p'):
        anchors = list(paragraph.iter('a'))
        urls = [anchor.get('href') for anchor in anchors if (anchor.get('href') or '').startswith(('https://', 'http://'))]
        if urls:
            references.append({'id': next((anchor.get('id') for anchor in anchors if anchor.get('id')), None),
                               'citation': ''.join(paragraph.itertext()), 'links': urls})
    links = {}
    for reference in references:
        for url in reference['links']:
            links.setdefault(url, []).append({'id': reference['id'], 'citation': reference['citation']})
    rows = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        for row in executor.map(audit, links.items()):
            rows.append(row)
            if len(rows) % 40 == 0:
                print(f'Checked {len(rows)} / {len(links)} links', flush=True)
    report = {'checkedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'uniqueUrls': len(rows), 'referenceEntries': len(references),
              'counts': {classification: sum(r['classification'] == classification for r in rows)
                         for classification in sorted({r['classification'] for r in rows})}, 'results': rows}
    OUTPUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'results'}, indent=2), flush=True)


if __name__ == '__main__':
    main()
