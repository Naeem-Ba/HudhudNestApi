// Clean-room repro of the main-image 500s on two fresh QA accounts (bounded, no loops). Evidence -> repro-main-image.json
import { writeFileSync } from 'node:fs';
import { loadEnv, safetyGate, Client, registerUser, loadState, saveState, uploadImages, makePng, BATCH_ID } from './lib.mjs';
import { listingSpec } from './data.mjs';
const env = loadEnv(); await safetyGate(env); const st = loadState();
const ev = [];
const log = (k, r) => { const o = { step: k, status: r.status, corr: r.headers?.get?.('x-correlation-id') ?? null, body: JSON.stringify(r.data ?? '').slice(0, 120) }; ev.push(o); console.log(JSON.stringify(o)); };
async function mkOwner(idx, specIdx) {
  const c = new Client('r' + idx, env); const phone = `${env.prefix}${idx}`;
  let acct = st.accounts.find((a) => a.phone === phone); let reg;
  if (!acct) { reg = await registerUser(c, phone, 'QA', `${BATCH_ID}-repro-${idx}`); if (!reg.ok) throw new Error('register failed'); acct = { phone, userId: reg.userId, role: 'repro', index: idx }; st.accounts.push(acct); saveState(st); }
  else { const { login } = await import('./lib.mjs'); await login(c, phone); }
  await c.req('POST', '/api/users/me/plan', { json: { Tier: 'free' } });
  const mine = await c.req('GET', '/api/properties/mine'); let id = (mine.data.items ?? mine.data)[0]?.id;
  if (!id) { const cr = await c.req('POST', '/api/properties', { json: listingSpec(specIdx, 0).payload }); id = cr.data.id; }
  st.reproListings ??= {}; st.reproListings[idx] = id; saveState(st);
  return { c, id };
}
const imgs = async (c, id) => (await c.req('GET', `/api/properties/${id}/images`)).data;
// Scenario 1: 3 images -> set a non-main image as main
{ const { c, id } = await mkOwner(901, 11);
  log('S1 upload 3 images', await uploadImages(c, id, [makePng(1), makePng(2), makePng(3)]));
  let list = await imgs(c, id); const t = list.find((x) => !x.isMain);
  log('S1 PATCH setmain on non-main (expect 200/204)', await c.req('PATCH', `/api/properties/${id}/images/${t.id}/setmain`));
  list = await imgs(c, id); const main = list.find((x) => x.isMain);
  log('S1 DELETE main while others exist (expect 204)', await c.req('DELETE', `/api/properties/${id}/images/${main.id}`)); }
// Scenario 2: delete the only image, then upload again
{ const { c, id } = await mkOwner(902, 12);
  log('S2 upload 1 image', await uploadImages(c, id, [makePng(7)]));
  const list = await imgs(c, id);
  log('S2 DELETE the only (main) image (expect 204)', await c.req('DELETE', `/api/properties/${id}/images/${list[0].id}`));
  log('S2 upload a new image afterwards (expect 200)', await uploadImages(c, id, [makePng(8)]));
  log('S2 upload again (expect 200)', await uploadImages(c, id, [makePng(9)])); }
writeFileSync(new URL('./repro-main-image.json', import.meta.url), JSON.stringify(ev, null, 1));
