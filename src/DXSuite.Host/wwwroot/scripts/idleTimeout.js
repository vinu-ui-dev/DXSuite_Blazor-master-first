// Client-side idle timeout for session management compliance.
// Tracks user activity and redirects to /session-timeout after the configured inactivity period.
//
// The keepalive exists because Blazor Server traffic runs over a WebSocket, which never
// touches the cookie middleware. Without it the sliding auth cookie expires under a user who
// is actively working in the circuit: the UI keeps rendering from the principal captured at
// circuit start, while every real HTTP request (page load, upload, controller call) is already
// unauthenticated. Pinging an HTTP endpoint on activity keeps the cookie and the client timer
// expiring together, and a 401 from that ping means the session is gone server-side.
window.idleTimeout = {
    _idleTimer: null,
    _timeoutMs: 0,
    _keepaliveIntervalMs: 0,
    _lastKeepalive: 0,
    _started: false,

    // Safe to call on every render; only the first call wires anything up.
    start: function (timeoutMinutes) {
        if (this._started || timeoutMinutes <= 0) return;
        this._started = true;

        this._timeoutMs = timeoutMinutes * 60 * 1000;
        // Well inside the cookie's half-life, so the sliding renewal always has a chance to fire.
        this._keepaliveIntervalMs = Math.max(30000, this._timeoutMs / 4);
        this._lastKeepalive = Date.now();

        var self = this;

        var onActivity = function () {
            self._resetIdleTimer();

            if (Date.now() - self._lastKeepalive >= self._keepaliveIntervalMs) {
                self._lastKeepalive = Date.now();
                self.ping();
            }
        };

        ['mousemove', 'keydown', 'mousedown', 'touchstart', 'scroll'].forEach(function (evt) {
            document.addEventListener(evt, onActivity, { passive: true });
        });

        // A backgrounded tab throttles timers, so re-check the session on return rather than
        // trusting that the idle timer fired on schedule while the tab was hidden.
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible') onActivity();
        });

        this._resetIdleTimer();
    },

    // Returns true if the server still considers the session live, false if it is gone,
    // and null when the server could not be reached (unreachable != signed out).
    ping: async function () {
        try {
            var res = await fetch('/auth/keepalive', {
                method: 'GET',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (res.status === 401) {
                this.expire();
                return false;
            }
            return true;
        } catch {
            return null;
        }
    },

    expire: function () {
        if (this._idleTimer) clearTimeout(this._idleTimer);
        window.location.href = '/session-timeout';
    },

    _resetIdleTimer: function () {
        var self = this;
        if (this._idleTimer) clearTimeout(this._idleTimer);
        this._idleTimer = setTimeout(function () { self.expire(); }, this._timeoutMs);
    }
};
