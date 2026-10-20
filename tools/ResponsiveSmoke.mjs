// Run: node tools/ResponsiveSmoke.mjs
// Uses an isolated local Chrome profile and the built-in Node.js WebSocket API.
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const chrome = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const baseUrl = process.env.NEXUS_URL || 'https://localhost:44391';
const widths = [320, 375, 390, 620, 768, 1024, 1440];
const pages = ['/', '/Account/Login', '/Talent/Index'];
const output = path.join(path.dirname(fileURLToPath(import.meta.url)), '_runtime');
const profile = await mkdtemp(path.join(tmpdir(), 'nexus-responsive-'));
const child = spawn(chrome, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--ignore-certificate-errors', '--remote-debugging-port=0',
  `--user-data-dir=${profile}`, 'about:blank'
], { stdio: 'ignore' });

let socket;
let nextId = 1;
const pending = new Map();

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function connect() {
  let port;
  for (let i = 0; i < 70; i++) {
    if (child.exitCode !== null) throw new Error(`Chrome exited: ${child.exitCode}`);
    try {
      port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]);
      break;
    } catch { await sleep(150); }
  }
  if (!port) throw new Error('Chrome DevTools port unavailable');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find(item => item.type === 'page');
  if (!target) throw new Error('No Chrome page target');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    const item = pending.get(message.id);
    if (!item) return;
    pending.delete(message.id);
    if (message.error) item.reject(new Error(message.error.message));
    else item.resolve(message.result);
  });
}

function call(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`Timed out: ${method}`));
    }, 12000);
    pending.set(id, {
      resolve: value => { clearTimeout(timer); resolve(value); },
      reject: error => { clearTimeout(timer); reject(error); }
    });
    socket.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(expression) {
  const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

async function waitForPage() {
  for (let i = 0; i < 50; i++) {
    if (await evaluate('document.readyState === "complete"')) return;
    await sleep(200);
  }
  throw new Error('Page did not finish loading');
}

const observations = [];
try {
  await connect();
  await call('Page.enable');
  await call('Runtime.enable');
  await mkdir(output, { recursive: true });
  for (const page of pages) {
    for (const width of widths) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
      await call('Page.navigate', { url: baseUrl + page });
      await waitForPage();
      const measured = await evaluate(`(() => {
        const root = document.documentElement;
        const viewport = window.innerWidth;
        const offenders = [...document.querySelectorAll('body *')]
          .filter(e => { const box = e.getBoundingClientRect(); return box.width && box.right > viewport + 2 && getComputedStyle(e).position !== 'fixed'; })
          .slice(0, 5).map(e => ({tag: e.tagName, class: String(e.className).slice(0, 70), right: Math.round(e.getBoundingClientRect().right)}));
        return { width: viewport, scrollWidth: root.scrollWidth, status: document.title, offenders };
      })()`);
      const name = page === '/' ? 'home' : page.split('/').pop().toLowerCase();
      if (page === '/' && [320, 390, 768, 1440].includes(width)) {
        const picture = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
        await writeFile(path.join(output, `responsive-${name}-${width}.png`), Buffer.from(picture.data, 'base64'));
      }
      observations.push({ page, ...measured });
      console.log(`${measured.scrollWidth > width + 1 ? 'FAIL' : 'PASS'} ${page} ${width}px scrollWidth=${measured.scrollWidth} ${JSON.stringify(measured.offenders)}`);
    }
  }
  await writeFile(path.join(output, 'responsive-results.json'), JSON.stringify(observations, null, 2));
  const failed = observations.filter(item => item.scrollWidth > item.width + 1);
  console.log(`RESPONSIVE_SMOKE=${observations.length - failed.length}/${observations.length}`);
  if (failed.length) process.exitCode = 1;
} catch (error) {
  console.error('RESPONSIVE_SMOKE_ERROR', error.message);
  process.exitCode = 2;
} finally {
  socket?.close();
  child.kill();
  await sleep(350);
  await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }).catch(() => {});
}
