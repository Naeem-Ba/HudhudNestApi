// PHASE 1: create 100 synthetic accounts + 100 listings + images through the real API. Resumable (state.json).
import { loadEnv, safetyGate, Client, registerUser, loadState, saveState, uploadImages, makePng, sleep, BATCH_ID } from './lib.mjs';
import { listingSpec, setDistrictNames } from './data.mjs';

const env = loadEnv();
const gate = await safetyGate(env);
console.log('GATE OK', gate.environment, gate.commitSha.slice(0, 7));

// district names for descriptions (public lookup)
const probeC = new Client('lk', env);
const names = {};
for (const g of [1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 14]) {
  const r = await probeC.req('GET', `/api/lookups/districts?governorateId=${g}`);
  for (const d of r.data) names[d.id] = { ar: d.nameAr, en: d.nameEn };
}
setDistrictNames(names);

const st = loadState();
st.batchId = BATCH_ID; st.gate = gate; st.errors ??= []; st.timings ??= [];
const total = Number(process.argv[2] ?? 100);
const CONC = 3;
let next = 1;
const phoneFor = (i) => `${env.prefix}${String(i).padStart(3, '0')}`;
const done = (i) => st.listings.some((l) => l.index === i && l.id);
const err = (i, step, res) => { const e = { index: i, step, status: res?.status, body: JSON.stringify(res?.data ?? '').slice(0, 300) }; st.errors.push(e); console.log('FAIL', JSON.stringify(e)); };

async function one(i) {
  if (done(i)) return;
  const phone = phoneFor(i);
  const c = new Client(`u${i}`, env);
  let acct = st.accounts.find((a) => a.phone === phone);
  if (!acct || !acct.userId) {
    const reg = await registerUser(c, phone, 'QA', `${BATCH_ID}-${String(i).padStart(3, '0')}`);
    if (!reg.ok) { err(i, `register:${reg.step ?? 'me'}`, reg.res ?? reg.me); st.timings.push(...c.timings); return; }
    acct = { phone, userId: reg.userId, index: i, role: 'owner' };
    st.accounts.push(acct);
  } else {
    const { login } = await import('./lib.mjs'); const l = await login(c, phone);
    if (l.status !== 200) { err(i, 'login', l); return; }
  }
  const plan = await c.req('POST', '/api/users/me/plan', { json: { Tier: 'free' } });
  if (![200, 204].includes(plan.status)) { err(i, 'plan', plan); return; }
  const spec = listingSpec(i, i);
  const cr = await c.req('POST', '/api/properties', { json: spec.payload });
  if (cr.status !== 201) { err(i, 'create', cr); st.timings.push(...c.timings); return; }
  const id = cr.data.id ?? cr.data.Id;
  const rec = { index: i, id, ownerPhone: phone, ownerId: acct.userId, lang: spec.lang, governorateId: spec.governorateId, districtId: spec.districtId,
    propertyTypeId: spec.propertyTypeId, propertyType: spec.propertyType, listingType: spec.listingType, currency: spec.currency, title: spec.payload.Title,
    coldRent: spec.payload.ColdRent ?? null, purchasePrice: spec.payload.PurchasePrice ?? null, rooms: spec.payload.Rooms ?? null, area: spec.payload.Area,
    furnishing: spec.payload.FurnishingStatus ?? null, createStatus: cr.data.status, wantedImages: spec.imageCount, images: [] };
  let n = 0;
  while (n < spec.imageCount) {
    const k = Math.min(10, spec.imageCount - n);
    const pngs = Array.from({ length: k }, (_, j) => makePng(i * 100 + n + j));
    const up = await uploadImages(c, id, pngs, 'image/png', `qa${i}`);
    if (up.status !== 200) { err(i, `upload@${n}`, up); break; }
    rec.images.push(...up.data.map((x) => ({ id: x.id, url: x.url, isMain: x.isMain })));
    n += k;
  }
  st.listings.push(rec);
  st.timings.push(...c.timings);
  console.log(`OK #${i} ${spec.propertyType} ${spec.listingType} gov${spec.governorateId} img ${rec.images.length}/${spec.imageCount}`);
}

async function worker() {
  while (true) {
    const i = next++; if (i > total) return;
    try { await one(i); } catch (e) { err(i, 'exception', { status: 0, data: String(e) }); }
    if (i % 5 === 0) saveState(st);
  }
}
await Promise.all(Array.from({ length: CONC }, worker));
saveState(st);
console.log(`DONE listings=${st.listings.length} accounts=${st.accounts.length} errors=${st.errors.length}`);
