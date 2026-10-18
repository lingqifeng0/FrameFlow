// Exercises the installed 0.2.1 plugin's real controller and identity helper.
// Uses copies, isolated configs and null audio/video output; no MarkText edits.
const fs = require('node:fs/promises')
const path = require('node:path')
const crypto = require('node:crypto')
const assert = require('node:assert/strict')
const { spawn } = require('node:child_process')
const base = path.resolve(__dirname, '../test-results/marktext-v11')
const root = path.resolve(__dirname, '../test-results/marktext-v12')
const { setup } = require(path.join(base, 'plugin/main.cjs'))
const { MpvClient, NativeHelper, parseLink } = require(path.join(base, 'probe-api.cjs'))
const player = path.join(root, 'player/FrameFlow.exe')
const config = path.join(root, 'player/portable_config')
const media = path.join(root, 'source/01-video.mp4')
const second = path.join(root, 'source/02-clip.mp4')
const note = path.join(root, 'source/notes/note.md')
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))
const children = new Set()
const reports = []
const events = []
const started = Date.now()
let settings = { playerPath: player, autoPlay: false, bringToFront: false }
let session = null
let observer = null
let controller = null
let native = null
let manual = null
let abort = null
const stamp = () => Date.now() - started
const link = (seconds, clip = false) => '../' + (clip ? '02-clip.mp4' : '01-video.mp4') + '#t=' + seconds.toFixed(3)

