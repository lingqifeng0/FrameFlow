"""Exercise real navigation commands, WinForms buttons, rendered OSD and expiry."""
from pathlib import Path
import time
import numpy as np
from PIL import Image
from native_harness import PlayerTest, ROOT, USER32, wait


def feedback(p, action=None, timeout=8):
    def visible():
        s = p.state('navigation')
        return s if s.get('visible') and (action is None or s.get('action') == action) else None
    return wait(visible, 'navigation feedback ' + str(action), timeout)


def positioned(p, seconds):
    p.request(['seek', seconds, 'absolute+exact'])
    wait(lambda: not p.get('seeking') and abs(p.get('time-pos') - seconds) < .04, 'actual seek completed')
    wait(lambda: not p.state('navigation').get('visible'), 'previous prompt expired', 5)


def capture(p, name):
    # The IPC acknowledgement precedes the next GPU repaint, especially paused.
    time.sleep(.12)
    path = ROOT / 'test-results' / (name + '.png')
    p.request(['screenshot-to-file', str(path), 'window'])
    return np.asarray(Image.open(path).convert('RGB')).astype(int)


p = PlayerTest('feedback')
try:
    p.start()
    p.check(not p.state('navigation').get('visible'), 'opening a file does not create a seek prompt')
    positioned(p, 10)
    main = p.main()
    buttons = {w['text']: w['hwnd'] for w in p.windows(main) if 'BUTTON' in w['class'].upper()}
    p.send(buttons['下一帧'], 0xf5)
    s = feedback(p, '下一帧')
    p.check(abs(s['time'] - p.get('time-pos')) < .001 and abs(s['time'] - 10 - 1 / 30) < .001,
            'next-frame button reports the decoded frame, not the previous time', s)
    p.check(s['timestamp'].endswith('.033') and ':' in s['timestamp'] and '%' in s['text'], 'feedback includes milliseconds and percentage')
    began = time.monotonic()
    shown = capture(p, 'feedback-visible')
    time.sleep(max(0, 2.4 - (time.monotonic() - began)))
    p.check(p.state('navigation')['visible'], 'prompt remains visible before 3 seconds')
    wait(lambda: not p.state('navigation')['visible'], 'prompt hides automatically', 2)
    elapsed = time.monotonic() - began
    p.check(2.7 < elapsed < 3.35, 'prompt expires at approximately 3 seconds', round(elapsed, 3))
    hidden = capture(p, 'feedback-hidden')
    h, w = shown.shape[:2]
    diff = np.abs(shown - hidden).max(axis=2)
    p.check(np.count_nonzero(diff[:h // 3, :w * 3 // 4] > 25) > 300 and np.count_nonzero(diff[h // 2:] > 25) < 20,
            'OSD is actually rendered at top left and removed without changing the paused frame')

    p.send(buttons['上一帧'], 0xf5)
    s = feedback(p, '上一帧')
    p.check(abs(s['time'] - 10) < .001 and p.get('pause'), 'previous-frame button reports actual time and stays paused', s)
    p.tool('seek-forward', 5)
    s = feedback(p, '步进 5 秒')
    p.check(abs(s['time'] - 15) < .04, 'forward seek replaces the previous frame prompt', s)
    time.sleep(2)
    p.tool('seek-back', 2.5)
    s = feedback(p, '步退 2.5 秒')
    p.check(abs(s['time'] - 12.5) < .04, 'custom backward step reports its actual result', s)
    time.sleep(1.2)
    p.check(p.state('navigation')['visible'], 'a new operation restarts the 3-second timer')
    p.tool('frame-forward')
    feedback(p, '下一帧')
    p.tool('frame-back')
    s = feedback(p, '上一帧')
    p.check(abs(s['time'] - 12.5) < .04, 'successive frame operations keep only the latest result')

    seek = next(w['hwnd'] for w in p.windows(main) if w['class'].startswith('WindowsForms10.Window')
                and USER32.GetParent(USER32.GetParent(USER32.GetParent(w['hwnd']))) == main
                and (r := p.rect(w['hwnd'])).right - r.left > 400 and r.bottom - r.top <= 35)
    r = p.rect(seek)
    lp = ((r.right - r.left) // 2) | (((r.bottom - r.top) // 2) << 16)
    p.send(seek, 0x201, 1, lp)
    p.send(seek, 0x202, 0, lp)
    s = feedback(p, '定位')
    p.check(abs(s['time'] - 30) < .15 and abs(s['time'] - p.get('time-pos')) < .001, 'timeline click reports actual destination', s)

    p.request(['set_property', 'fullscreen', True])
    wait(lambda: p.state('window').get('mode') == 'fullscreen', 'fullscreen ready')
    p.tool('frame-forward')
    feedback(p, '下一帧')
    shown = capture(p, 'feedback-fullscreen')
    p.check(shown.shape[0] > hidden.shape[0], 'fullscreen uses the same on-video overlay')
    p.request(['set_property', 'fullscreen', False])

    positioned(p, 20)
    p.request(['frame-step'])
    s = feedback(p, '下一帧')
    p.check(abs(s['time'] - 20 - 1 / 30) < .001, 'native frame command also displays feedback')
    p.request(['frame-back-step'])
    s = feedback(p, '上一帧')
    p.check(abs(s['time'] - 20) < .001, 'native backward frame command displays feedback')
    p.request(['keypress', '.'])
    s = feedback(p, '下一帧')
    p.check(abs(s['time'] - 20 - 1 / 30) < .001, 'default frame keyboard binding also displays feedback')
    p.request(['seek', 5, 'relative'])
    s = feedback(p, '定位')
    p.check(abs(s['time'] - p.get('time-pos')) < .001, 'native keyframe seek reports decoded time instead of assuming requested time', s)

    # A drag reports the release position, rather than leaving an intermediate
    # timestamp in the overlay while the paused video finishes its exact seek.
    r = p.rect(seek)
    width = r.right - r.left
    x = round((width - 12) * .7 + 6)
    p.send(seek, 0x201, 1, 40 | (12 << 16))
    p.send(seek, 0x200, 1, 120 | (12 << 16))
    p.send(seek, 0x202, 0, x | (12 << 16))
    s = wait(lambda: s if (s := p.state('navigation')).get('action') == '定位' and abs(s.get('time', 0) - 42) < .15 else None, 'drag feedback uses final position')
    p.check(abs(s['time'] - p.get('time-pos')) < .001, 'timeline drag reports its final release position', s)

    positioned(p, 0)
    p.tool('frame-back')
    s = feedback(p, '上一帧')
    p.check(s['time'] < .001 and '已到开头' in s['text'], 'previous frame at start gives boundary feedback', s)
    wait(lambda: not p.state('navigation')['visible'], 'boundary prompt expires', 5)
    p.request(['set_property', 'pause', False])
    s = feedback(p, 'pause')
    p.check(s['kind'] == 'operation' and s['text'] == '播放', 'resuming playback gives one common-action prompt')
    wait(lambda: not p.state('navigation')['visible'], 'playback prompt expires', 4)
    time.sleep(.5)
    p.check(not p.state('navigation')['visible'], 'ordinary playback does not repeatedly create navigation prompts')
    p.save('native-feedback.json')
finally:
    p.close()

p = PlayerTest('feedback-off', {'ShowNavigationFeedback': False})
try:
    p.start()
    p.tool('seek-forward', 5)
    wait(lambda: p.get('time-pos') > 4.9, 'disabled-feedback seek still works')
    p.tool('frame-forward')
    time.sleep(.3)
    p.check(not p.state('navigation').get('visible'), 'disabling feedback keeps navigation working without overlays')
    p.close()
    p.start()
    p.check(not p.state('navigation').get('visible'), 'disabled preference survives restart')
    p.save('native-feedback-off.json')
finally:
    p.close()
