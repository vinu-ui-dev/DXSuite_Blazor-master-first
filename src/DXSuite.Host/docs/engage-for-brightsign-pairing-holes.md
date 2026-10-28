# The two pairing holes section 9.7 left open

Written for whoever fixes them. Both predate this flavour and neither blocks a build. They are
left open deliberately: each is anti-enumeration behaviour that section 9.7 describes but does not
specify a replacement for, and changing either one carelessly turns a denial-of-service defence
into a denial of service.

The third hole in that section, the repeatable status poll, is fixed. Delivery is one-time, the
plaintext is dropped from the row on collection or on expiry of the fifteen-minute window, and what
sits there in between is protected at rest.

---

## Hole 1: one operator typo denies every pending enrollment in the tenant

**Where.** `DXSuite.Director/Services/DevicePairingService.cs:158-177`, inside
`ApprovePairingAsync`, on the branch taken when the entered code matches nothing.

**What happens now.** A wrong six-digit code increments `FailedAttempts` on **every** pending
request in the tenant, and auto-denies each one that crosses `MaxFailedAttempts` (5). A denied row
is permanent and is also the blocklist, so those devices cannot re-request either.

**Why it is written that way.** It is an anti-enumeration measure. If only the matching request's
counter moved, an attacker could tell a hit from a miss by watching which counter changed, and the
code is only about 19.8 bits.

**What it costs.** Five typos during a rollout deny every player waiting to enroll, and the recovery
is unblocking each denied row by hand. The larger the enrollment batch, the worse it gets, which is
exactly backwards.

**The shape of the fix.** Count failures against the **actor**, not against the pending requests.
The attempt is made by a signed-in operator over an authenticated call, so there is an identity to
count against that an attacker cannot spread across:

- Keep a per-user (or per-source-IP for defence in depth) failure counter with a lockout, the same
  shape `ApiKeyRateLimiter` already uses for auth failures.
- Leave `TblDevicePairingRequest.FailedAttempts` alone, or retire it. Nothing else reads it.
- Enumeration stays closed because the response is still generic and the operator is locked out
  before they could walk the space.

**What to be careful about.** Do not simply move the increment onto the matching request only. That
is the change that reopens enumeration, and it looks like the obvious minimal fix.

---

## Hole 2: the blocklist is poisonable

**Where.** `DXSuite.Director/Services/DevicePairingService.cs:375-390`, `IsDeviceBlockedAsync`,
called from `CreatePairingRequestAsync:41`.

**What happens now.** Denied pairing requests are retained forever and serve as the blocklist. The
match is on `DeviceId` **or** `MacAddress`, and both arrive in the body of an anonymous
`POST api/signage/Pairing/request`. So anyone who can reach that route can submit a request
carrying a real player's MAC, get it denied, and permanently block that player from ever enrolling.

**Why it is written that way.** A denied device should not be able to come back by asking again, and
at the moment of denial the request body is the only identity on hand.

**What it costs.** A remote, unauthenticated, permanent denial of enrollment for any player whose
MAC is known, and MACs are printed on the chassis.

**The shape of the fix.** Block on an identity the requester does not supply, or make the block
revocable in practice rather than only in theory:

- **Preferred**: block on nothing the caller asserts. The only trustworthy identity at that point is
  the connection, so rate-limit and lock out by source, and treat the denied-row list as an
  operator-facing record rather than as an enforcement gate.
- **If the blocklist must stay an enforcement gate**: match only on rows an operator actually
  reviewed with the device in front of them, and give denied rows a retention window instead of
  keeping them forever. A block a human never saw should not outlive the request that created it.
- Either way, the existing Unblock action stays the manual escape hatch.

**What to be careful about.** Matching on `DeviceId` alone is not a fix: it is client-chosen too,
and historically it was the hostname.

---

## Where the pairing routes still have no throttle

Related, and also still open: section 9.7 asks for a rate limiter on
`POST api/signage/Pairing/request` per IP and per tenant, plus a cap on outstanding unapproved
requests per tenant, and the same limiter on the pairing-grant path keyed on the armed serial.
`PairingController` still touches no limiter, so an unauthenticated caller can create unbounded
`TblDevicePairingRequest` rows in any tenant's Director database.

**This one is now cheap.** The recovery work added the solution's first `AddRateLimiter`, registered
in `DXSuite.Host/Program.cs` and configured by
`DXSuite.Director/Services/RecoveryRateLimiting.cs`. A second policy alongside it, applied to
`PairingController` with `[EnableRateLimiting]`, is the same shape and needs no new infrastructure.
Both fixes above are made easier by having it, because it gives them somewhere to put a lockout.
