import { loadEnv, safetyGate, Client } from './lib.mjs';
const env = loadEnv(); await safetyGate(env);
const c = new Client('r', env);
for (const e of ['LegalStatusType', 'FurnishingStatus', 'RentalDurationType', 'AreaUnit', 'PropertyCondition', 'ListingType']) {
  const r = await c.req('GET', `/api/enums/${e}`); console.log(e, r.status, JSON.stringify(r.data).slice(0, 500));
}
const d = {};
for (const g of [1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 14]) {
  const r = await c.req('GET', `/api/lookups/districts?governorateId=${g}`);
  d[g] = Array.isArray(r.data) ? r.data.map((x) => [x.id, x.nameAr, x.nameEn]) : r.status;
}
console.log(JSON.stringify(d));
