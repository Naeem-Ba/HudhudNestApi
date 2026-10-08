// Deterministic synthetic listing specs. Reference ids come from the live lookups (recon, 2026-10-08).
import { BATCH_ID } from './lib.mjs';

// governorate id -> [nameAr, nameEn, lat, lon, [districtIds...]]
export const GOV = {
  1: ['دمشق', 'Damascus', 33.5138, 36.2765, [1, 3, 6, 8, 10, 11, 12, 15, 16, 17]],
  2: ['ريف دمشق', 'Rural Damascus', 33.5, 36.4, [19, 20, 22, 23, 27]],
  3: ['حلب', 'Aleppo', 36.2021, 37.1343, [28, 30, 33, 36, 37]],
  4: ['حمص', 'Homs', 34.7324, 36.7137, [38, 39, 40, 41, 44]],
  5: ['حماة', 'Hama', 35.1318, 36.7578, [45, 46, 47, 48]],
  6: ['اللاذقية', 'Latakia', 35.5317, 35.7915, [50, 51, 52, 53]],
  7: ['طرطوس', 'Tartus', 34.8886, 35.8866, [54, 55, 56, 57]],
  9: ['دير الزور', 'Deir ez-Zor', 35.3359, 40.1408, [64, 65, 66]],
  10: ['الرقة', 'Raqqa', 35.9528, 39.0079, [67, 68, 69]],
  11: ['إدلب', 'Idlib', 35.9306, 36.6339, [70, 71, 72, 73]],
  12: ['درعا', 'Daraa', 32.6189, 36.1021, [75, 76, 77]],
  14: ['السويداء', 'As-Suwayda', 32.7094, 36.5694, [80, 81, 82]],
};
export const GOV_IDS = Object.keys(GOV).map(Number);
const DIST_NAMES = {};
export const setDistrictNames = (m) => Object.assign(DIST_NAMES, m);

// property type id -> [code, category, nameAr, nameEn, nameDe]
export const PT = {
  1: ['apartment', 'Residential', 'شقة سكنية', 'apartment', 'Wohnung'],
  2: ['hotel-apartment', 'Residential', 'شقة فندقية', 'hotel apartment', 'Hotelapartment'],
  3: ['villa', 'Residential', 'فيلا', 'villa', 'Villa'],
  4: ['house', 'Residential', 'منزل مستقل', 'detached house', 'Einfamilienhaus'],
  5: ['studio', 'Residential', 'استوديو', 'studio', 'Studio'],
  6: ['residential-land', 'Land', 'أرض سكنية', 'residential plot', 'Wohngrundstück'],
  7: ['agricultural-land', 'Land', 'أرض زراعية', 'agricultural land', 'Ackerland'],
  8: ['industrial-land', 'Land', 'أرض صناعية', 'industrial plot', 'Industriegrundstück'],
  9: ['commercial-land', 'Land', 'أرض تجارية', 'commercial plot', 'Gewerbegrundstück'],
  10: ['shop', 'Commercial', 'محل تجاري', 'shop', 'Ladenlokal'],
  11: ['office', 'Commercial', 'مكتب', 'office', 'Büro'],
  12: ['warehouse', 'Commercial', 'مستودع', 'warehouse', 'Lagerhalle'],
  13: ['hotel', 'Commercial', 'فندق', 'hotel', 'Hotel'],
};
// weighted cycle of 20 so every one of the 13 types is used
const TYPE_CYCLE = [1, 1, 1, 1, 1, 1, 3, 4, 5, 2, 6, 7, 10, 10, 11, 12, 9, 8, 13, 3];
// listing type cycle of 10: rent 4, sale 4, both 2 (lands never rent)
const LT_CYCLE = ['ForRent', 'ForSale', 'ForRent', 'ForSale', 'ForRentAndSale', 'ForRent', 'ForSale', 'ForRentAndSale', 'ForRent', 'ForSale'];
// images per listing: 0 x4, 1 x10, 3 x20, 4 x40, 6 x20, 10 x6 = 410
const IMG_COUNTS = [...Array(4).fill(0), ...Array(10).fill(1), ...Array(20).fill(3), ...Array(40).fill(4), ...Array(20).fill(6), ...Array(6).fill(10)];

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const pick = (r, a) => a[Math.floor(r() * a.length)];

