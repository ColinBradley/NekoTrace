const COOKIE_NAME = "nekotrace-time-zone";
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

export function getTimeZone(): string {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

// Written as a side effect of importing rather than on demand, because a page being prerendered has no circuit
// yet and so cannot ask the browser anything. The cookie is what the server reads to get the zone right from
// the first byte of the *next* request; without it every full page load would render UTC until interop caught up.
document.cookie = `${COOKIE_NAME}=${encodeURIComponent(getTimeZone())};path=/;max-age=${COOKIE_MAX_AGE_SECONDS};samesite=lax`;
