# Area prices and the price estimator: methodology (growth engine P6)

**What it is:** a statistical range built from the asking prices of listings that are on the market now on
HudhudNest. **What it is not:** a valuation, an appraisal or a sale price. Asking prices are usually above
final prices, and the site only sees its own listings.

## Data
- Listings that are published, Available or Reserved, and not expired (`Property.OnMarket`).
- Need a governorate, an area in m², and a price above zero.
- Sale: `PurchasePrice`. Rent: monthly `ColdRent`, or `WarmRent` when no cold rent is given (warm rent
  includes some charges, so rent ranges can lean high). "For rent and sale" listings count in both.
- Short-stay nightly prices are **never** mixed in.
- All prices are converted to USD at the site's active currency rates. A listing whose currency has no rate is
  left out (never guessed).

## Cleaning, per group
A group is one governorate (or one district) × sale/rent × (all types, or one property type).
1. Drop sizes below 15 m² or above 5,000 m² (typos and land plots).
2. Collapse duplicates: same owner, same district, same rounded area and rounded price count once (one flat
   posted twice).
3. Price per m², then drop outliers outside the Tukey fences [P25 − 1.5·IQR, P75 + 1.5·IQR].
4. Show nothing unless at least **8 listings** remain **from at least 3 different owners** (one agency cannot
   set an area's price alone).

## Output
- P25, median and P75 per m² (linear interpolation between ranks).
- The estimate for a given area is P25·area … P75·area (the middle half of comparable listings), with the
  median as the middle value. It is rounded to whole currency units.
- Every answer shows: number of listings used, which level it came from (district+type → district →
  governorate+type → governorate; the most specific level with enough data is used and named), the oldest and
  newest listing date, the computation time, and a confidence label:
  - Low: 8–14 listings, Medium: 15–39, High: 40+.
  - One level lower when the middle half is wider than 60 % of the median.
- "Not enough data" (`not_enough_data`), "area out of range" (`area_out_of_range`) and "no rate for this
  currency" (`currency_rate_missing`) are answers, not errors. No row ever means "price 0".

## Operation
- `AreaPriceStatsHostedService` rebuilds the whole `AreaPriceStats` table every 6 hours (first run 2 minutes
  after start), under a database advisory lock, inside one transaction. No HTTP request computes statistics.
- Public endpoints (anonymous, rate limited "public-read", cached 10 minutes per exact query):
  - `GET /api/price-estimates?kind=Sale|Rent&governorateId=&districtId=&propertyTypeId=&area=&currency=`
  - `GET /api/price-estimates/areas?kind=&governorateId=&currency=`
- No owner, listing id or personal field is ever returned.
- All thresholds are settings (`AreaPrices` section): MinSample, MinDistinctOwners, OutlierIqrFactor,
  MinAreaSqm, MaxAreaSqm, MediumConfidenceFrom, HighConfidenceFrom, WideSpreadRatio, RecomputeHours.

## Known limits
- With few listings in most areas at launch, most answers will be "not enough data". That is intended.
- Rooms, floor, condition and age are not used yet: two very different flats of the same size compare equally.
  The range (not a single number) and the confidence label carry this uncertainty.
- Price history (P1) is not used: the statistics are a snapshot of today's market.