function launch(executable, args) {
  assert.equal(path.resolve(executable), player)
  const child = spawn(executable, args.concat(['--vo=null', '--ao=null', '--mute=yes', '--force-window=no']),
    { cwd: path.dirname(executable), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  children.add(child)
  child.stdout.resume()
  child.stderr.on('data', data => events.push({ ms: stamp(), stderr: String(data).slice(0, 2000) }))
  child.once('exit', () => children.delete(child))
  events.push({ ms: stamp(), pid: child.pid, args })
  return child
}
async function connect(endpoint) {
  const deadline = Date.now() + 15000
  while (Date.now() < deadline) {
    try { return await MpvClient.connect(endpoint, undefined, 100) } catch {}
    await sleep(30)
  }
  throw new Error('Own test player did not open IPC: ' + endpoint)
}
async function exited(child) {
  if (!child || child.exitCode !== null) return
  await Promise.race([new Promise(resolve => child.once('exit', resolve)),
    sleep(15000).then(() => { if (child.exitCode === null) throw new Error('Own player did not exit') })])
}
async function check(title, seconds, playing = false, clip = false, interval = 1800) {
  assert(observer)
  const ready = Date.now() + 10000
  while (true) {
    try {
      const value = await observer.get('time-pos')
      if (typeof value === 'number' && Math.abs(value - seconds) < .2) break
    } catch {}
    assert(Date.now() < ready, title + ': target was not reached')
    await sleep(20)
  }
  const samples = []
  const first = Date.now()
  while (Date.now() - first < interval) {
    const time = await observer.get('time-pos')
    const file = await observer.get('path')
    assert.equal(path.resolve(file), clip ? second : media, title + ': wrong video')
    assert(time >= seconds - .2 && time <= seconds + (playing ? interval / 1000 + .8 : .2), title + ': unexpected time ' + time)
    samples.push(time)
    await sleep(40)
  }
  const row = { check: title, target: seconds, first: samples[0], last: samples.at(-1), samples: samples.length,
    pid: JSON.parse(await observer.get('user-data/modern/resume')).pid, result: 'passed' }
  reports.push(row)
  console.log(JSON.stringify(row))
}
async function makeController(name, endpoints = []) {
  abort = new AbortController()
  native = new NativeHelper(path.join(base, 'plugin'))
  const data = path.join(root, 'data-' + name)
  await fs.mkdir(data, { recursive: true })
  const ctx = {
    manifest: JSON.parse(await fs.readFile(path.join(base, 'plugin/manifest.json'), 'utf8')), signal: abort.signal,
    app: { paths: { application: root, data, code: path.join(base, 'plugin') } }, register() {},
    ui: { async registerView() {}, async notice(message) { events.push({ ms: stamp(), notice: message }) } },
    settings: { async read() { return settings }, async register() {} },
    storage: { async get() { return session }, async set(key, value) { session = value } },
    log: { warn(message) { events.push({ ms: stamp(), warning: message }) }, info() {}, error() {} },
    events: { async on() {} }, links: { async register() {} }
  }
  controller = await setup(ctx, native, { existingEndpoints: endpoints, spawnPlayer: launch })
}
async function request(seconds, clip = false) {
  const result = await controller.request(link(seconds, clip), note)
  assert(result, 'Latest click must return a verified result')
  assert.equal(result.seconds, seconds)
  assert.equal(path.resolve(result.file), clip ? second : media)
  if (!observer) observer = await connect(session.endpoint)
  const state = JSON.parse(await observer.get('user-data/modern/resume'))
  assert.equal(state.pid, result.identity.pid, 'Identity must match the command connection')
  return result
}
async function stop() {
  if (observer) { try { await observer.command(['quit'], undefined, 1500) } catch {} }
  abort?.abort(); controller?.dispose(); native?.dispose(); observer?.dispose()
  observer = controller = native = null
  for (const child of [...children]) { if (child.exitCode === null) { child.kill(); await exited(child) } }
  manual = null
}
async function seed(remember = true) {
  await fs.writeFile(path.join(config, 'modern-ui.json'), JSON.stringify({ RememberPosition: remember, RememberSpeed: false,
    AutoFolderPlaylist: true, Positions: { [media]: 1037.29, [second]: 3 } }, null, 2))
  session = null
}

async function main() {
  await fs.mkdir(root, { recursive: true })
  await fs.cp(path.resolve(__dirname, '../dist/FrameFlow'), path.join(root, 'player'), { recursive: true,
    filter: source => !source.includes(path.sep + 'portable_config') && !source.endsWith('.pdb') })
  await fs.mkdir(config, { recursive: true })
  await fs.writeFile(path.join(config, 'mpv.conf'), 'osc=no\nresume-playback=yes\nsave-position-on-quit=yes\nkeep-open=yes\nautocreate-playlist=no\n')
  await fs.writeFile(path.join(config, 'mpvnet.conf'), 'process-instance=single\nauto-load-folder=no\n')
  await fs.mkdir(path.dirname(note), { recursive: true })
  await fs.copyFile(path.join(base, 'source/video.mp4'), media)
  await fs.copyFile(path.resolve(__dirname, '../test-results/frame-test.mp4'), second)
  await fs.writeFile(note, '[回看](../01-video.mp4#t=461.850)\n')
  assert.equal(parseLink(link(461.85), note).seconds, 461.85)

  await seed()
  await makeController('cold')
  await request(461.85)
  await check('cold plugin launch remains at the Markdown timestamp', 461.85, false, false, 3200)
  await observer.set('pause', true)
  await request(654.98)
  await check('warm reuse of the same video stays at the next timestamp', 654.98)
  await request(2, true)
  await check('switching video does not reuse the first video position', 2, false, true)
  await request(461.85)
  await check('switching back honors the Markdown timestamp', 461.85)
  const requests = []
  for (const time of [220, 555, 100, 461.85]) { requests.push(controller.request(link(time), note)); await sleep(15) }
  const latest = await Promise.all(requests)
  assert(latest.at(-1) && latest.at(-1).seconds === 461.85)
  await check('rapid different timestamp clicks keep the final click', 461.85, false, false, 3200)
  const playlist = await observer.get('playlist')
  assert.equal(playlist.length, 2)
  reports.push({ check: 'folder playlist remains populated without moving playback', count: playlist.length, result: 'passed' })
  // A manual open forwarded into a plugin-created instance uses shared progress.
  const secondary = launch(player, ['--pause=yes', media])
  await exited(secondary)
  await sleep(300)
  await check('manual open in a plugin-created window uses the common progress', 461.85)
  await stop()

  await seed()
  const endpoint = '\\\\.\\pipe\\mpvnet-manual-replay-audit-' + process.pid
  manual = launch(player, ['--process-instance=multi', '--pause=yes', '--input-ipc-server=' + endpoint, media])
  observer = await connect(endpoint)
  await check('ordinary open restores the common resume before plugin reuse', 1037.29, false, false, 800)
  const existingPid = manual.pid
  await makeController('existing', [endpoint])
  const result = await request(461.85)
  assert.equal(result.identity.pid, existingPid)
  await check('plugin reuses the verified existing manual window', 461.85, false, false, 3200)
  settings.autoPlay = true
  await request(654.98)
  await check('automatic playback advances after the requested timestamp', 654.98, true)
  await observer.set('pause', true)
  settings.autoPlay = false
  await request(461.85)
  await stop()
  // Keep the stale saved session so the real controller checks its identity,
  // rejects the closed endpoint, and launches a new independent connection.
  await makeController('reconnect')
  await request(654.98)
  await check('stale saved session reconnects and honors a new timestamp', 654.98, false, false, 3200)
  await stop()

  await seed(false)
  await makeController('remember-off')
  await request(461.85)
  await check('disabling resume does not disable explicit plugin jumps', 461.85, false, false, 2200)
  await stop()
  const hash = async file => crypto.createHash('sha256').update(await fs.readFile(file)).digest('hex')
  await fs.writeFile(path.join(root, 'results.json'), JSON.stringify({ plugin: '0.2.1', playerSHA256: await hash(player),
    pluginSHA256: await hash(path.join(base, 'plugin/main.cjs')), reports, events }, null, 2))
  console.log('All ' + reports.length + ' actual MarkText replay checks passed.')
}
main().catch(async error => {
  console.error(error)
  await fs.writeFile(path.join(root, 'failed.json'), JSON.stringify({ message: error.message, stack: error.stack, reports, events }, null, 2))
  process.exitCode = 1
}).finally(stop)
