# GTAA Moxie delivery script

`GtaaMessageDelivery.py` is Radiant's IronPython script that runs inside Omnivex
Moxie at GTAA. It pulls Director's Message delivery document and writes it into
the GTAA Messages DataPipe sheet for the Ink app, and the Business Rule values
document into its own sheet (spec `docs/spec/gtaa-fsd-1.1.md`, INK-01 to
INK-16, section 4.2, section 6.8).

This folder is the source of truth (A-20). The finished files are copied to the
Moxie server, and to `C:\CODE\Moxie` with the other Moxie scripts, after a run
merges.

| File | What it is |
| --- | --- |
| `GtaaMessageDelivery.py` | The script, in the house style: header block, Customization Points, helper functions, main |
| `config.example.json` | Every configuration key with its default, no real key or host |
| `tests/` | CPython tests for the parts of the script that do not need Moxie |

## Configuration

The script reads one JSON file, by default
`C:\ProgramData\Radiant\GtaaMessageDelivery\config.json` (the `configPath`
Customization Point is the only setting in the source). Copy
`config.example.json` there and fill in `directorBaseUrl` and `apiKey`. At
installation the file is restricted to the Moxie service account and
administrators, because it holds the key (INK-03).

| Key | Default | Meaning |
| --- | --- | --- |
| `directorBaseUrl` | required | `https://` address of Director, such as the load-balanced name (INK-03) |
| `apiKey` | required | A Director API key with scope `messaging:read`. Never printed |
| `deliveryPath` | `/api/messaging/delivery` | Delivery route (INK-03) |
| `ruleValuesPath` | `/api/messaging/rule-values` | Business Rule values route (INK-13) |
| `dataPipeFolder` | `Global` | `Global`, `Local`, or a DataPipe folder path as `GetSheet` takes it |
| `messagesSheetName` | `GTAA Messages` | Messages sheet (section 4.2) |
| `ruleValuesSheetName` | `GTAA Rule Values` | Business Rule values sheet (section 4.2) |
| `englishLanguageCode`, `frenchLanguageCode` | `eng`, `fra` | Language codes read for the `TextEN` and `TextFR` cells: the product catalogue codes Director keys each entry's `texts` by (D-02) |
| `safetyNetIntervalSeconds` | `12` | Longest wait between pulls |
| `httpTimeoutSeconds` | `5` | Timeout of each request to Director |
| `listenerEnabled` | `false` | Doorbell listener on or off (pull only at GTAA until Q-06, A-04) |
| `listenerPort` | none | Listener port; required when the listener is on |
| `listenerPath` | `gtaa-doorbell` | Listener path, as in `http://*:<port>/<path>/` |
| `debug` | `false` | Extra log lines; never the key or a response body |

Keys starting with `_` are notes. Unknown keys are ignored, so 1.2 can add keys
(AM-05). A `null` value takes the default.

The script refuses to start, and logs why, when the file is missing or not
valid JSON, when `directorBaseUrl` or `apiKey` is missing, or when a value has
the wrong kind. The refusal names the key and never prints a value.

Requests use TLS 1.2 with normal certificate validation (A-32). TLS 1.2 is
added to the protocols the Moxie process already allows, never put in their
place, as other scripts share the process. The script sets its own validating
callback on each request, so an always-true callback that another Moxie script
installs process-wide does not apply to it.

## Pull

Each pull is a GET of the delivery route with the key and `If-None-Match` set
to the ETag of the last body the sheet took (INK-05). Only a 200 whose body has
`schema` 1 and a `displays` list reaches the cell mapping
(`WriteDeliveryToSheet`), and its ETag is stored once the mapping has taken it.
A 304 writes nothing. Anything else (no connection, a timeout, any other
status, a body that is not JSON, another `schema`) writes nothing and keeps
every last value on the sheet (INK-11). An outage is logged once when it starts
and once when it ends; with `debug` on, every pull is logged, by size and
display count only.

## Messages sheet

`WriteDeliveryToSheet` finds the Messages sheet (`messagesSheetName` in
`dataPipeFolder`), `PlanDeliveryWrites` works out the cells that differ and
`ApplyWrites` writes them, as section 4.2 sets out:

- 165 columns, A to FI, labels in row 1. `MESSAGES_LAYOUT` is the one table of
  the columns; 1.2 redefines its counter blocks there (AM-05, AM-15). Columns
  CQ to FI are written blank in 1.1.
- One row per display, found by `DisplayId` in column A. A new display is
  appended below the last row; rows are never moved, sorted or removed. A
  display missing from a body keeps its row untouched and is logged by id and
  row. Removing a dead row is a manual step: delete the whole row, since the
  script takes the first blank column A cell as the bottom of the sheet.
- Slot n of Full screen, Overlay and Ticker holds entry n of that part's
  array, so slots stay compact and the script keeps no slot state.
