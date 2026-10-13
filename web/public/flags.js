// Country flags for competitions and matches. Betfair gives codes only (competition region = ISO alpha-3, event country = alpha-2);
// the pictures are the MIT-licensed flag-icons set (see flags/LICENSE-flag-icons.txt).
let iso3 = {};
try { iso3 = await (await fetch('flags/iso3.json')).json(); } catch { /* no flags then */ }
const have = new Set();   // flag files known to exist (checked lazily via the <img> error handler)

/** Flag file code for a competition (region) or match (country code), or null. */
export function flagCode(region, countryCode, name = '') {
  let code = null;
  if (countryCode) { code = String(countryCode).toLowerCase(); if (code === 'uk') code = 'gb'; }
  else if (region) {
    if (/^international$/i.test(region)) return /UEFA|European/i.test(name) ? 'eu' : 'un';
    code = iso3[String(region).toUpperCase()] ?? null;
  }
  if (code === 'gb') {   // the UK plays as four nations
    if (/^English/i.test(name)) code = 'gb-eng'; else if (/^Scottish/i.test(name)) code = 'gb-sct';
    else if (/^Welsh/i.test(name)) code = 'gb-wls'; else if (/^Northern Irish/i.test(name)) code = 'gb-nir';
  }
  return code;
}

/** An <img> for the flag, or an empty spacer so labels stay aligned. */
export function flagImg(code) {
  const img = document.createElement('img');
  img.className = 'flag';
  if (code) { img.src = `flags/${code}.png`; img.alt = code; img.onerror = () => { img.style.visibility = 'hidden'; }; }
  else img.style.visibility = 'hidden';
  return img;
}
