// Shared helpers for the HUDHUDNEST staging QA batch. STAGING ONLY - see safetyGate().
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import zlib from 'node:zlib';

export const BATCH_ID = 'HUDHUDNEST-STAGING-QA-2026-10';
export const BASE = 'https://propertyapi-staging-api.onrender.com';
export const PRODUCTION_HOSTS = ['wohnungen-api.onrender.com', 'hudhudnest.com'];
export const STATE_FILE = new URL('./state.json', import.meta.url);

export function loadEnv() {
  const p = process.env.STAGING_ENV_FILE;
  if (p && existsSync(p)) {
    for (const line of readFileSync(p, 'utf8').split(/\r?\n/)) {
      const i = line.indexOf('=');
      if (i > 0) process.env[line.slice(0, i).trim()] = line.slice(i + 1).trim();
    }
  }
  for (const k of ['STAGING_SMOKE_FIXED_OTP', 'STAGING_SMOKE_PHONE_PREFIX', 'STAGING_SMOKE_CLEANUP_SECRET', 'STAGING_SMOKE_PASSWORD'])
    if (!process.env[k]) throw new Error(`missing ${k}`);
  return {
    otp: process.env.STAGING_SMOKE_FIXED_OTP, prefix: process.env.STAGING_SMOKE_PHONE_PREFIX,
    secret: process.env.STAGING_SMOKE_CLEANUP_SECRET, password: process.env.STAGING_SMOKE_PASSWORD,
  };
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const loadState = () => (existsSync(STATE_FILE) ? JSON.parse(readFileSync(STATE_FILE, 'utf8')) : { accounts: [], listings: [], log: [] });
export const saveState = (s) => writeFileSync(STATE_FILE, JSON.stringify(s, null, 1));

// ENVIRONMENT SAFETY GATE: aborts unless the API proves it is Staging with isolation on.
export async function safetyGate(env) {
  const host = new URL(BASE).host;
  if (PRODUCTION_HOSTS.includes(host)) throw new Error('STOP: production host');
  const r = await fetch(`${BASE}/api/operational/build-info`, { headers: { 'X-Staging-Smoke-Secret': env.secret } });
  if (r.status !== 200) throw new Error(`STOP: build-info HTTP ${r.status}`);
  const b = await r.json();
  const iso = b.isolation ?? {};
  if (b.environment !== 'Staging' || !b.migration?.complete || iso.externalNotificationsDisabled !== true ||
      iso.inMemoryMedia !== false || !/staging/i.test(iso.storageMarker ?? '') || !/staging/i.test(iso.redisMarker ?? ''))
    throw new Error('STOP: not provably Staging ' + JSON.stringify(iso));
  return { environment: b.environment, commitSha: b.commitSha, version: b.version, isolation: iso, migrations: b.migration.appliedCount };
}

export class Client {
  constructor(name, env, origin = 'https://staging--hudhudnest.netlify.app') {
    this.name = name; this.env = env; this.origin = origin; this.jar = new Map(); this.token = null; this.csrf = null; this.timings = [];
  }
  cookieHeader() { return [...this.jar].map(([k, v]) => `${k}=${v}`).join('; '); }
  store(res) {
    for (const sc of res.headers.getSetCookie?.() ?? []) {
      const [pair] = sc.split(';'); const i = pair.indexOf('='); const k = pair.slice(0, i).trim(); const v = pair.slice(i + 1);
      if (/expires=Thu, 01 Jan 1970|max-age=0/i.test(sc) || v === '') this.jar.delete(k); else this.jar.set(k, v);
    }
  }
  async req(method, path, { json, form, auth = true, secret = false, headers = {}, noCsrf = false, token } = {}) {
    const h = { Origin: this.origin, ...headers };
    if (this.jar.size) h.Cookie = this.cookieHeader();
    if (method !== 'GET' && !noCsrf) {
      if (!this.csrf) await this.fetchCsrf();
      h['X-XSRF-TOKEN'] = this.csrf;
    }
    const t = token ?? (auth ? this.token : null);
    if (t) h.Authorization = `Bearer ${t}`;
    if (secret) h['X-Staging-Smoke-Secret'] = this.env.secret;
    let body;
    if (json !== undefined) { h['Content-Type'] = 'application/json'; body = JSON.stringify(json); }
    if (form) body = form;
    const t0 = performance.now();
    let res, text;
    for (let a = 0; a < 3; a++) {
      try { res = await fetch(BASE + path, { method, headers: h, body, redirect: 'manual', signal: AbortSignal.timeout(60000) }); text = await res.text(); break; }
      catch (e) { if (a === 2) return { status: 0, data: String(e), ms: 0, headers: new Headers() }; await sleep(2000); }
    }
    const ms = performance.now() - t0;
    this.store(res);
    let data; try { data = JSON.parse(text); } catch { data = text; }
    this.timings.push({ method, path: path.split('?')[0].replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, '{id}'), status: res.status, ms });
    return { status: res.status, data, ms, headers: res.headers };
  }
  async fetchCsrf() {
    for (let a = 0; a < 4; a++) {
      const r = await fetch(`${BASE}/api/security/csrf-token`, { headers: { Origin: this.origin, ...(this.jar.size ? { Cookie: this.cookieHeader() } : {}) } });
      this.store(r); const t = await r.text();
      try { this.csrf = JSON.parse(t).csrfToken; if (this.csrf) return; } catch { /* retry */ }
      this.csrfFailures = (this.csrfFailures ?? []).concat(`HTTP ${r.status} len=${t.length}`);
      await sleep(r.status === 429 ? 20000 : 1500 * (a + 1));
    }
    throw new Error('csrf-token unavailable: ' + (this.csrfFailures ?? []).join('; '));
  }
}

