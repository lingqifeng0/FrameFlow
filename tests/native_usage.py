"""Actual volume popup, common-action OSD, local folder queue and playback modes."""
import ctypes as C
from ctypes import wintypes as W
import json
import shutil
import subprocess
import time
import numpy as np
from PIL import Image
from native_harness import PlayerTest, USER32, ROOT, FIXTURE, wait

folder = ROOT / 'test-results/local-folder-v10'
folder.mkdir(exist_ok=True)
for name in ['01-video.mp4', '02-video.mp4', '10-video.mkv']:
    shutil.copyfile(FIXTURE, folder / name)
(folder / 'ignore.txt').write_text('not a video')
(folder / 'ignore.mp3').write_text('not a video')
(folder / 'nested').mkdir(exist_ok=True)
shutil.copyfile(FIXTURE, folder / 'nested/03-not-direct.mp4')
p = PlayerTest('usage-v10', settings={'AutoFolderPlaylist': True, 'PlaylistVisible': True}, fixture=folder / '02-video.mp4')


def overlay(action):
    return wait(lambda: (s if (s := p.state('navigation')).get('visible') and s.get('action') == action else None), 'operation OSD ' + action)


def screenshot(name):
    time.sleep(.12)
    path = ROOT / 'test-results' / (name + '.png')
    p.request(['screenshot-to-file', str(path), 'window'])
    return np.asarray(Image.open(path).convert('RGB')).astype(int)


def set_mode(mode):
    p.request(['script-message-to', 'mpvnet', 'modern-playlist', 'mode', mode])
    return wait(lambda: (s if (s := p.state('playlist')).get('mode') == mode else None), 'playlist mode ' + mode)


