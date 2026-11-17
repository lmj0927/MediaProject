import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout } from 'node:timers/promises';

const source = dirname(fileURLToPath(import.meta.url));
const project = resolve(source, '../..');
const destination = join(project, 'Assets', 'RoomPresentation', 'UI Sprites');
const scale = Number(process.argv[2] ?? 6);
if (!Number.isInteger(scale) || scale < 1 || scale > 10) throw new Error('Choose an integer sprite export scale from 1 to 10.');

const scratch = join(project, 'tmp');
await mkdir(scratch, { recursive: true });
const profile = await mkdtemp(join(scratch, 'ui-export-'));
const edge = process.env.UI_EDGE_PATH ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const browser = spawn(edge, [
  '--headless=new', '--no-first-run', '--disable-background-networking',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank',
], { windowsHide: true, stdio: 'ignore' });
let spawnError;
browser.on('error', error => { spawnError = error; });

let socket;
try {
  let port;
  for (let attempt = 0; attempt < 120 && !port; attempt++) {
    if (spawnError) throw spawnError;
    try {
      const endpoint = await readFile(join(profile, 'DevToolsActivePort'), 'utf8');
      [port] = endpoint.trim().split(/\r?\n/);
    } catch {
      await setTimeout(100);
    }
  }
  if (!port) throw new Error('Edge did not start the local SVG renderer.');
  const targets = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
  await new Promise((accept, reject) => {
    socket.addEventListener('open', accept, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });

  const manifest = JSON.parse(await readFile(join(source, 'sprites.json'), 'utf8'));
  let id = 0;
  function send(method, params) {
    const request = ++id;
    return new Promise((accept, reject) => {
      function receive(event) {
        const response = JSON.parse(event.data);
        if (response.id !== request) return;
        socket.removeEventListener('message', receive);
        if (response.error) reject(new Error(response.error.message));
        else accept(response.result);
      }
      socket.addEventListener('message', receive);
      socket.send(JSON.stringify({ id: request, method, params }));
    });
  }

  for (const [name, { width, height }] of Object.entries(manifest)) {
    const svg = (await readFile(join(source, name + '.svg'), 'utf8'))
      .replace(/ width="[^"]+" height="[^"]+"/, ' width="' + width * scale + '" height="' + height * scale + '"');
    const uri = 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
    const expression = `(async () => {
      const image = new Image();
      image.src = ${JSON.stringify(uri)};
      await image.decode();
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      canvas.getContext('2d').drawImage(image, 0, 0);
      return canvas.toDataURL().split(',')[1];
    })()`;
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails || !result.result.value) throw new Error('Could not render ' + name);
    await writeFile(join(destination, name + '.png'), Buffer.from(result.result.value, 'base64'));
  }
  console.log('Exported ' + Object.keys(manifest).length + ' transparent PNGs at ' + scale + 'x.');
  socket.close();
} finally {
  if (socket?.readyState === WebSocket.OPEN) socket.close();
  browser.kill();
  if (dirname(resolve(profile)) === resolve(scratch) && basename(profile).startsWith('ui-export-')) {
    await rm(profile, { recursive: true, force: true, maxRetries: 4, retryDelay: 250 }).catch(() => {});
  }
}
