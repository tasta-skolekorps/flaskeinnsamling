// Shared client for the flaskeinnsamling API. Pages render their embedded data first
// and re-render with API data when (and if) it arrives. Never throws; null means "use fallback".
(function () {
  // Set to the deployed API URL (e.g. 'https://<app>.azurecontainerapps.io') after the first deploy.
  // Empty = API disabled: pages use only their embedded data and make no network requests.
  const API_BASE = '';
  const SEASON = '2026-27';
  const TIMEOUT_MS = 4000;

  const isLoopback = host => /^(localhost|127\.0\.0\.1|\[::1\])$/i.test(host);

  // ?api=<url> overrides the base for testing. Only honoured for a loopback API or when the page
  // itself is served from localhost, so a crafted link can't point the public site at a foreign API.
  function resolveBase() {
    const override = new URLSearchParams(location.search).get('api');
    if (override) {
      try {
        const u = new URL(override);
        if (/^https?:$/.test(u.protocol) && (isLoopback(u.hostname) || isLoopback(location.hostname))) {
          return u.origin + u.pathname.replace(/\/+$/, '');
        }
        console.warn('flaskeApi: ignoring ?api override', override);
      } catch (e) {
        console.warn('flaskeApi: invalid ?api override', override);
      }
    }
    return API_BASE.replace(/\/+$/, '');
  }

  const base = resolveBase();
  const cache = {};

  async function fetchSeason(season) {
    if (!base) return null;
    const ctrl = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = setTimeout(() => ctrl && ctrl.abort(new Error('timeout')), TIMEOUT_MS);
    try {
      const res = await Promise.race([
        fetch(base + '/api/seasons/' + encodeURIComponent(season), {
          mode: 'cors', cache: 'no-cache', headers: { Accept: 'application/json' }, signal: ctrl ? ctrl.signal : undefined
        }).then(r => {
          if (!r.ok) throw new Error('HTTP ' + r.status);
          return r.json();
        }),
        new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), TIMEOUT_MS + 100))
      ]);
      if (!res || typeof res !== 'object' || Array.isArray(res)) throw new Error('unexpected response');
      if (res.season && res.season !== season) throw new Error('season mismatch: ' + res.season);
      return res;
    } catch (e) {
      console.warn('flaskeApi: using embedded data for ' + season + ' (' + (e && e.message || e) + ')');
      return null;
    } finally {
      clearTimeout(timer);
    }
  }

  function loadSeason(season) {
    const s = season || SEASON;
    if (!cache[s]) cache[s] = fetchSeason(s);
    return cache[s];
  }

  // True for a non-empty plain object (an empty one usually means the API isn't seeded yet).
  const hasEntries = v => !!v && typeof v === 'object' && !Array.isArray(v) && Object.keys(v).length > 0;

  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  window.flaskeApi = { base, season: SEASON, loadSeason, hasEntries, esc };
})();
