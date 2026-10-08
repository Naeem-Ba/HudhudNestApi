// PHASE 7 cleanup (NOT RUN automatically). Deletes ONLY listings owned by accounts created by this batch.
// Each account is verified (last name contains the batch id) before anything is deleted. Runs the Staging safety gate first.
// Usage: STAGING_ENV_FILE=<secrets file> node cleanup.mjs [--yes]
import { loadEnv, safetyGate, Client, login, loadState, saveState, BATCH_ID } from './lib.mjs';
const env = loadEnv(); await safetyGate(env);
if (!process.argv.includes('--yes')) { console.log('Dry run. Re-run with --yes to delete.'); }
const st = loadState(); const removed = []; let skipped = 0;
for (const a of st.accounts) {
  const c = new Client('cleanup', env); const l = await login(c, a.phone);
  if (l.status !== 200) { console.log('login failed for account index', a.index, l.status); skipped++; continue; }
  const me = await c.req('GET', '/api/users/me');
  if (!String(me.data.lastName ?? '').includes(BATCH_ID)) { console.log('SKIP (not a batch account)', a.index); skipped++; continue; }
  const mine = await c.req('GET', '/api/properties/mine'); const items = mine.data.items ?? mine.data;
  for (const it of items) {
    if (process.argv.includes('--yes')) { const d = await c.req('DELETE', `/api/properties/${it.id}`); removed.push({ id: it.id, status: d.status }); }
    else removed.push({ id: it.id, status: 'dry-run' });
  }
}
st.cleanup = { ranAt: new Date().toISOString(), listings: removed.length, skipped, dryRun: !process.argv.includes('--yes') }; saveState(st);
console.log(JSON.stringify(st.cleanup));
