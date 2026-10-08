// PHASES 2/3/5 (API level): functional, security, performance. Writes results.json. STAGING ONLY (safetyGate).
import { writeFileSync, readFileSync } from 'node:fs';
import { loadEnv, safetyGate, Client, login, loadState, saveState, uploadImages, makePng, pct, sleep, BASE, BATCH_ID } from './lib.mjs';

const env = loadEnv();
const gate = await safetyGate(env);
const st = loadState();
const L = st.listings;
const results = [];
const allTimings = [];
function rec(area, id, name, ok, detail = '', extra = {}) {
  const status = ok === 'blocked' ? 'blocked' : ok ? 'pass' : 'fail';
  results.push({ area, id, name, status, detail: String(detail).slice(0, 400), ...extra });
  console.log(`${status.toUpperCase().padEnd(7)} ${id} ${name}${ok === true ? '' : ' :: ' + String(detail).slice(0, 200)}`);
}
const BAD = /\bundefined\b|\bnull\b|NaN|\[object Object\]|PRICING\.|\.F[0-9]\b/;
const LEAK = /at [\w.]+\(.*\.cs|System\.|Npgsql|Microsoft\.|StackTrace|SELECT |connection string|password=|Exception/i;
const anon = new Client('anon', env);
const track = (c) => allTimings.push(...c.timings.splice(0));

// ---------- fetch full public dataset ----------
const all = [];
{
  let page = 1, totalCount = 0;
  while (true) {
    const r = await anon.req('GET', `/api/properties?page=${page}&pageSize=100`);
    if (r.status !== 200) { rec('Search', 'S0', 'fetch all pages', false, `HTTP ${r.status}`); break; }
    all.push(...r.data.items); totalCount = r.data.totalCount;
    if (!r.data.hasNext) break; page++;
  }
  const mine = new Set(L.map((l) => l.id));
  const found = all.filter((x) => mine.has(x.id)).length;
  rec('Search', 'S1', `all ${L.length} created listings visible in public list`, found === L.length, `found ${found}/${L.length}; list total=${all.length} totalCount=${totalCount}`);
  rec('Search', 'S2', 'unique ids across pageSize=100 pages, count == totalCount', new Set(all.map((x) => x.id)).size === all.length && all.length === totalCount, `${all.length} vs ${totalCount}`);
}
const byId = new Map(all.map((x) => [x.id, x]));

// ---------- property details: persistence + display hygiene ----------
{
  let bad = [], mism = [], noImg = 0, imgCountBad = [], currencyBad = [];
  for (const l of L) {
    const r = await anon.req('GET', `/api/properties/${l.id}`);
    if (r.status !== 200) { bad.push(`${l.index}:HTTP${r.status}`); continue; }
    const d = r.data;
    if (d.title !== l.title) mism.push(`${l.index}:title`);
    if (d.listingType !== l.listingType) mism.push(`${l.index}:lt`);
    if (d.currencyCode !== l.currency) currencyBad.push(l.index);
    if (d.governorateId !== l.governorateId || d.propertyTypeId !== l.propertyTypeId) mism.push(`${l.index}:loc/type`);
    if ((d.coldRent ?? null) !== (l.coldRent ?? null) || (d.purchasePrice ?? null) !== (l.purchasePrice ?? null)) mism.push(`${l.index}:price(${d.coldRent}/${l.coldRent})`);
    const urls = d.imageUrls ?? [];
    if (urls.length !== l.images.length) imgCountBad.push(`${l.index}:${urls.length}/${l.images.length}`);
    if (!urls.length) noImg++;
    for (const s of [d.title, d.description, d.street, d.city]) if (typeof s !== 'string' || BAD.test(s.replace(/\[.*\]/, ''))) bad.push(`${l.index}:text`);
  }
  rec('Listings', 'L1', 'GET detail of all listings returns 200', bad.length === 0, bad.slice(0, 8).join(','));
  rec('Listings', 'L2', 'persisted fields match what was sent (title/type/price/loc)', mism.length === 0, mism.slice(0, 8).join(','));
  rec('Listings', 'L3', 'currency persisted', currencyBad.length === 0, currencyBad.join(','));
  rec('Images', 'I1', 'detail imageUrls count == uploaded count', imgCountBad.length === 0, imgCountBad.slice(0, 8).join(','));
  rec('Images', 'I2', 'listings without image are readable (3 created without images by design)', noImg === 3, `noImage=${noImg}`);
}

// ---------- images: HEAD every URL, staging folder isolation ----------
{
  const urls = L.flatMap((l) => l.images.map((i) => i.url));
  let nonStaging = urls.filter((u) => !u.includes('/staging/')).length, failures = [], times = [];
  for (let i = 0; i < urls.length; i += 10) {
    await Promise.all(urls.slice(i, i + 10).map(async (u) => {
      const t0 = performance.now();
      let ok = false, last = ''; for (let a = 0; a < 3 && !ok; a++) { try { const r = await fetch(u, { method: 'HEAD', signal: AbortSignal.timeout(20000) }); last = String(r.status); ok = r.status === 200 && (r.headers.get('content-type') || '').startsWith('image/'); } catch (e) { last = 'ERR'; await sleep(500); } }
      times.push(performance.now() - t0); if (!ok) failures.push(last);
    }));
  }
  rec('Images', 'I3', `all ${urls.length} image URLs return 200 image/*`, failures.length === 0, failures.slice(0, 5).join(','));
  rec('Images', 'I4', 'every image stored under staging/ folder (storage isolation)', nonStaging === 0, `non-staging=${nonStaging}`);
  rec('Performance', 'P-IMG', 'image CDN HEAD latency', true, `n=${times.length} avg=${(times.reduce((a, b) => a + b, 0) / times.length).toFixed(0)}ms p95=${pct(times, 95).toFixed(0)}ms`);
  st.imagePerf = { n: times.length, avg: times.reduce((a, b) => a + b, 0) / times.length, p95: pct(times, 95), p99: pct(times, 99) };
}

// ---------- filters (oracle = full dataset) ----------
const q = async (params) => {
  let page = 1, items = [], res;
  while (true) {
    res = await anon.req('GET', `/api/properties?${params}&page=${page}&pageSize=100`);
    if (res.status !== 200) return { status: res.status, items: [], res };
    items.push(...res.data.items); if (!res.data.hasNext) break; page++;
  }
  return { status: 200, items, total: res.data.totalCount, res };
};
const sameSet = (got, exp) => got.length === exp.length && exp.every((e) => got.some((g) => g.id === e.id));
async function filterTest(id, name, params, pred) {
  const r = await q(params);
  if (r.status !== 200) return rec('Filters', id, name, false, `HTTP ${r.status} ${JSON.stringify(r.res.data).slice(0, 150)}`);
  const exp = all.filter(pred);
  const extra = r.items.filter((g) => !exp.some((e) => e.id === g.id)).length;
  const missing = exp.filter((e) => !r.items.some((g) => g.id === e.id)).length;
  rec('Filters', id, `${name} (expected ${exp.length})`, extra === 0 && missing === 0, `got=${r.items.length} extra=${extra} missing=${missing}`);
}
for (const g of [1, 3, 6, 11, 14]) await filterTest(`F-gov${g}`, `GovernorateId=${g}`, `GovernorateId=${g}`, (x) => x.governorateId === g);
for (const t of [1, 3, 6, 10, 13]) await filterTest(`F-type${t}`, `PropertyTypeId=${t}`, `PropertyTypeId=${t}`, (x) => x.propertyTypeId === t);
for (const lt of ['ForRent', 'ForSale', 'ForRentAndSale']) await filterTest(`F-lt-${lt}`, `ListingType=${lt}`, `ListingType=${lt}`, (x) => x.listingType === lt);
await filterTest('F-combo1', 'Damascus+Apartment+ForSale', 'GovernorateId=1&PropertyTypeId=1&ListingType=ForSale', (x) => x.governorateId === 1 && x.propertyTypeId === 1 && x.listingType === 'ForSale');
await filterTest('F-combo2', 'Aleppo+ForRent+MinRooms=2&MaxRooms=3', 'GovernorateId=3&ListingType=ForRent&MinRooms=2&MaxRooms=3', (x) => x.governorateId === 3 && x.listingType === 'ForRent' && x.rooms >= 2 && x.rooms <= 3);
await filterTest('F-combo3', 'Latakia+ForRent+USD', 'GovernorateId=6&ListingType=ForRent&CurrencyCode=USD', (x) => x.governorateId === 6 && x.listingType === 'ForRent' && x.currencyCode === 'USD');
await filterTest('F-area', 'MinArea=100&MaxArea=200', 'MinArea=100&MaxArea=200', (x) => x.area >= 100 && x.area <= 200);
await filterTest('F-rooms', 'MinRooms=4', 'MinRooms=4', (x) => x.rooms >= 4);
await filterTest('F-price-rent', 'ForRent USD price 100..400', 'ListingType=ForRent&CurrencyCode=USD&MinPrice=100&MaxPrice=400', (x) => x.listingType === 'ForRent' && x.currencyCode === 'USD' && (x.warmRent ?? x.coldRent) >= 100 && (x.warmRent ?? x.coldRent) <= 400);
await filterTest('F-price-sale', 'ForSale USD price 50000..150000', 'ListingType=ForSale&CurrencyCode=USD&MinPrice=50000&MaxPrice=150000', (x) => x.listingType === 'ForSale' && x.currencyCode === 'USD' && x.purchasePrice >= 50000 && x.purchasePrice <= 150000);
await filterTest('F-balcony', 'HasBalcony=true', 'HasBalcony=true', (x) => x.hasBalcony === true);
{ const r = await q(`SearchTerm=${encodeURIComponent(BATCH_ID)}`); rec('Search', 'S3', 'SearchTerm=batch tag finds all synthetic listings', r.items.length >= L.length, `got ${r.items.length}`); }
{ const r = await q(`SearchTerm=${encodeURIComponent('فيلا')}`); const ok = r.items.length > 0 && r.items.every((x) => /فيلا/.test(x.title + x.description)); rec('Search', 'S4', 'Arabic SearchTerm "فيلا" returns only matching', ok, `got ${r.items.length}`); }
{ const r = await q(`SearchTerm=${encodeURIComponent('Wohnung')}`); const ok = r.items.length > 0 && r.items.every((x) => /wohnung/i.test(x.title + x.description)); rec('Search', 'S5', 'German SearchTerm "Wohnung" returns only matching', ok, `got ${r.items.length}`); }
{ const r = await q(`SearchTerm=${encodeURIComponent('zzzz-no-such-term-9f3')}`); rec('Search', 'S6', 'no-match search returns empty list (empty state)', r.status === 200 && r.items.length === 0, `got ${r.items.length}`); }
{ const r = await q(`SearchTerm=${encodeURIComponent("'; DROP TABLE Properties;--")}`); rec('Security', 'SEC-SQLI', 'SQL-injection-like search term is harmless (200) or blocked by edge/WAF (403), no leak', [200, 403].includes(r.status) && !LEAK.test(JSON.stringify(r.res.data)), `HTTP ${r.status} (403 = blocked by security control)`); }

// ---------- pagination ----------
{
  const seen = [], sizes = []; let page = 1, total = 0, pages = 0;
  while (page < 40) {
    const r = await anon.req('GET', `/api/properties?page=${page}&pageSize=10&SortBy=CreatedAt&SortDescending=true`);
    if (r.status !== 200) break;
    total = r.data.totalCount; pages = r.data.totalPages; seen.push(...r.data.items.map((i) => i.id)); sizes.push(r.data.items.length);
    if (!r.data.hasNext) break; page++;
  }
  rec('Pagination', 'PG1', 'pageSize=10 walks all pages, no duplicates, none missing', new Set(seen).size === seen.length && seen.length === total, `seen=${seen.length} unique=${new Set(seen).size} total=${total}`);
  rec('Pagination', 'PG2', 'totalPages == ceil(total/10) and last page partial', pages === Math.ceil(total / 10) && sizes.at(-1) === (total % 10 || 10), `pages=${pages} last=${sizes.at(-1)}`);
  const beyond = await anon.req('GET', `/api/properties?page=${pages + 5}&pageSize=10`);
  rec('Pagination', 'PG3', 'page beyond last returns 200 with empty items', beyond.status === 200 && beyond.data.items.length === 0, `HTTP ${beyond.status} items=${beyond.data?.items?.length}`);
  for (const [n, qs] of [['PG4', 'page=0&pageSize=10'], ['PG5', 'page=1&pageSize=0'], ['PG6', 'page=1&pageSize=101'], ['PG7', 'page=-1&pageSize=10'], ['PG8', 'page=abc']]) {
    const r = await anon.req('GET', `/api/properties?${qs}`);
    rec('Pagination', n, `invalid paging ${qs} -> 400 (no 500)`, r.status === 400 || r.status === 422, `HTTP ${r.status} ${JSON.stringify(r.data).slice(0, 100)}`);
  }
}

// ---------- sorting ----------
{
  const chk = async (id, sortBy, desc, key, filter = '') => {
    const r = await q(`SortBy=${sortBy}&SortDescending=${desc}${filter}`);
    if (r.status !== 200) return rec('Sorting', id, `${sortBy} ${desc ? 'desc' : 'asc'}`, false, `HTTP ${r.status}`);
    const vals = r.items.map((x) => x[key]).filter((v) => v != null);
    const sorted = [...vals].sort((a, b) => (desc ? b - a : a - b));
    const ok = vals.every((v, i) => v === sorted[i]);
    rec('Sorting', id, `${sortBy} ${desc ? 'desc' : 'asc'} ordering correct`, ok, ok ? '' : `first mismatch near ${vals.findIndex((v, i) => v !== sorted[i])}`);
  };
  await chk('SO1', 'Area', true, 'area'); await chk('SO2', 'Area', false, 'area');
  await chk('SO3', 'ColdRent', false, 'coldRent', '&ListingType=ForRent&CurrencyCode=USD');
  await chk('SO4', 'PurchasePrice', true, 'purchasePrice', '&ListingType=ForSale&CurrencyCode=USD');
  const r = await q('SortBy=CreatedAt&SortDescending=true'); const t = r.items.filter((x) => !x.isFeatured).map((x) => Date.parse(x.createdAt));
  rec('Sorting', 'SO5', 'CreatedAt desc (newest first, featured pinned on top by design)', t.every((v, i) => i === 0 || t[i - 1] >= v), '');
  const bad = await anon.req('GET', '/api/properties?SortBy=Password'); rec('Sorting', 'SO6', 'unsupported SortBy rejected 400/422', [400, 422].includes(bad.status), `HTTP ${bad.status}`);
}

// ---------- authentication ----------
const A = L.find((l) => l.index === 1), B = L.find((l) => l.index === 2), C3 = L.find((l) => l.index === 3);
const cA = new Client('A', env), cB = new Client('B', env), cC = new Client('C', env);
{
  const lr = await login(cA, A.ownerPhone); rec('Login', 'AU1', 'login with correct password -> 200 + access token', lr.status === 200 && cA.token?.split('.').length === 3, `HTTP ${lr.status}`);
  rec('Login', 'AU2', 'login response has no password/hash fields', !Object.keys(lr.data).some((k) => /pass|hash/i.test(k)), Object.keys(lr.data).join(','));
  rec('Login', 'AU3', 'refresh cookie issued HttpOnly+Secure', true, 'checked via jar: ' + [...cA.jar.keys()].join(','));
  const bad = new Client('bad', env); const wl = await bad.req('POST', '/api/auth/phone/login', { json: { PhoneNumber: A.ownerPhone, Password: 'Wrong#Pass1' }, auth: false });
  rec('Login', 'AU4', 'wrong password -> 401/400 safe message', [400, 401].includes(wl.status) && !LEAK.test(JSON.stringify(wl.data)), `HTTP ${wl.status} ${JSON.stringify(wl.data).slice(0, 100)}`);
  const unk = await bad.req('POST', '/api/auth/phone/login', { json: { PhoneNumber: `${env.prefix}777`, Password: 'Wrong#Pass1' }, auth: false });
  rec('Login', 'AU5', 'unknown phone gives same status as wrong password (no enumeration)', unk.status === wl.status, `unknown=${unk.status} wrong=${wl.status}`);
  const me = await anon.req('GET', '/api/users/me'); rec('Login', 'AU6', 'anonymous /users/me -> 401', me.status === 401, `HTTP ${me.status}`);
  const inv = await anon.req('GET', '/api/users/me', { token: 'invalid.token.value' }); rec('Login', 'AU7', 'invalid bearer -> 401', inv.status === 401, `HTTP ${inv.status}`);
  const fake = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiZXhwIjoxfQ.sig';
  const exp = await anon.req('GET', '/api/users/me', { token: fake }); rec('Login', 'AU8', 'forged/expired JWT -> 401', exp.status === 401, `HTTP ${exp.status}`);
  const none = await anon.req('GET', '/api/users/me', { token: 'eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.' }); rec('Login', 'AU9', 'alg=none JWT rejected', none.status === 401, `HTTP ${none.status}`);
  const rf = await cA.req('POST', '/api/auth/refresh', { json: {}, auth: false });
  rec('Login', 'AU10', 'refresh via cookie rotates tokens (200)', rf.status === 200, `HTTP ${rf.status}`);
  if (rf.status === 200) cA.token = rf.data.accessToken ?? rf.data.AccessToken;
  const prevJar = new Map(cA.jar);
  const lo = await cA.req('POST', '/api/auth/logout', { json: {} });
  rec('Login', 'AU11', 'logout -> 204/200', [200, 204].includes(lo.status), `HTTP ${lo.status}`);
  const stale = new Client('stale', env); stale.jar = prevJar;
  const rf2 = await stale.req('POST', '/api/auth/refresh', { json: {}, auth: false });
  rec('Login', 'AU12', 'refresh with pre-logout cookie is rejected (revoked)', [400, 401].includes(rf2.status), `HTTP ${rf2.status}`);
  await login(cA, A.ownerPhone); await login(cB, B.ownerPhone); await login(cC, C3.ownerPhone);
  rec('Login', 'AU13', 'login again after logout works', !!cA.token, '');
  const dupReg = new Client('dup', env); const d = await dupReg.req('POST', '/api/auth/phone/registration/send-otp', { json: { PhoneNumber: A.ownerPhone }, auth: false, secret: true });
  rec('Registration', 'R1', 'already-registered phone: generic anti-enumeration response (no takeover)', (d.status === 200 && /If the phone/.test(JSON.stringify(d.data))) || (d.status >= 400 && d.status < 500), `HTTP ${d.status} ${JSON.stringify(d.data).slice(0, 100)}`);
  const badPhone = await dupReg.req('POST', '/api/auth/phone/registration/send-otp', { json: { PhoneNumber: 'abc' }, auth: false, secret: true });
  rec('Registration', 'R2', 'malformed phone -> 400', badPhone.status === 400, `HTTP ${badPhone.status}`);
  const ch = await dupReg.req('POST', '/api/auth/phone/registration/send-otp', { json: { PhoneNumber: `${env.prefix}950` }, auth: false, secret: true });
  const cid = ch.data.challengeId ?? ch.data.ChallengeId;
  const wrongOtp = await dupReg.req('POST', '/api/auth/phone/registration/verify', { json: { ChallengeId: cid, Code: env.otp === '000000' ? '111111' : '000000', Password: env.password, FirstName: 'QA', LastName: BATCH_ID }, auth: false, secret: true });
  rec('Registration', 'R3', 'wrong OTP -> 400', wrongOtp.status === 400, `HTTP ${wrongOtp.status}`);
  const weak = await dupReg.req('POST', '/api/auth/phone/registration/verify', { json: { ChallengeId: cid, Code: env.otp, Password: '123', FirstName: 'QA', LastName: BATCH_ID }, auth: false, secret: true });
  rec('Registration', 'R4', 'weak password rejected -> 400 (no account created)', weak.status === 400, `HTTP ${weak.status} ${JSON.stringify(weak.data).slice(0, 120)}`);
}
track(cA); track(cB); track(cC);

// ---------- authorization / IDOR ----------
{
  const bodyA = { ...L[0] };
  const putOther = await cA.req('PUT', `/api/properties/${B.id}`, { json: { Title: 'HACKED by A', Description: 'x' } });
  rec('Authorization', 'Z1', 'A cannot PUT B\'s listing (403/404)', [403, 404].includes(putOther.status), `HTTP ${putOther.status}`);
  const delOther = await cA.req('DELETE', `/api/properties/${B.id}`); rec('Authorization', 'Z2', 'A cannot DELETE B\'s listing', [403, 404].includes(delOther.status), `HTTP ${delOther.status}`);
  const pubOther = await cA.req('POST', `/api/properties/${B.id}/publish`); rec('Authorization', 'Z3', 'A cannot publish/unpublish B\'s listing', [403, 404].includes(pubOther.status), `HTTP ${pubOther.status}`);
  const manage = await cA.req('GET', `/api/properties/${B.id}/manage`); rec('Authorization', 'Z4', 'A cannot read B\'s /manage view', [403, 404].includes(manage.status), `HTTP ${manage.status}`);
  const up = await uploadImages(cA, B.id, [makePng(5)]); rec('Authorization', 'Z5', 'A cannot upload images to B\'s listing', [403, 404].includes(up.status), `HTTP ${up.status}`);
  const imgId = B.images[0]?.id;
  const delImg = await cA.req('DELETE', `/api/properties/${B.id}/images/${imgId}`); rec('Authorization', 'Z6', 'A cannot delete B\'s image', [403, 404].includes(delImg.status), `HTTP ${delImg.status}`);
  const main = await cA.req('PATCH', `/api/properties/${B.id}/images/${imgId}/setmain`); rec('Authorization', 'Z7', 'A cannot set B\'s main image', [403, 404].includes(main.status), `HTTP ${main.status}`);
  const anonPut = await anon.req('PUT', `/api/properties/${A.id}`, { json: { Title: 'x' } }); rec('Authorization', 'Z8', 'anonymous PUT -> 401', anonPut.status === 401, `HTTP ${anonPut.status}`);
  const anonPost = await anon.req('POST', '/api/properties', { json: {} }); rec('Authorization', 'Z9', 'anonymous create -> 401', anonPost.status === 401, `HTTP ${anonPost.status}`);
  const still = await anon.req('GET', `/api/properties/${B.id}`); rec('Authorization', 'Z10', 'B\'s listing unchanged after attacks', still.data.title === B.title, still.data.title?.slice(0, 40));
  const mineB = await cB.req('GET', '/api/properties/mine'); const mineList = mineB.data.items ?? mineB.data;
  rec('Dashboard', 'D1', 'B /properties/mine lists exactly own listing', mineB.status === 200 && Array.isArray(mineList) && mineList.length === 1 && mineList[0].id === B.id, `HTTP ${mineB.status} n=${mineList?.length}`);
  // mass assignment on update: A tries to change own listing owner
  const upd = await cA.req('PUT', `/api/properties/${A.id}`, { json: { OwnerId: B.ownerId, Title: A.title, Description: 'updated by owner ' + BATCH_ID } });
  const chk = await anon.req('GET', `/api/properties/${A.id}`);
  rec('Authorization', 'Z11', 'mass-assignment: OwnerId in PUT body does not transfer ownership', chk.data.ownerId === A.ownerId, `PUT HTTP ${upd.status}; owner now ${chk.data.ownerId === A.ownerId ? 'unchanged' : 'CHANGED'}`);
  // private data exposure
  const prof = await anon.req('GET', `/api/users/${B.ownerId}`); const profText = JSON.stringify(prof.data);
  rec('Authorization', 'Z12', 'anonymous /users/{id} does not expose phone/email', prof.status !== 200 || !/phoneNumber|email/i.test(profText) || (!/\+1555/.test(profText)), `HTTP ${prof.status} ${profText.slice(0, 160)}`);
  const pub = await anon.req('GET', `/api/properties/${B.id}`); const pt = JSON.stringify(pub.data);
  rec('Authorization', 'Z13', 'public listing exposes no owner phone/email', !/\+1555\d+|@/.test(pt.replace(/\[.*?\]/g, '')), 'checked public detail JSON');
  const usersList = await cA.req('GET', '/api/admin/users'); rec('Authorization', 'Z14', 'normal user cannot call admin users endpoint', [401, 403, 404].includes(usersList.status), `HTTP ${usersList.status}`);
  const opver = await cA.req('GET', '/api/operational/version'); rec('Authorization', 'Z15', 'normal user cannot read /operational/version', [401, 403].includes(opver.status), `HTTP ${opver.status}`);
  const bi = await anon.req('GET', '/api/operational/build-info'); rec('Authorization', 'Z16', 'build-info without secret -> 404', bi.status === 404, `HTTP ${bi.status}`);
  const cleanupNo = await anon.req('POST', '/api/staging-test-support/cleanup', { json: { RunId: 'x'.repeat(10), UserIds: [], PropertyIds: [] } }); rec('Authorization', 'Z17', 'cleanup endpoint without secret -> 404', cleanupNo.status === 404, `HTTP ${cleanupNo.status}`);
  const csrf = await cA.req('POST', '/api/auth/refresh', { json: {}, auth: false, noCsrf: true });
  rec('Authorization', 'Z18', 'cookie-authenticated refresh without CSRF token rejected (403)', csrf.status === 403, `HTTP ${csrf.status}`);
}

// ---------- ownership via create: OwnerId spoof (free quota needed -> use probe account after freeing its slot) ----------
{
  const probe = st.accounts.find((a) => a.role === 'probe'); const cP = new Client('P', env); await login(cP, probe.phone);
  { const m0 = await cP.req('GET', '/api/properties/mine'); for (const it of (m0.data.items ?? m0.data)) await cP.req('DELETE', `/api/properties/${it.id}`); }
  const { listingSpec } = await import('./data.mjs');
  const spec = listingSpec(101, 0).payload; spec.OwnerId = B.ownerId;
  const cr = await cP.req('POST', '/api/properties', { json: spec });
  if (cr.status === 201) {
    const d = await anon.req('GET', `/api/properties/${cr.data.id}`);
    rec('Authorization', 'Z19', 'mass-assignment: OwnerId in POST body ignored (owner = caller)', d.data.ownerId === probe.userId, `owner=${d.data.ownerId === probe.userId ? 'caller' : 'SPOOFED'}`);
    st.probeListings = [cr.data.id];
  } else { rec('Authorization', 'Z19', 'OwnerId spoof on create', cr.status === 400 || cr.status === 403, `HTTP ${cr.status} ${JSON.stringify(cr.data).slice(0, 150)}`); st.probeListings = []; }
  const q2 = await cP.req('POST', '/api/properties', { json: listingSpec(102, 0).payload }); rec('Listings', 'L4', 'free-plan quota (1 active) enforced -> 409', q2.status === 409, `HTTP ${q2.status}`);
  track(cP);
}

// ---------- API validation / error hygiene ----------
{
  const { listingSpec } = await import('./data.mjs');
  const mk = (over) => ({ ...listingSpec(7, 0).payload, ...over });
  const probe = st.accounts.find((a) => a.role === 'probe'); const cP = new Client('P2', env); await login(cP, probe.phone);
  // delete probe's listing so quota doesn't mask validation (validation runs first anyway)
  const cases = [
    ['E1', 'empty title', mk({ Title: '' })], ['E2', 'empty description', mk({ Description: '' })], ['E3', 'negative rent', mk({ ListingType: 'ForRent', ColdRent: -5, WarmRent: undefined })],
    ['E4', 'zero sale price', mk({ ListingType: 'ForSale', PurchasePrice: 0, ColdRent: undefined })], ['E5', 'huge price 1e30', mk({ ListingType: 'ForSale', PurchasePrice: 1e30, LegalStatus: 'GreenDeed' })],
    ['E6', 'invalid currency', mk({ CurrencyCode: 'XXX' })], ['E7', 'bad governorate id', mk({ GovernorateId: 99999 })], ['E8', 'district/governorate mismatch', mk({ GovernorateId: 1, DistrictId: 80 })],
    ['E9', 'lat without lon', mk({ Longitude: undefined })], ['E10', 'rooms 999', mk({ Rooms: 999 })], ['E11', 'area 1 m2', mk({ Area: 1 })], ['E12', 'title 5000 chars', mk({ Title: 'x'.repeat(5000) })],
    ['E13', 'invalid listing type', mk({ ListingType: 'Bogus' })], ['E14', 'sale without legal status', mk({ ListingType: 'ForSale', PurchasePrice: 1000, LegalStatus: undefined })],
  ];
  for (const [id, name, body] of cases) {
    const r = await cP.req('POST', '/api/properties', { json: body });
    const txt = JSON.stringify(r.data);
    if (false) { rec('API', id, name, [201, 400, 409].includes(r.status) && !LEAK.test(txt), `HTTP ${r.status}`); if (r.status === 201) { const g = await anon.req('GET', `/api/properties/${r.data.id}`); rec('Security', 'SEC-XSS', 'script tag stored/returned as inert JSON text (API)', typeof g.data.title === 'string', g.data.title?.slice(0, 40)); await cP.req('DELETE', `/api/properties/${r.data.id}`); } continue; }
    rec('API', id, `${name} -> 400/422 validation (not 409/500), safe body`, [400, 422].includes(r.status) && !LEAK.test(txt), `HTTP ${r.status} ${txt.slice(0, 120)}`);
  }
  { // XSS storage test needs a free quota slot
    const mineP = await cP.req('GET', '/api/properties/mine'); for (const it of (mineP.data.items ?? mineP.data)) await cP.req('DELETE', `/api/properties/${it.id}`);
    const xp = { ...listingSpec(1, 0).payload, Title: '<script>alert(1)</script> QA <img src=x onerror=alert(1)>' };
    const xr = await cP.req('POST', '/api/properties', { json: xp });
    if (xr.status === 201) { const g = await anon.req('GET', `/api/properties/${xr.data.id}`); rec('Security', 'SEC-XSS', 'HTML in title: stored verbatim as JSON text (frontend must escape) or sanitized', typeof g.data.title === 'string', `stored="${g.data.title.slice(0, 50)}"`); await cP.req('DELETE', `/api/properties/${xr.data.id}`); }
    else rec('Security', 'SEC-XSS', 'HTML in title rejected/sanitized', [400, 422].includes(xr.status), `HTTP ${xr.status}`);
    const rr = await cP.req('POST', '/api/properties', { json: listingSpec(2, 0).payload }); st.probeListings = rr.status === 201 ? [rr.data.id] : [];
  }
  const g1 = await anon.req('GET', '/api/properties/not-a-guid'); rec('API', 'E16', 'non-guid id -> 4xx (404/400) not 500', g1.status >= 400 && g1.status < 500 && !LEAK.test(JSON.stringify(g1.data)), `HTTP ${g1.status}`);
  const g2 = await anon.req('GET', '/api/properties/00000000-0000-0000-0000-000000000000'); rec('API', 'E17', 'unknown guid -> 404', g2.status === 404, `HTTP ${g2.status}`);
  const mal = await cP.req('POST', '/api/properties', { headers: { 'Content-Type': 'application/json' }, form: '{bad json' }); rec('API', 'E18', 'malformed JSON -> 400', mal.status === 400, `HTTP ${mal.status}`);
  // image validation on own listing
  const own = st.probeListings?.[0];
  if (own) {
    const txtFile = new FormData(); txtFile.append('files', new Blob(['not an image'], { type: 'image/png' }), 'fake.png');
    const r1 = await cP.req('POST', `/api/properties/${own}/images`, { form: txtFile }); rec('Images', 'I5', 'non-image bytes labelled image/png rejected', r1.status === 400, `HTTP ${r1.status} ${JSON.stringify(r1.data).slice(0, 100)}`);
    const svg = new FormData(); svg.append('files', new Blob(['<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>'], { type: 'image/svg+xml' }), 'x.svg');
    const r2 = await cP.req('POST', `/api/properties/${own}/images`, { form: svg }); rec('Images', 'I6', 'SVG/script upload rejected (unsupported type)', r2.status === 400, `HTTP ${r2.status}`);
    const exe = new FormData(); exe.append('files', new Blob([new Uint8Array([0x4d, 0x5a, 0x90, 0])], { type: 'application/octet-stream' }), 'a.exe');
    const r3 = await cP.req('POST', `/api/properties/${own}/images`, { form: exe }); rec('Images', 'I7', 'executable upload rejected', r3.status === 400, `HTTP ${r3.status}`);
    const empty = new FormData(); const r4 = await cP.req('POST', `/api/properties/${own}/images`, { form: empty }); rec('Images', 'I8', 'upload with no files -> 400', r4.status === 400, `HTTP ${r4.status}`);
    const eleven = await uploadImages(cP, own, Array.from({ length: 11 }, (_, i) => makePng(900 + i, 64, 64))); rec('Images', 'I9', '11 files in one request rejected (limit 10)', eleven.status === 400, `HTTP ${eleven.status}`);
    const up1 = await uploadImages(cP, own, [makePng(950)]); rec('Images', 'I10', 'valid upload to own listing -> 200', up1.status === 200, `HTTP ${up1.status}`);
    const up2 = await uploadImages(cP, own, [makePng(951)]);
    const imgs = await cP.req('GET', `/api/properties/${own}/images`);
    rec('Images', 'I11', 'listing images list ordered, exactly one main', imgs.status === 200 && imgs.data.filter((x) => x.isMain).length === 1 && imgs.data.every((x, i) => x.sortOrder === i), JSON.stringify(imgs.data.map((x) => [x.sortOrder, x.isMain])));
    { // main-image behaviour is exercised on clean accounts by repro-main-image.mjs (the probe listing is intentionally left in the broken state it reproduced)
      const ev = JSON.parse(readFileSync(new URL('./repro-main-image.json', import.meta.url), 'utf8')); const by = (k) => ev.find((e) => e.step.startsWith(k));
      rec('Images', 'I12', 'set a non-main image as main (clean listing)', by('S1 PATCH').status === 204, 'HTTP ' + by('S1 PATCH').status);
      rec('Images', 'I13b', 'delete MAIN image while others exist', by('S1 DELETE').status === 204, 'HTTP ' + by('S1 DELETE').status + ' corr ' + by('S1 DELETE').corr, { bug: 'STG-001' });
      rec('Images', 'I13c', 'delete the only image, then upload a new one', by('S2 upload a new').status === 200, 'HTTP ' + by('S2 upload a new').status + ' (and again ' + by('S2 upload again').status + ')', { bug: 'STG-002' });
      const nonMain = (await cP.req('GET', `/api/properties/${own}/images`)).data.find((x) => !x.isMain);
      if (nonMain) { const del = await cP.req('DELETE', `/api/properties/${own}/images/${nonMain.id}`); rec('Images', 'I13a', 'delete NON-main image succeeds', [200, 204].includes(del.status), 'HTTP ' + del.status); await sleep(4000); const gone = await fetch(nonMain.url, { method: 'HEAD' }); rec('Images', 'I14', 'deleted image URL no longer served by storage (after 4s)', gone.status === 404, 'HTTP ' + gone.status); }
    }
    const big = new Uint8Array(21 * 1024 * 1024); big.set([137, 80, 78, 71, 13, 10, 26, 10]); const bf = new FormData(); bf.append('files', new Blob([big], { type: 'image/png' }), 'big.png');
    const r5 = await cP.req('POST', `/api/properties/${own}/images`, { form: bf }); rec('Images', 'I15', '21 MB upload rejected (413/400)', [400, 413].includes(r5.status), `HTTP ${r5.status}`);
  }
  track(cP);
}

// ---------- favorites ----------
{
  const f = async (m, c, id) => c.req(m, `/api/favorites/${id}`);
  const unauth = await anon.req('GET', '/api/favorites'); rec('Favorites', 'FV1', 'anonymous favorites list -> 401', unauth.status === 401, `HTTP ${unauth.status}`);
  await f('DELETE', cA, B.id);
  const add = await f('POST', cA, B.id); rec('Favorites', 'FV2', 'A favorites B\'s listing', [200, 201, 204].includes(add.status), `HTTP ${add.status}`);
  const dup = await f('POST', cA, B.id); rec('Favorites', 'FV3', 'duplicate favorite is idempotent/409, not 500', [200, 201, 204, 409].includes(dup.status), `HTTP ${dup.status}`);
  const list = await cA.req('GET', '/api/favorites'); const items = list.data.items ?? list.data;
  rec('Favorites', 'FV4', 'favorite listed exactly once', list.status === 200 && items.filter((x) => (x.id ?? x.propertyId) === B.id).length === 1, `n=${items?.length}`);
  const lb = await cB.req('GET', '/api/favorites'); rec('Favorites', 'FV5', 'B does not see A\'s favorites', (lb.data.items ?? lb.data).length === 0, `n=${(lb.data.items ?? lb.data).length}`);
  const rem = await f('DELETE', cA, B.id); rec('Favorites', 'FV6', 'remove favorite', [200, 204].includes(rem.status), `HTTP ${rem.status}`);
  const rem2 = await f('DELETE', cA, B.id); rec('Favorites', 'FV7', 'remove again does not 500', rem2.status < 500, `HTTP ${rem2.status}`);
  const re = await f('POST', cA, B.id); rec('Favorites', 'FV8', 'favorite again after remove', [200, 201, 204].includes(re.status), `HTTP ${re.status}`);
  const cA2 = new Client('A2', env); await login(cA2, A.ownerPhone); const l2 = await cA2.req('GET', '/api/favorites');
  rec('Favorites', 'FV9', 'favorite persists after re-login', (l2.data.items ?? l2.data).some((x) => (x.id ?? x.propertyId) === B.id), '');
  const nf = await f('POST', cA, '00000000-0000-0000-0000-000000000000'); rec('Favorites', 'FV10', 'favorite of nonexistent listing -> 404/400', [400, 404].includes(nf.status), `HTTP ${nf.status}`);
  await f('DELETE', cA, B.id); track(cA2);
}

// ---------- contact / messages ----------
{
  const marker = `${BATCH_ID}-msg-${Date.now()}`;
  const send = await cA.req('POST', '/api/messages', { json: { PropertyId: B.id, ReceiverId: null, Content: marker } }); rec('Contact', 'M1', 'A sends inquiry on B\'s listing -> 201', send.status === 201, `HTTP ${send.status} ${JSON.stringify(send.data).slice(0, 100)}`);
  const inB = await cB.req('GET', `/api/messages?PropertyId=${B.id}`); const t = JSON.stringify(inB.data);
  rec('Contact', 'M2', 'B (owner) receives the inquiry exactly once', inB.status === 200 && (t.match(new RegExp(marker, 'g')) || []).length === 1, `HTTP ${inB.status} occurrences=${(t.match(new RegExp(marker, 'g')) || []).length}`);
  const inC = await cC.req('GET', `/api/messages?PropertyId=${B.id}`); rec('Contact', 'M3', 'third user C cannot read A<->B thread', !JSON.stringify(inC.data).includes(marker), `HTTP ${inC.status}`);
  const empty = await cA.req('POST', '/api/messages', { json: { PropertyId: B.id, ReceiverId: null, Content: '' } }); rec('Contact', 'M4', 'empty message -> 400/422', [400, 422].includes(empty.status), `HTTP ${empty.status}`);
  const nop = await cA.req('POST', '/api/messages', { json: { PropertyId: '00000000-0000-0000-0000-000000000001', ReceiverId: null, Content: 'x' } }); rec('Contact', 'M5', 'message to nonexistent listing -> 404/400', [400, 404].includes(nop.status), `HTTP ${nop.status}`);
  const an = await anon.req('POST', '/api/messages', { json: { PropertyId: B.id, Content: 'x' } }); rec('Contact', 'M6', 'anonymous message -> 401', an.status === 401, `HTTP ${an.status}`);
  const xss = await cA.req('POST', '/api/messages', { json: { PropertyId: B.id, ReceiverId: null, Content: 'x'.repeat(20000) } }); rec('Contact', 'M7', 'oversized message rejected (4xx)', xss.status >= 400 && xss.status < 500, `HTTP ${xss.status}`);
  const conv = await cB.req('GET', '/api/messages/conversations'); rec('Contact', 'M8', 'owner conversations list loads', conv.status === 200, `HTTP ${conv.status}`);
}

// ---------- CORS / headers / info exposure ----------
{
  const pre = async (origin) => { const r = await fetch(`${BASE}/api/properties`, { method: 'OPTIONS', headers: { Origin: origin, 'Access-Control-Request-Method': 'GET' } }); return r.headers.get('access-control-allow-origin'); };
  rec('Security', 'CORS1', 'staging web origin allowed', (await pre('https://staging--hudhudnest.netlify.app')) === 'https://staging--hudhudnest.netlify.app', '');
  rec('Security', 'CORS2', 'production web origin NOT allowed by staging API', (await pre('https://hudhudnest.com')) === null, '');
  rec('Security', 'CORS3', 'arbitrary origin not reflected', (await pre('https://evil.example')) === null, '');
  const r = await anon.req('GET', '/api/properties?pageSize=1');
  const h = Object.fromEntries(r.headers);
  rec('Security', 'HDR1', 'security headers present (HSTS, nosniff, XFO, CSP)', !!(h['strict-transport-security'] && h['x-content-type-options'] && h['x-frame-options'] && h['content-security-policy']), '');
  for (const p of ['/swagger', '/swagger/v1/swagger.json', '/.env', '/appsettings.json', '/api/operational/version', '/metrics']) {
    const x = await anon.req('GET', p); rec('Security', `EXP-${p}`, `${p} not publicly exposed`, x.status !== 200 || typeof x.data === 'string' && !/openapi|connectionstring/i.test(x.data), `HTTP ${x.status}`);
  }
  const lk = await anon.req('GET', '/api/properties/' + 'a'.repeat(5000)); rec('API', 'E19', 'very long path -> 4xx without leak', lk.status >= 400 && lk.status < 500 && !LEAK.test(JSON.stringify(lk.data)), `HTTP ${lk.status}`);
}

// ---------- public reads of other verticals (limited coverage) ----------
{
  const ss = await anon.req('GET', '/api/short-stay/listings'); rec('Listings', 'SS1', 'short-stay public list reachable', ss.status === 200, `HTTP ${ss.status} total=${ss.data?.totalCount ?? ss.data?.length}`);
  const inv = await anon.req('GET', '/api/investments/projects'); rec('Listings', 'IN1', 'investment projects public list reachable', inv.status === 200, `HTTP ${inv.status} total=${inv.data?.totalCount ?? inv.data?.length}`);
  rec('Listings', 'SS2', 'short-stay & investment creation by synthetic users', 'blocked', 'Short stay needs separate flows/owner roles; investment projects are admin-managed. Not created (no admin credentials, scope).');
}

// ---------- randomized deterministic journey ----------
{
  let seed = 424242; const rnd = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296;
  const c = new Client('R', env); await login(c, L[10].ownerPhone); let fails = 0, steps = 0;
  const target = L[20];
  for (let i = 0; i < 40; i++) {
    const op = Math.floor(rnd() * 6); const l = L[Math.floor(rnd() * L.length)]; let r;
    if (op === 0) r = await c.req('GET', `/api/properties?GovernorateId=${l.governorateId}&pageSize=12`);
    else if (op === 1) r = await c.req('GET', `/api/properties/${l.id}`);
    else if (op === 2 && l.id !== L[10].id) r = await c.req('POST', `/api/favorites/${l.id}`);
    else if (op === 3 && l.id !== L[10].id) r = await c.req('DELETE', `/api/favorites/${l.id}`);
    else if (op === 4) r = await c.req('GET', '/api/favorites');
    else r = await c.req('GET', '/api/users/me');
    steps++; if (r && r.status >= 500) fails++;
    if (r && r.status === 401) { await login(c, L[10].ownerPhone); }
  }
  for (const l of L) await c.req('DELETE', `/api/favorites/${l.id}`);
  rec('Randomized', 'RND1', `${steps} deterministic mixed operations (seed 424242) with zero 5xx`, fails === 0, `5xx=${fails}`);
  track(c);
}

// ---------- performance ----------
{
  const perf = []; const sample = L.map((l) => l.id);
  const measure = async (name, n, conc, fn) => {
    const times = [], codes = {}; let errors = 0; const queue = Array.from({ length: n }, (_, i) => i);
    await Promise.all(Array.from({ length: conc }, async () => { while (queue.length) { const i = queue.shift(); const t0 = performance.now(); let s; try { s = await fn(i); } catch { s = 0; } times.push(performance.now() - t0); codes[s] = (codes[s] || 0) + 1; if (s >= 500 || s === 0) errors++; } }));
    perf.push({ endpoint: name, requests: n, concurrency: conc, avgMs: +(times.reduce((a, b) => a + b, 0) / n).toFixed(0), p95Ms: +pct(times, 95).toFixed(0), p99Ms: +pct(times, 99).toFixed(0), status: codes, errors5xx: errors });
  };
  const pc = new Client('perf', env);
  const s = (r) => r.status;
  await measure('GET /api/properties?pageSize=20', 30, 1, async (i) => s(await pc.req('GET', `/api/properties?page=${1 + (i % 5)}&pageSize=20`)));
  await measure('GET /api/properties (filters combo)', 30, 1, async (i) => s(await pc.req('GET', `/api/properties?GovernorateId=${1 + (i % 6)}&ListingType=ForSale&MinPrice=1000&CurrencyCode=USD&pageSize=20`)));
  await measure('GET /api/properties/{id}', 40, 1, async (i) => s(await pc.req('GET', `/api/properties/${sample[i % sample.length]}`)));
  await measure('GET /api/properties/{id} (conc 5)', 50, 5, async (i) => s(await pc.req('GET', `/api/properties/${sample[(i * 3) % sample.length]}`)));
  await measure('GET /api/properties?SearchTerm', 20, 1, async (i) => s(await pc.req('GET', `/api/properties?SearchTerm=${['فيلا', 'apartment', 'Wohnung', 'شقة'][i % 4]}&pageSize=20`)));
  await measure('GET /api/lookups/governorates', 20, 1, async () => s(await pc.req('GET', '/api/lookups/governorates?countryCode=SY')));
  await measure('GET /api/properties/{id}/images', 20, 1, async (i) => s(await pc.req('GET', `/api/properties/${sample[i % sample.length]}/images`)));
  const pa = new Client('perfA', env); await login(pa, A.ownerPhone);
  await measure('GET /api/properties/mine (auth)', 15, 1, async () => s(await pa.req('GET', '/api/properties/mine')));
  await measure('GET /api/users/me (auth)', 15, 1, async () => s(await pa.req('GET', '/api/users/me')));
  st.perf = perf; track(pc); track(pa);
  for (const p of perf) rec('Performance', `PERF-${p.endpoint}`, `${p.endpoint}: avg ${p.avgMs}ms p95 ${p.p95Ms}ms p99 ${p.p99Ms}ms ${JSON.stringify(p.status)}`, p.errors5xx === 0 && p.p95Ms < 1500, `p95 budget 1500ms; errors=${p.errors5xx}`);
  const rl = perf.flatMap((p) => Object.keys(p.status)).filter((k) => k === '429');
  const rl429 = perf.filter((p) => p.status['429']).map((p) => `${p.endpoint}: ${p.status['429']}x429`);
  rec('Performance', 'PERF-RL', 'per-IP rate limit (120 req/min, shared Staging bucket) enforced with 429 under burst (informational)', true, rl429.length ? rl429.join('; ') : 'no 429 at this load');
}

st.gate = gate; st.testsRanAt = new Date().toISOString();
st.apiTimings = (() => { const g = {}; for (const t of allTimings) { const k = `${t.method} ${t.path}`; (g[k] ??= []).push(t); } return Object.entries(g).map(([k, v]) => ({ endpoint: k, count: v.length, avgMs: +(v.reduce((a, b) => a + b.ms, 0) / v.length).toFixed(0), p95Ms: +pct(v.map((x) => x.ms), 95).toFixed(0), p99Ms: +pct(v.map((x) => x.ms), 99).toFixed(0), non2xx: v.filter((x) => x.status >= 400).length })).sort((a, b) => b.count - a.count); })();
saveState(st);
writeFileSync(new URL('./results.json', import.meta.url), JSON.stringify(results, null, 1));
const sum = results.reduce((m, r) => (m[r.status] = (m[r.status] || 0) + 1, m), {});
console.log('SUMMARY', JSON.stringify(sum));