try:
    p.start()
    s = wait(lambda: (s if (s := p.state('playlist')).get('count') == 3 else None), 'auto folder populated')
    p.check([PathItem['title'] for PathItem in s['items']] == ['01-video.mp4', '02-video.mp4', '10-video.mkv'],
            'current directory videos populate in natural filename order, excluding audio/text/subdirectories', s)
    p.check(s['current'] == 1 and p.get('path') == str(folder / '02-video.mp4'), 'folder discovery retains the opened file and its playlist position')
    p.check(p.get('pause'), 'folder discovery does not restart playback or change pause')
    commands = {entry['name'] for entry in p.get('command-list')}
    p.check('playlist-unshuffle' in commands, 'the bundled mpv supports restoring playlist order after random playback')
    main = p.main()
    mode_button = next(w['hwnd'] for w in p.windows(main) if w['text'] == '顺序播放 ▾')
    p.send(mode_button, 0xf5)
    menus = [w for w in p.windows() if w['hwnd'] != main and w['class'].startswith('WindowsForms10.Window')]
    print('OWN MODE MENUS:', json.dumps(menus, ensure_ascii=True), flush=True)
    invoked = False
    for menu in menus:
        result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'tests/settings-uia.ps1'),
            '-TestWindow', str(menu['hwnd']), '-InvokeName', '单个循环'], capture_output=True, timeout=15)
        if result.returncode == 0: invoked = True; break
        print('MENU TEST:', result.stdout.decode('utf-8-sig', errors='replace'), result.stderr.decode('utf-8-sig', errors='replace'), flush=True)
    wait(lambda: p.state('playlist')['mode'] == 'single-loop', 'actual playback-mode menu item')
    p.check(invoked and p.get('loop-file') == 'inf' and p.get('loop-playlist') in ('no', False, 0), 'actual sidebar mode menu enables single-video looping')
    set_mode('list-loop')
    p.check(p.get('loop-file') in ('no', False, 0) and p.get('loop-playlist') == 'inf', 'list loop repeats the queue without trapping the current video')
    before = {entry['path'] for entry in p.state('playlist')['items']}
    set_mode('random')
    p.check(p.get('loop-playlist') == 'inf' and {entry['path'] for entry in p.state('playlist')['items']} == before,
            'random playback preserves all videos and enables cycling through the shuffled queue')
    set_mode('sequence')
    p.check(p.get('loop-file') in ('no', False, 0) and p.get('loop-playlist') in ('no', False, 0) and p.state('playlist')['items'][0]['title'] == '01-video.mp4',
            'sequential playback removes both loops and restores natural order')
    p.request(['playlist-next'])
    wait(lambda: p.get('path') == str(folder / '10-video.mkv'), 'next directory video')
    p.check(p.state('playlist')['count'] == 3, 'switching to the next local video preserves the folder queue')

    volume_button = next(w['hwnd'] for w in p.windows(main) if w['text'] == '音量')
    p.send(volume_button, 0xf5)
    volume = wait(lambda: (s if (s := p.state('volume')).get('visible') else None), 'volume popover')
    p.check(not any(w['text'] == '音量' for w in p.windows()), 'volume button opens no separate modal dialog')
    button_rect = p.rect(volume_button)
    p.check(volume['bounds']['y'] + volume['bounds']['height'] <= button_rect.top + 3, 'slim volume popover is anchored above the toolbar button', volume)
    popup = next(w['hwnd'] for w in p.windows() if w['class'].startswith('WindowsForms10.Window') and w['hwnd'] != main)
    slider = next(w['hwnd'] for w in p.windows(popup) if w['class'].startswith('WindowsForms10.Window') and
        50 < p.rect(w['hwnd']).right-p.rect(w['hwnd']).left < 120 and p.rect(w['hwnd']).bottom-p.rect(w['hwnd']).top > 200)
    rect = p.rect(slider)
    lp = ((rect.right-rect.left)//2) | (((rect.bottom-rect.top)//2) << 16)
    p.send(slider, 0x201, 1, lp); p.send(slider, 0x202, 0, lp)
    wait(lambda: abs(p.get('volume') - 50) < 1, 'actual slider midpoint volume')
    p.check(True, 'clicking the actual vertical slider adjusts volume to 50%')
    s = overlay('volume')
    p.check('50%' in s['text'] and s['durationSeconds'] == 3, 'slider feedback uses the same top-left 3-second OSD')
    p.tool('mute')
    overlay('mute')
    p.check(p.state('volume')['muted'], 'mute state is synchronized while the popover remains open')
    p.tool('mute')
    p.send(volume_button, 0xf5)
    wait(lambda: not p.state('volume')['visible'], 'close popover')
    p.check(True, 'clicking the button again closes the lightweight volume popover')
    native = next(w['hwnd'] for w in p.windows(main) if w['class'] == 'mpv')
    surface = USER32.GetParent(native)
    rect = p.rect(surface)
    lp = ((rect.left+rect.right)//2 & 65535) | (((rect.top+rect.bottom)//2 & 65535) << 16)
    volume_before = p.get('volume')
    p.send(surface, 0x20a, 120 << 16, lp)
    s = overlay('volume')
    p.check(p.get('volume') > volume_before and '%' in s['text'] and p.get('osd-bar') is False,
            'real wheel input changes volume and uses percentage text without the central native bar', s)
    began = time.monotonic()
    visible = screenshot('volume-osd-visible-v10')
    wait(lambda: not p.state('navigation')['visible'], 'operation OSD expires', 4)
    elapsed = time.monotonic()-began
    hidden = screenshot('volume-osd-hidden-v10')
    diff = np.abs(visible-hidden).max(axis=2)
    h, w = diff.shape
    p.check(2.7 < elapsed < 3.4 and np.count_nonzero(diff[:h//3, :w*3//4] > 25) > 100 and
            np.count_nonzero(diff[h//2:] > 25) < 30, 'volume OSD is actually drawn at the top left and disappears after 3 seconds')
    p.tool('speed', 1.34)
    p.check('1.34' in overlay('speed')['text'], 'speed changes share the same OSD')
    p.tool('sub-toggle')
    p.check('字幕' in overlay('sub-visibility')['text'], 'subtitle toggles share the same OSD')
    p.tool('brightness-up')
    p.check('亮度' in overlay('brightness')['text'], 'picture adjustments share the same OSD')
    set_mode('single-loop')
    p.close()
    p.start()
    p.check(wait(lambda: p.state('playlist').get('mode') == 'single-loop', 'saved mode') and p.get('loop-file') == 'inf',
            'chosen playback mode survives reopening')
    p.save('native-usage.json')
    print('All', len(p.reports), 'checks passed.', flush=True)
finally:
    p.close()
