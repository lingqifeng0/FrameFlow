"""Real WPF global search, one candidate per view, and PDF-style navigation."""
import json
import subprocess
from native_harness import PlayerTest, ROOT, wait

p = PlayerTest('search-v10')


def uia(hwnd, *args):
    result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        str(ROOT / 'tests/settings-uia.ps1'), '-TestWindow', str(hwnd), *args], capture_output=True, timeout=15)
    if result.returncode: raise RuntimeError(result.stdout.decode('utf-8-sig', errors='replace') + result.stderr.decode('utf-8-sig', errors='replace'))
    return result.stdout.decode('utf-8-sig', errors='replace')


def ready(): return wait(lambda: (s if (s := p.state('settings')).get('ready') else None), 'settings ready')
def search(text):
    hwnd = next(w['hwnd'] for w in p.windows() if w['text'] == '设置')
    uia(hwnd, '-Search', text)
    return wait(lambda: (s if (s := p.state('settings')).get('search') == text else None), 'actual textbox search')


try:
    p.start()
    p.tool('settings')
    s = ready()
    hwnd = next(w['hwnd'] for w in p.windows() if w['text'] == '设置')
    p.check(s['category'] == '常规' and s['search'] == '', 'normal settings open starts in General with an empty global search box', s)
    tree = json.loads(uia(hwnd, '-ReadTree'))
    p.check(next(item['expanded'] for item in tree if item['name'] == '视频') == 'Collapsed', 'the actual Video tree branch starts collapsed', tree)
    s = search('缓存')
    total = s['results']['count']
    first = s['results']['current']
    p.check(total > 1 and s['results']['index'] == 1 and len(s['visibleSettings']) == 1,
            'global search finds multiple settings but shows one editable candidate', s)
    p.check(len({item['category'] for item in s['results']['matches']}) > 1, 'results span several categories, not only the selected category')
    uia(hwnd, '-InvokeId', 'NextSearchResult')
    s = wait(lambda: (s if (s := p.state('settings'))['results']['index'] == 2 else None), 'next result')
    p.check(s['results']['current'] != first and len(s['visibleSettings']) == 1, 'actual Next button replaces the displayed candidate')
    uia(hwnd, '-InvokeId', 'PreviousSearchResult')
    s = wait(lambda: (s if (s := p.state('settings'))['results']['index'] == 1 else None), 'previous result')
    p.check(s['results']['current'] == first, 'actual Previous button returns to the prior candidate')
    uia(hwnd, '-InvokeId', 'PreviousSearchResult')
    s = wait(lambda: (s if (s := p.state('settings'))['results']['index'] == total else None), 'wrap to last')
    p.check(True, 'Previous wraps from first to last and the counter reflects the last result')
    uia(hwnd, '-InvokeId', 'NextSearchResult')
    wait(lambda: p.state('settings')['results']['index'] == 1, 'wrap to first')
    p.check(True, 'Next wraps from last to first')
    s = search('断点')
    p.check(s['results']['current'] == 'modern.remember-position', 'the familiar term 断点 finds the playback-position preference first', s)
    uia(hwnd, '-Checkbox', '记住每个视频的播放位置')
    p.check(json.loads((p.config / 'modern-ui.json').read_text())['RememberPosition'], 'the individually displayed preference is editable and saves immediately')
    s = search('倍速')
    p.check(s['results']['count'] > 0 and any('speed' in item['name'] for item in s['results']['matches']), '倍速 finds speed-related settings')
    s = search('硬解')
    p.check(s['results']['current'] == 'hwdec', 'the hardware-decoding alias finds hwdec')
    s = search('demuxer-max-bytes')
    p.check(s['results']['count'] == 1 and s['visibleSettings'][0]['name'] == 'demuxer-max-bytes', 'exact parameter-name search shows its full editable control')
    s = search('视频 缩放')
    p.check(s['results']['count'] > 0, 'space-separated terms match together across metadata')
    s = search('zz-no-such-setting-zz')
    p.check(s['results']['count'] == 0 and len(s['visibleSettings']) == 0, 'zero results clear the previous candidate instead of showing stale settings')
    p.send(hwnd, 0x10)
    wait(lambda: not p.state('settings')['visible'], 'closed')
    p.tool('settings')
    s = ready()
    p.check(s['category'] == '常规' and s['search'] == '' and not next(item['expanded'] for item in s['categories'] if item['name'] == '视频'),
            'reopening resets to General and collapses Video regardless of the prior search/category')
    p.save('native-search.json')
    print('All', len(p.reports), 'checks passed.', flush=True)
finally:
    p.close()
