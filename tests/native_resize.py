"""Window state/persistence through the player's API.

Mouse resizing must additionally be verified with actual mouse input, not
SetWindowPos or synthetic WM_NCHITTEST queries. See window-drag-v11.json.
"""
import json
from pipe_probe import ROOT, Probe

reports = []
p = Probe('window-api-v11', settings={'RememberPosition': False, 'Width': 1250, 'Height': 900, 'WindowDpi': 168,
                                     'Toolbar': [{'Id': 'window-reset', 'Title': '还原初始窗口大小'}]})


def window(): return json.loads(p.get('user-data/modern/window') or '{}')
def passed(condition, title, data=None):
    assert condition, (title, data)
    reports.append({'check': title, 'result': 'passed', 'data': data})
    print('PASS:', title, data if data is not None else '', flush=True)
def mode(action, expected):
    p.request(['script-message-to', 'mpvnet', 'modern-window', action])
    p.wait(lambda: window().get('mode') == expected, action)
    return window()
def same_size(a, b): return (a['width'], a['height']) == (b['width'], b['height'])


try:
    p.wait(lambda: window().get('mode') == 'normal', 'initial window state')
    s = window()
    normal = s['bounds']
    passed(s['resizeGestures'] == 0, 'API state tests do not impersonate actual mouse drags')
    passed(abs(s['initialBounds']['width']/s['workArea']['width']-.75) < .02 and
           abs(s['initialBounds']['height']/s['workArea']['height']-.85) < .02, 'initial size occupies 75% width and 85% height')
    s = mode('maximize', 'maximized')
    passed(s['bounds'] == s['workArea'], 'maximized custom window fills the work area', s)
    passed(same_size(s['normalBounds'], normal), 'maximizing retains the saved normal size')
    s = mode('restore', 'normal')
    passed(same_size(s['bounds'], normal), 'restoring from maximized recovers the saved normal size')
    s = mode('fullscreen', 'fullscreen')
    passed(s['bounds'] == s['screenBounds'], 'fullscreen fills the entire monitor', s)
    passed(same_size(s['normalBounds'], normal), 'fullscreen retains normal dimensions')
    s = mode('exit-fullscreen', 'normal')
    passed(same_size(s['bounds'], normal), 'exiting fullscreen restores normal dimensions')
    mode('maximize', 'maximized')
    mode('fullscreen', 'fullscreen')
    s = mode('exit-fullscreen', 'maximized')
    passed(s['bounds'] == s['workArea'], 'fullscreen entered from maximized restores maximized mode')
    passed(same_size(s['normalBounds'], normal), 'maximized/fullscreen transitions preserve the saved normal size')
    mode('fullscreen', 'fullscreen')
    p.close()
    saved = json.loads((p.config/'modern-ui.json').read_text(encoding='utf8'))
    passed((saved['Width'], saved['Height']) == (normal['width'], normal['height']), 'quitting fullscreen saves normal dimensions')
    p = Probe('window-api-reopen-v11', config=p.config)
    p.wait(lambda: window().get('mode') == 'normal', 'reopen normal')
    passed(same_size(window()['bounds'], normal), 'reopening restores the saved width and height')
    mode('maximize', 'maximized')
    p.request(['script-message-to', 'mpvnet', 'modern-tool', 'window-reset'])
    p.wait(lambda: window().get('mode') == 'normal' and window()['bounds'] == window()['initialBounds'], 'reset from maximized')
    passed(True, 'reset command leaves maximized mode and restores the centered initial size')
    mode('fullscreen', 'fullscreen')
    p.request(['script-message-to', 'mpvnet', 'modern-tool', 'window-reset'])
    p.wait(lambda: window().get('mode') == 'normal' and window()['bounds'] == window()['initialBounds'], 'reset from fullscreen')
    passed(True, 'reset command exits fullscreen and restores the initial size')
    p.close()
    p = Probe('window-api-reset-reopen-v11', config=p.config)
    p.wait(lambda: window().get('mode') == 'normal', 'reset reopen')
    passed(window()['bounds'] == window()['initialBounds'], 'reset dimensions and centered position survive a restart')
    (ROOT/'test-results/native-resize-v11.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf8')
    print('All',len(reports),'window API checks passed.',flush=True)
finally:
    p.close()
