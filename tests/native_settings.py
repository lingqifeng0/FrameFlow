"""First-open measurement and real WPF search/edit/save with lazy controls."""
import json
import subprocess
import time
from native_harness import PlayerTest, ROOT, USER32, wait

p = PlayerTest('settings-v9', config_text='# Preserve this comment\ndemuxer-max-bytes=128MiB\nsaturation=7\ndemuxer-cache-wait=no\n[custom-test]\ncontrast=11\n# profile comment\n')
original = (p.config / 'mpv.conf').read_bytes()
times = []


def search(text):
    p.request(['script-message-to', 'mpvnet', 'show-conf-editor', text])
    return wait(lambda: (s if (s := p.state('settings')).get('ready') and
        (s.get('category') == text[:-1] and s.get('search') == '' if text.endswith(':') else s.get('search') == text) else None), 'search ' + text)


def close_settings():
    hwnd = next(w['hwnd'] for w in p.windows() if w['text'] == '设置')
    p.send(hwnd, 0x10)
    wait(lambda: not p.state('settings').get('visible'), 'settings closed')


try:
    p.start()
    for i in range(2):
        began = time.perf_counter()
        p.tool('settings')
        hwnd = wait(lambda: next((w['hwnd'] for w in p.windows() if w['text'] == '设置'), None), 'settings shown')
        p.send(hwnd, 0)
        elapsed = round((time.perf_counter() - began) * 1000, 1)
        s = wait(lambda: (s if (s := p.state('settings')).get('ready') else None), 'first content rendered')
        times.append({'responsiveMilliseconds': elapsed, 'contentMilliseconds': s['openMilliseconds'],
                      'createdControls': s['createdControls'], 'totalSettings': s['totalSettings']})
        p.check(s['createdControls'] < s['totalSettings'] / 4, 'settings open constructs only the visible category, round ' + str(i + 1), times[-1])
        close_settings()
    p.check((p.config / 'mpv.conf').read_bytes() == original, 'opening/closing without edits preserves config byte for byte')
    baseline = ROOT / 'test-results/settings-benchmark-v8.json'
    if baseline.exists():
        old = json.loads(baseline.read_text())['milliseconds']
        print('SETTINGS BEFORE/AFTER:', json.dumps({'v8Milliseconds': old, 'v9': times}), flush=True)

    main = p.main()
    gear = next(w['hwnd'] for w in p.windows(main) if w['text'] == '设置')
    USER32.PostMessageW(gear, 0xf5, 0, 0)
    wait(lambda: p.state('settings').get('ready'), 'gear button opens settings')
    p.check(True, 'actual gear button opens the same Chinese settings window')
    s = search('demuxer-max-bytes')
    p.check([v['name'] for v in s['visibleSettings']] == ['demuxer-max-bytes'] and s['visibleSettings'][0]['value'] == '128MiB',
            'searching an unvisited category creates its control with the original saved value', s)
    hwnd = next(w['hwnd'] for w in p.windows() if w['text'] == '设置')
    subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'tests/settings-uia.ps1'), '-TestWindow', str(hwnd), '-Value', '96MiB'], check=True, timeout=15)
    search('常规:')
    s = search('demuxer-max-bytes')
    count = s['createdControls']
    search('常规:')
    s = search('demuxer-max-bytes')
    p.check(s['createdControls'] == count and s['visibleSettings'][0]['value'] == '96MiB',
            'switching categories reuses the control and retains an unsaved edit', s)
    s = search('解复用:')
    p.check(any(v['name'] == 'demuxer-max-bytes' for v in s['visibleSettings']), 'Chinese category navigation still includes the searched setting')
    s = search('缓存')
    p.check(len(s['visibleSettings']) > 0 and all(v['title'] for v in s['visibleSettings']), 'Chinese title/help search works without building all controls')
    close_settings()
    content = (p.config / 'mpv.conf').read_text(encoding='utf-8-sig')
    p.check('demuxer-max-bytes=96MiB' in content and p.get('demuxer-max-bytes') == 96 * 1024 * 1024,
            'a lazily created control saves and applies its edited native value')
    p.check('saturation=7' in content and 'demuxer-cache-wait=no' in content and '[custom-test]' in content and
            'contrast=11' in content and '# Preserve this comment' in content and '# profile comment' in content,
            'saving an edit preserves unvisited settings, unknown editor options, comments and profiles')
    s = search('demuxer-max-bytes')
    p.check(s['visibleSettings'][0]['value'] == '96MiB', 'edited value reloads correctly in a new settings window')
    close_settings()
    search('常规:')
    hwnd = next(w['hwnd'] for w in p.windows() if w['text'] == '设置')
    p.tool('frame-forward')
    wait(lambda: p.state('navigation').get('visible'), 'feedback currently enabled')
    subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'tests/settings-uia.ps1'), '-TestWindow', str(hwnd),
                    '-Checkbox', '逐帧和跳转时，在左上角显示位置提示（3 秒）'], check=True, timeout=15)
    p.check(not p.state('navigation')['visible'], 'new feedback checkbox clears an existing overlay immediately')
    close_settings()
    p.tool('seek-forward', 5)
    time.sleep(.3)
    p.check(not p.state('navigation')['visible'] and json.loads((p.config / 'modern-ui.json').read_text())['ShowNavigationFeedback'] is False,
            'feedback preference is saved and affects subsequent seeks')
    p.save('native-settings.json')
    (ROOT / 'test-results/settings-benchmark-v10.json').write_text(json.dumps(times, indent=2), encoding='utf8')
finally:
    p.close()