const tok = (d) => d?.accessToken ?? d?.AccessToken;
export async function registerUser(c, phone, first, last) {
  const s = await c.req('POST', '/api/auth/phone/registration/send-otp', { json: { PhoneNumber: phone }, auth: false, secret: true });
  if (s.status !== 200) return { ok: false, step: 'send-otp', res: s };
  const challengeId = s.data.challengeId ?? s.data.ChallengeId;
  const v = await c.req('POST', '/api/auth/phone/registration/verify', {
    json: { ChallengeId: challengeId, Code: c.env.otp, Password: c.env.password, FirstName: first, LastName: last }, auth: false, secret: true });
  if (v.status !== 200) return { ok: false, step: 'verify', res: v };
  c.token = tok(v.data);
  const me = await c.req('GET', '/api/users/me');
  return { ok: !!c.token && me.status === 200, userId: me.data?.id ?? me.data?.Id, me };
}
export async function login(c, phone) {
  const r = await c.req('POST', '/api/auth/phone/login', { json: { PhoneNumber: phone, Password: c.env.password }, auth: false });
  if (r.status === 200) c.token = tok(r.data);
  return r;
}

// ---- synthetic images: procedurally generated PNGs (no real people) ----
function crc32(buf) { let c, crc = ~0; for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; } return (~crc) >>> 0; }
function chunk(type, data) { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td)); return Buffer.concat([len, td, crc]); }
export function makePng(seed, w = 640, h = 420) {
  const rnd = (n) => { const x = Math.sin(seed * 9301 + n * 49297) * 233280; return x - Math.floor(x); };
  const hue = Math.floor(rnd(1) * 255), bw = 200 + Math.floor(rnd(2) * 200), bh = 160 + Math.floor(rnd(3) * 140), bx = 60 + Math.floor(rnd(4) * 120);
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 3 + 1)] = 0;
    for (let x = 0; x < w; x++) {
      const o = y * (w * 3 + 1) + 1 + x * 3; let r, g, b;
      if (y > h - 60) { r = 90; g = 120 + (y % 7); b = 80; }
      else if (x >= bx && x < bx + bw && y > h - 60 - bh) {
        const win = ((x - bx) % 40 > 10 && (x - bx) % 40 < 30 && (y % 44) > 12 && (y % 44) < 34);
        r = win ? 190 : 200 - hue / 4; g = win ? 220 : 170; b = win ? 245 : 140 + hue / 3;
      } else { r = 120 + (y >> 2); g = 170 + (y >> 3); b = 235; }
      raw[o] = r & 255; raw[o + 1] = g & 255; raw[o + 2] = b & 255;
    }
  }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
export async function uploadImages(c, propertyId, pngs, contentType = 'image/png', name = 'qa') {
  const form = new FormData();
  pngs.forEach((b, i) => form.append('files', new Blob([b], { type: contentType }), `${name}-${i + 1}.png`));
  return c.req('POST', `/api/properties/${propertyId}/images`, { form });
}
export const pct = (arr, p) => { if (!arr.length) return 0; const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.ceil(p / 100 * s.length) - 1)]; };