- Every cell is compared first and written only when it differs, labels
  included; a steady body writes 0 cells. Seconds, ids and `Rank` are written
  and compared as numbers, colours are upper case, text, paths and `EndUtc`
  pass through exactly, and an empty value is a blank cell.
- Write order: a `VersionId` going blank first, then every other changed cell,
  then the remaining `VersionId` cells, so new text never lands after its id.

A missing sheet, or a write that fails, stops the mapping; the pull keeps its
ETag, and the next pull compares again and writes only what did not land. With
`debug` on, each mapping logs its number of cells written.

## Business Rule values sheet

Each pull of the loop also pulls the Business Rule values route
(`ruleValuesPath`) the same way as the delivery route: the same key and scope,
its own ETag and `If-None-Match`, and the same refusals (INK-13). A good body
has `schema` 1 and a `values` list of rows with `key`, `value` and `unit`
(1.2 BRE-12). `PlanRuleValuesWrites` and the same `ApplyWrites` write it into
the Rule Values sheet (`ruleValuesSheetName`):

- Labels `key`, `value`, `unit` in row 1 (columns A to C), then one row per key
  in the order received, under the same cell rules as the Messages sheet:
  compare first, a number value written and compared as a number, anything
  else as text, an empty value a blank cell.
- A row without a key, or a key listed twice, is skipped and logged. Rows left
  over from a longer document are blanked (value and unit before key), and the
  keys no longer sent are logged.

The route is built in 1.2 and answers 404 until then (A-23), and whenever
`TenantFeatureFlags.BusinessRules` is off. A 404 writes nothing, keeps the
ETag and the sheet as they are, and is logged once (not as an outage at each
pull); the route being served again is logged once too. The sheet is opened
only after a good 200, so in 1.1 it need not exist yet.

The two routes never share a sheet, so a Business Rule change never rewrites a
Message cell. The delivery is pulled first in each pull, and a rule values
failure of any kind leaves the delivery pull and its writes alone.

## Loop and doorbell

The script runs as one long-lived copy with one writer, a loop: it pulls at
start, then waits for a doorbell nudge for up to `safetyNetIntervalSeconds` and
pulls again, so the nudge and the safety net never write at the same time
(INK-02). Nudges that arrive while a pull runs cause one more pull, not one
each.

- **Listener on**: a background thread listens on
  `http://*:<listenerPort>/<listenerPath>/` and answers every request there 200
  at once, with an empty body, then wakes the loop. It reads no body and writes
  no cell, so a nudge only causes a pull (INK-09) and a forged one costs one
  extra pull. A nudge pulls within about 1 second.
- **Listener off** (GTAA until Q-06, A-04): the loop pulls on the safety net
  alone, so a change lands within the interval.

A Moxie timed task starts the script again and again as a watchdog. A copy that
finds another running exits at once and logs nothing; the running copy holds a
named mutex (`Global\Radiant.GtaaMessageDelivery`), which also covers the
listener-off case where there is no port. A copy that finds the doorbell port
taken by another program exits without raising and logs one line. Any other
listener failure (access denied: the URL ACL is missing) is logged and raised.
The URL ACL and the inbound firewall rule for the port are installation steps,
needed only once the doorbell is allowed (Q-06). With `debug` on, each pull after the first says whether a nudge or the
safety net woke it.

## Tests

Moxie cannot run here, so the tests cover what does not need it: configuration
loading and refusal, the log lines, URL and folder helpers, the certificate
decision, TLS 1.2 added to the process protocols, the example config, the pull's outcomes and log lines against a stub
delivery route in the section 4.2 shape (hash ETag and 304), the Messages sheet
layout, cell rules, write order and rows against a fake sheet, the loop, the
nudge handling and the start-up rules (single copy, port in use, listener off),
the Business Rule values pull (404 included) and sheet, both routes in one loop,
the doorbell and safety-net timings with real threads and an `AutoResetEvent` stand-in, and a guard
against Python 3 only syntax that IronPython 2.7 would reject. Everything that needs .NET (file read, Newtonsoft,
`Debug.WriteLine`, `HttpWebRequest`, the sheets, `HttpListener`,
`AutoResetEvent`, the mutex) is a small function passed in
or kept at the bottom of the helpers, so the logic around it is tested with
Python stand-ins.

From the repository root, with Python 3:

```
python -B -m unittest discover -s tools/moxie/gtaa/tests -v
```

CI runs the same command in the `moxie-script-tests` job. The tests are not part
of `DXSuite.sln`.

### Rules for changes

- Keep the script IronPython 2.7: no f-strings, annotations, `nonlocal` or
  walrus, and ASCII only. The syntax test catches the common cases.
- Put new logic in functions that take plain Python values and the config
  dictionary, and test them in `tests/`. Keep .NET and Moxie calls (`Application`,
  `Debug`, `CellLocationConverter`, sheets, web requests) thin and outside those
  functions.
- What the tests cannot show (a real pull, real cell writes, the listener) is
  checked by the acceptance on a Moxie server.