const AR_ADJ = ['واسعة', 'مشرقة', 'هادئة', 'حديثة التشطيب', 'مطلة على المدينة', 'قريبة من الخدمات', 'بإطلالة مفتوحة', 'في موقع مميز'];
const AR_FEAT = ['موقف سيارة', 'مصعد', 'شرفة واسعة', 'تدفئة مركزية', 'طاقة شمسية', 'حديقة صغيرة', 'اشتراك مولدة', 'قرب المدارس والأسواق'];
const EN_ADJ = ['spacious', 'bright', 'quiet', 'recently renovated', 'with open views', 'close to amenities'];
const DE_ADJ = ['geräumige', 'helle', 'ruhige', 'frisch renovierte', 'zentral gelegene', 'gut angebundene'];

export function listingSpec(i, ownerPhoneIndex) {
  const r = rng(1000 + i * 77);
  const gId = GOV_IDS[(i - 1) % GOV_IDS.length];
  const [gAr, gEn, lat, lon, dists] = GOV[gId];
  const districtId = dists[(i * 7) % dists.length];
  const ptId = TYPE_CYCLE[(i - 1) % TYPE_CYCLE.length];
  const [, cat, tAr, tEn, tDe] = PT[ptId];
  const isLand = cat === 'Land', isRes = cat === 'Residential';
  let listingType = LT_CYCLE[(i - 1) % LT_CYCLE.length];
  if (isLand) listingType = 'ForSale';
  if (ptId === 13) listingType = 'ForSale';
  const lang = i % 10 === 3 || i % 10 === 8 ? 'en' : i % 10 === 5 ? 'de' : 'ar';
  const dName = DIST_NAMES[districtId] ?? { ar: '', en: '', de: '' };
  const rooms = isLand ? undefined : ptId === 5 ? 1 : ptId === 13 ? 20 + Math.floor(r() * 20) : 1 + Math.floor(r() * (isRes ? 6 : 4));
  const area = isLand ? Math.round(500 + r() * 8000) : ptId === 12 ? Math.round(300 + r() * 1200) : Math.round(35 + r() * (isRes ? 300 : 150));
  const floor = isLand || ptId === 3 || ptId === 4 ? undefined : Math.floor(r() * 10);
  const currency = i % 17 === 0 ? 'SYP' : i % 9 === 0 ? 'EUR' : 'USD';
  const fx = currency === 'SYP' ? 13000 : 1;
  const rentUsd = Math.round((60 + r() * (isRes ? 700 : 900)) / 5) * 5;
  const saleUsd = Math.round((9000 + r() * (isLand ? 90000 : 260000)) / 500) * 500;
  const coldRent = listingType === 'ForSale' ? undefined : rentUsd * fx;
  const purchasePrice = listingType === 'ForRent' ? undefined : saleUsd * fx;
  const adjAr = pick(r, AR_ADJ), featAr = pick(r, AR_FEAT), feat2 = pick(r, AR_FEAT);
  const purpose = { ForRent: ['للإيجار', 'for rent', 'zu vermieten'], ForSale: ['للبيع', 'for sale', 'zu verkaufen'], ForRentAndSale: ['للبيع أو الإيجار', 'for sale or rent', 'zu verkaufen oder zu vermieten'] }[listingType];
  let title, description;
  const roomTxt = rooms && isRes ? rooms : null;
  if (lang === 'ar') {
    title = `${tAr} ${adjAr} ${purpose[0]} في ${dName.ar || gAr}`;
    description = `${tAr} ${adjAr} في منطقة ${dName.ar || gAr} - ${gAr}، مساحتها ${area} ${isLand ? 'متر مربع' : 'م²'}` +
      (roomTxt ? `، تتكون من ${roomTxt} غرف ومطبخ وحمامين` : '') + `. تتميز بـ ${featAr} و${feat2}. مناسبة ${isLand ? 'للبناء أو الاستثمار' : isRes ? 'للعائلات' : 'للأعمال'} ومعروضة بحالة جيدة. ` +
      `[بيانات اختبار اصطناعية ${BATCH_ID} #${i}]`;
  } else if (lang === 'en') {
    title = `${cap(pick(r, EN_ADJ))} ${tEn} ${purpose[1]} in ${dName.en || gEn}`;
    description = `A ${pick(r, EN_ADJ)} ${tEn} in ${dName.en || gEn}, ${gEn}, offering ${area} m2` + (roomTxt ? ` with ${roomTxt} rooms` : '') +
      `. Well connected, close to shops and services, suitable for ${isLand ? 'development or investment' : isRes ? 'families' : 'business use'}. ` +
      `[Synthetic QA data ${BATCH_ID} #${i}]`;
  } else {
    title = `${cap(pick(r, DE_ADJ))} ${tDe} ${purpose[2]} in ${dName.en || gEn}`;
    description = `Eine ${pick(r, DE_ADJ)} ${tDe} in ${dName.en || gEn}, ${gEn} mit ${area} m2` + (roomTxt ? ` und ${roomTxt} Zimmern` : '') +
      `. Gute Anbindung, Einkaufsmöglichkeiten in der Nähe, geeignet für ${isLand ? 'Bebauung oder Investition' : isRes ? 'Familien' : 'gewerbliche Nutzung'}. ` +
      `[Synthetische QA-Daten ${BATCH_ID} #${i}]`;
  }
  const rent = listingType !== 'ForSale';
  const start = new Date(Date.UTC(2026, 10, 1 + (i % 25)));
  const dur = ['SixMonths', 'OneYear', 'Custom'][i % 3];
  const end = new Date(start.getTime() + (dur === 'SixMonths' ? 182 : dur === 'OneYear' ? 365 : 270) * 86400000);
  const payload = {
    OwnerId: '00000000-0000-0000-0000-000000000000', Title: title, Description: description, ListingType: listingType,
    Street: lang === 'ar' ? `شارع ${pick(r, ['الثورة', 'بغداد', 'الجلاء', 'الحمرا', 'النيل', 'القوتلي'])} رقم ${1 + (i % 60)}` : `QA Street ${1 + (i % 60)}`,
    City: lang === 'ar' ? gAr : gEn, Region: lang === 'ar' ? gAr : gEn, CountryCode: 'SY', PostalCode: String(10000 + i),
    GovernorateId: gId, DistrictId: districtId, PropertyTypeId: ptId,
    Latitude: +(lat + (r() - 0.5) * 0.08).toFixed(6), Longitude: +(lon + (r() - 0.5) * 0.08).toFixed(6),
    ColdRent: coldRent, WarmRent: coldRent ? Math.round(coldRent * 1.08) : undefined, PurchasePrice: purchasePrice,
    Deposit: coldRent ? coldRent * 2 : undefined, AdditionalCosts: coldRent ? Math.round(coldRent * 0.05) : undefined,
    CurrencyCode: currency, Rooms: rooms, Area: area, AreaUnit: 'SquareMeter', Floor: floor,
    HasBalcony: isRes && r() > 0.4, HasElevator: isRes && (floor ?? 0) > 3, HasParkingSpace: r() > 0.5,
    FurnishingStatus: isRes ? ['Unfurnished', 'SemiFurnished', 'Furnished'][i % 3] : undefined,
    Condition: ['New', 'Renovated', 'GoodCondition', 'NeedsRenovation'][i % 4],
    LegalStatus: rent && listingType === 'ForRent' ? undefined : ['GreenDeed', 'YouthHousing', 'Shares', 'Notary', 'CourtJudgment', 'InheritanceDocument'][i % 6],
    HeatingType: isRes ? pick(r, ['Gas', 'Electric', 'Central']) : undefined,
    AvailableFrom: start.toISOString(),
    AmenityIds: [],
  };
  if (rent) { payload.RentalStartDate = start.toISOString().slice(0, 10); payload.RentalEndDate = end.toISOString().slice(0, 10); payload.RentalDurationType = dur; }
  return {
    index: i, ownerIndex: ownerPhoneIndex, lang, governorateId: gId, districtId, propertyTypeId: ptId, propertyType: PT[ptId][0],
    listingType, currency, imageCount: IMG_COUNTS[(i * 37) % IMG_COUNTS.length], payload,
  };
}
const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);
export const IMG_COUNT_TOTAL = IMG_COUNTS.reduce((a, b) => a + b, 0);
