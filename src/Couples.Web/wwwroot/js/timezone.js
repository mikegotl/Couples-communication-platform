// Returns the browser's IANA time zone (for example "Europe/London"), used as the default for quiet hours.
export function browserTimeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}
