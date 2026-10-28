# ******************************************************************************
# Moxie version: recorded at deployment
# Created: 2026-10-20
# Revised: 2026-10-20
# Version: 1
# Author: Radiant Technology
# ******************************************************************************
# Description: GTAA Message delivery from Director to Ink
#
# Pulls Director's Message delivery document (GET /api/messaging/delivery) and lays it into the
# GTAA Messages DataPipe sheet, one row per Director Display, for Omnivex's Ink app to read, and
# the Business Rule values document (GET /api/messaging/rule-values) into its own sheet.
# Specification: docs/spec/gtaa-fsd-1.1.md, INK-01 to INK-16, section 4.2 and section 6.8.
#
# CONFIGURATION. Everything site-specific lives in a JSON file on the Moxie server, by default
# C:\ProgramData\Radiant\GtaaMessageDelivery\config.json, never in this source: the API key in
# particular (INK-03). The file is readable only by the Moxie service account and administrators.
# See config.example.json beside this script for every key and its default. Unknown keys are
# ignored, so a later version can add keys without breaking an older script.
#
# The script refuses to start when the file is missing, is not valid JSON, has no API key, or has
# a value of the wrong kind. The refusal says which key is wrong and never prints the key itself.
#
# PULL. Each pull sends the ETag of the last body the sheet took as If-None-Match (INK-05). A 304
# writes nothing. An error, a timeout, a status other than 200 or 304, or a body that is not
# schema 1 also writes nothing: the sheet keeps every last value until a good pull (INK-11), and
# the outage is logged once when it starts and once when it ends.
#
# SHEET. The Messages sheet has 165 columns, A to FI, labels in row 1 and one row per Director
# Display, found by its DisplayId in column A; MESSAGES_LAYOUT is the one table of its columns.
# Slot n of each part holds entry n of that part's array, so the script keeps no slot state.
# Every cell is compared first and written only when it differs, numbers compared as numbers; a
# steady delivery writes nothing. Each slot's VersionId is written after its other cells, or
# first when it goes blank. New displays are appended at the bottom; rows are never moved or
# removed, and a display missing from a delivery keeps its row and is logged. Columns CQ to FI
# (the 1.2 counter columns) stay blank.
#
# RULE VALUES. Each pull also takes the Business Rule values route (GET /api/messaging/rule-values)
# the same way, with its own ETag, into its own sheet: labels key, value, unit in row 1, then one
# row per key in the order received, under the same cell rules (INK-13, section 4.2). The route is
# built in 1.2 and answers 404 until then, or while Business Rules is off: a 404 writes nothing and
# is logged once, not as an outage. The two routes never share a sheet, so a Business Rule change
# never rewrites a Message cell, and the delivery is pulled first so the rule values never hold
# it up.
#
# LOOP. The script is one long-lived copy with one writer: a loop that pulls at start, then waits
# for a doorbell nudge for up to safetyNetIntervalSeconds and pulls again (INK-02). With the
# listener on, a thread on http://*:<listenerPort>/<listenerPath>/ answers every request 200 at once
# and wakes the loop; it reads no body and writes no cell, so a nudge only causes a pull (INK-09).
# With the listener off (GTAA until Q-06, A-04) the loop pulls on the safety net alone. A Moxie
# timed task starts the script again as a watchdog: a copy that finds another running (a named
# mutex) or the doorbell port taken exits without raising. The URL ACL and inbound firewall rule
# for the port are deployment steps.
#
# TLS. Director is called over HTTPS with TLS 1.2 and normal certificate validation (A-32). TLS 1.2
# is added to the protocols the Moxie process allows, never put in their place. Other
# Moxie scripts in the same process install an always-true validation callback on
# ServicePointManager, so this script sets its own validating callback on each request instead of
# trusting the process-wide one.
#
# Leave debug off in production. Debug output never contains the API key or a response body.
# ******************************************************************************

# ******************************************************************************
# Imports
# ******************************************************************************

import sys

# Moxie runs this file under IronPython, where sys.platform is "cli". The repository's CPython tests
# import the same file to exercise the helper functions, so every .NET reference sits behind this.
RUNNING_IN_MOXIE = sys.platform == "cli"

if RUNNING_IN_MOXIE:
    import clr

    clr.AddReference("System")
    clr.AddReference("mscorlib")

    from System.IO import File, StreamReader
    from System.Net import (HttpListener, HttpListenerException, ServicePointManager,
                            SecurityProtocolType, WebException, WebRequest)
    from System.Net.Security import RemoteCertificateValidationCallback
    from System.Text import Encoding
    from System.Threading import AbandonedMutexException, AutoResetEvent, Mutex, Thread, ThreadStart

    # Newtonsoft Json.NET, checked in order: server interop path, x64, x32
    serverJsonPath = r"C:\interop\json\Newtonsoft.Json.dll"
    x64JsonPath    = r"C:\Program Files\Omnivex\Moxie\Player\Newtonsoft.Json.dll"
    x32JsonPath    = r"C:\Program Files (x86)\Omnivex\Moxie\Player\Newtonsoft.Json.dll"

    if File.Exists(serverJsonPath):
        clr.AddReferenceToFileAndPath(serverJsonPath)
    elif File.Exists(x64JsonPath):
        clr.AddReferenceToFileAndPath(x64JsonPath)
    elif File.Exists(x32JsonPath):
        clr.AddReferenceToFileAndPath(x32JsonPath)

    from Newtonsoft.Json import JsonConvert, JsonSerializerSettings, DateParseHandling
    from Newtonsoft.Json.Linq import JTokenType

    # Newtonsoft turns ISO 8601 strings into DateTime values by default, which read back in the
    # server's culture with no offset. endUtc must reach the sheet exactly as Director published it
    # (section 4.2, rule 3), so date parsing is off. getattr because the member is named None.
    jsonSettings = JsonSerializerSettings()
    jsonSettings.DateParseHandling = getattr(DateParseHandling, "None")

# ******************************************************************************
# Customization Points
# ******************************************************************************

# The only setting in the source: where the configuration file is. Every other Customization Point
# is read from that file by LoadConfig below and held in the config dictionary.
configPath = r"C:\ProgramData\Radiant\GtaaMessageDelivery\config.json"

# Bound by Main from the configuration file. Helpers that need a setting take the dictionary as an
# argument, so they stay testable outside Moxie.
config = None

# Each route's last ETag, kept for as long as the script runs (see NewPullState).
deliveryState = None
ruleValuesState = None

# ******************************************************************************
# Helper Functions: configuration
# ******************************************************************************

class ConfigError(Exception):
    """The configuration cannot be used, so the script does not start.
    The message names the file and the key at fault and never echoes a configured value, so a key
    pasted into the wrong field cannot leak through the refusal either."""
    pass


# Every key the script reads, with its kind and default. REQUIRED has no default. A key set to
# null in the file counts as absent and takes its default.
REQUIRED = object()

SETTINGS = (
    ("directorBaseUrl",          "httpsUrl",  REQUIRED),
    ("apiKey",                   "text",      REQUIRED),
    ("deliveryPath",             "routePath", "/api/messaging/delivery"),
    ("ruleValuesPath",           "routePath", "/api/messaging/rule-values"),
    ("messagesSheetName",        "text",      "GTAA Messages"),
    ("ruleValuesSheetName",      "text",      "GTAA Rule Values"),
    ("dataPipeFolder",           "text",      "Global"),
    # Director keys each entry's texts by its product catalogue codes, eng and fra under D-02.
    ("englishLanguageCode",      "text",      "eng"),
    ("frenchLanguageCode",       "text",      "fra"),
    ("safetyNetIntervalSeconds", "seconds",   12),
    ("httpTimeoutSeconds",       "seconds",   5),
    ("listenerEnabled",          "flag",      False),
    ("listenerPort",             "port",      None),
    ("listenerPath",             "text",      "gtaa-doorbell"),
    ("debug",                    "flag",      False),
)

# IronPython 2.7 has a separate long type for large whole numbers; CPython 3 does not.
try:
    NUMBER_TYPES = (int, long, float)
except NameError:
    NUMBER_TYPES = (int, float)


def IsNumber(value):
    # bool is a subclass of int, and true must not pass as an interval of 1 second.
    return isinstance(value, NUMBER_TYPES) and not isinstance(value, bool)


def CheckSetting(path, name, kind, value):
    """Raise ConfigError when value is not a usable value of its kind."""
    problem = None
    if kind == "text":
        if not isinstance(value, str) or value.strip() == "":
            problem = "must be non-empty text"
    elif kind == "httpsUrl":
        if (not isinstance(value, str) or not value.lower().startswith("https://")
                or value[len("https://"):].strip("/") == "" or len(value.split()) != 1):
            problem = "must be an absolute https:// URL"
    elif kind == "routePath":
        if not isinstance(value, str) or not value.startswith("/") or len(value.split()) != 1:
            problem = "must be a path starting with /"
    elif kind == "seconds":
        if not IsNumber(value) or value <= 0:
            problem = "must be a number of seconds above 0"
    elif kind == "port":
        if not IsNumber(value) or value != int(value) or not (1 <= value <= 65535):
            problem = "must be a whole number from 1 to 65535"
    elif kind == "flag":
        if not isinstance(value, bool):
            problem = "must be true or false"
    if problem is not None:
        raise ConfigError(path + ": " + name + " " + problem)


def LoadConfig(path, readText, parseJson):
    """Read and validate the configuration file, returning a dictionary with every default filled.

    readText and parseJson are passed in because the real ones are .NET (File.ReadAllText and
    Newtonsoft); the repository tests pass Python equivalents."""
    try:
        text = readText(path)
    except Exception as ex:
        raise ConfigError(path + ": cannot be read (" + str(ex) + ")")

    # The parser's own message is left out: it can quote the text near the fault, which may be
    # the key.
    try:
        document = parseJson(text)
    except Exception:
        raise ConfigError(path + ": is not valid JSON")

    if not isinstance(document, dict):
        raise ConfigError(path + ": must be a JSON object")

    loaded = {}
    for name, kind, default in SETTINGS:
        value = document.get(name)
        if value is None:
            if default is REQUIRED:
                raise ConfigError(path + ": " + name + " is missing")
            loaded[name] = default
            continue
        CheckSetting(path, name, kind, value)
        # The port goes into the listener prefix as text, where 8091.0 would not bind.
        loaded[name] = int(value) if kind == "port" else value

    if loaded["listenerEnabled"] and loaded["listenerPort"] is None:
        raise ConfigError(path + ": listenerPort is missing, and the listener is on")

    return loaded


def OnOff(flag):
    return "on" if flag else "off"


def DescribeConfig(loaded):
    """One line for the log at start, so support can see what the script is running with.
    The key is reported only as set; its value never appears."""
    listener = "listener off"
    if loaded["listenerEnabled"]:
        listener = "listener on port " + str(loaded["listenerPort"]) + " path /" + loaded["listenerPath"].strip("/") + "/"
    return ("started: Director " + loaded["directorBaseUrl"]
            + ", API key set"
            + ", sheets '" + loaded["messagesSheetName"] + "' and '" + loaded["ruleValuesSheetName"] + "'"
            + " in DataPipe " + loaded["dataPipeFolder"]
            + ", languages " + loaded["englishLanguageCode"] + "/" + loaded["frenchLanguageCode"]
            + ", safety net " + str(loaded["safetyNetIntervalSeconds"]) + " s"
            + ", timeout " + str(loaded["httpTimeoutSeconds"]) + " s"
            + ", " + listener
            + ", debug " + OnOff(loaded["debug"]))

# ******************************************************************************
# Helper Functions: logging
# ******************************************************************************

LOG_PREFIX = "[GtaaMessageDelivery] "


def FormatLogLine(message, loaded):
    """Prefix a log line and mask the API key wherever it appears.

    Masking here rather than at each call site covers text the script does not compose itself,
    such as a .NET exception message that might echo a request header."""
    line = LOG_PREFIX + str(message)
    if loaded is not None:
        apiKey = loaded.get("apiKey")
        if apiKey:
            line = line.replace(apiKey, "[api key]")
    return line


def StartUp(path, readText, parseJson, writeLine):
    """Load the configuration and log the start, or log why the script will not start and raise.

    Raising leaves the refusal in Moxie's script error log as well; the configuration is fixed by
    editing the file, after which the next run starts."""
    try:
        loaded = LoadConfig(path, readText, parseJson)
    except ConfigError as ex:
        writeLine(FormatLogLine("not starting: " + str(ex), None))
        raise
    writeLine(FormatLogLine(DescribeConfig(loaded), loaded))
    return loaded

# ******************************************************************************
# Helper Functions: Director requests
# ******************************************************************************

def RouteUrl(loaded, pathSetting):
    """Absolute URL of a Director route, from the base URL and one of the path settings.
    The base may carry a virtual directory and a trailing slash; both are kept and joined once."""
    return loaded["directorBaseUrl"].rstrip("/") + loaded[pathSetting]


def CertificateAccepted(sslPolicyErrors):
    """True only when .NET found nothing wrong with Director's certificate (SslPolicyErrors.None,
    which is 0). A name mismatch, a broken chain or no certificate at all refuses the request."""
    return int(sslPolicyErrors) == 0


def ValidateDirectorCertificate(sender, certificate, chain, sslPolicyErrors):
    return CertificateAccepted(sslPolicyErrors)


def AddTls12(servicePoints, protocols):
    """Allow TLS 1.2 for Director's requests. HttpWebRequest has no per-request protocol setting,
    so it is added to ServicePointManager's protocols; replacing them would take away a protocol
    another script in the same Moxie process relies on."""
    servicePoints.SecurityProtocol = servicePoints.SecurityProtocol | protocols.Tls12


def NewDirectorRequest(loaded, url):
    """An HttpWebRequest to Director with the key, the configured timeout and normal certificate
    validation. Callers add their own headers (If-None-Match) and read the response.

    The validation callback is set on the request because a per-request callback overrides
    ServicePointManager's process-wide one, which other Moxie scripts set to accept anything."""
    timeoutMs = int(loaded["httpTimeoutSeconds"] * 1000)
    request = WebRequest.Create(url)
    request.Method = "GET"
    request.Timeout = timeoutMs
    request.ReadWriteTimeout = timeoutMs
    request.Headers["X-API-KEY"] = loaded["apiKey"]
    request.ServerCertificateValidationCallback = RemoteCertificateValidationCallback(ValidateDirectorCertificate)
    return request

# ******************************************************************************
# Helper Functions: delivery pull
# ******************************************************************************

# What one pull did, for the loop and the tests.
PULL_CHANGED = "changed"
PULL_UNCHANGED = "unchanged"
PULL_FAILED = "failed"
PULL_ABSENT = "absent"

# INK-04: fields are only ever added within a schema number, so any other number means the body
# can no longer be mapped safely, however familiar it looks.
DELIVERY_SCHEMA = 1

# The routes the script pulls, each the same way (INK-05, INK-13): name (for the log), path
# setting, schema, the list every good body carries and what its entries are called, and whether
# a 404 means the route is simply not served. The Business Rule values route answers 404 until
# 1.2 builds it (A-23) and whenever TenantFeatureFlags.BusinessRules is off, so its 404 is a
# steady state, not an outage. A delivery 404 (Messaging off) stays a failed pull.
DELIVERY_ROUTE = {"name": "delivery", "path": "deliveryPath", "schema": DELIVERY_SCHEMA,
                  "list": "displays", "entries": "displays", "absentOn404": False}
RULE_VALUES_ROUTE = {"name": "rule values", "path": "ruleValuesPath", "schema": 1,
                     "list": "values", "entries": "rows", "absentOn404": True}


def NewPullState():
    """What the script remembers between pulls of one route: the ETag of the last body its sheet
    took, and whether the last pull failed or found the route not served, so each of those is
    logged once rather than at every poll."""
    return {"etag": None, "failing": False, "absent": False}


def PullSucceeded(loaded, route, state, message, writeLine):
    if state["failing"]:
        writeLine(FormatLogLine(route["name"] + " pull recovered", loaded))
    elif state["absent"]:
        writeLine(FormatLogLine(route["name"] + " route is now served", loaded))
    state["failing"] = False
    state["absent"] = False
    if loaded["debug"]:
        writeLine(FormatLogLine(message, loaded))


def PullFailed(loaded, route, state, reason, writeLine):
    """Log a failed pull and report it. Nothing reaches the sheet and the ETag stays, so the sheet
    keeps every last value and the next good pull brings it up to date (INK-11)."""
    if not state["failing"] or loaded["debug"]:
        writeLine(FormatLogLine(route["name"] + " pull failed: " + reason + "; keeping the last values", loaded))
    state["failing"] = True
    state["absent"] = False
    return PULL_FAILED


def PullAbsent(loaded, route, state, writeLine):
    """The route is not served (404). Nothing is written and the ETag stays; one line says so."""
    if not state["absent"] or loaded["debug"]:
        writeLine(FormatLogLine(route["name"] + " route not served (404); its sheet is left as it is", loaded))
    state["absent"] = True
    state["failing"] = False
    return PULL_ABSENT


def PullRoute(loaded, route, state, fetch, parseJson, handOff, writeLine):
    """Pull one route once and hand a good body of its schema to its sheet.

    fetch(url, ifNoneMatch) returns (status, etag, bodyText) for any HTTP answer and raises when
    Director cannot be reached; handOff(document) writes the sheet. Both are passed in because
    the real ones are .NET and Moxie; the repository tests pass stand-ins.

    No log line carries the body or anything taken from it beyond its size, its entry count and
    a numeric schema: the delivery body holds every Message's text, at the poll rate."""
    try:
        status, etag, bodyText = fetch(RouteUrl(loaded, route["path"]), state["etag"])
    except Exception as ex:
        return PullFailed(loaded, route, state, "cannot reach Director (" + str(ex) + ")", writeLine)

    if status == 304:
        PullSucceeded(loaded, route, state, route["name"] + " unchanged (304)", writeLine)
        return PULL_UNCHANGED
    if status == 404 and route["absentOn404"]:
        return PullAbsent(loaded, route, state, writeLine)
    if status != 200:
        return PullFailed(loaded, route, state, "HTTP " + str(status), writeLine)

    # The parser's message is left out: it can quote the text near the fault.
    notADocument = "the response is not a " + route["name"] + " document"
    try:
        document = parseJson(bodyText)
    except Exception:
        return PullFailed(loaded, route, state, "the response is not JSON", writeLine)
    if not isinstance(document, dict):
        return PullFailed(loaded, route, state, notADocument, writeLine)
    schema = document.get("schema")
    if not IsNumber(schema) or schema != route["schema"]:
        shown = str(schema) if IsNumber(schema) else "missing or not a number"
        return PullFailed(loaded, route, state, "unknown schema " + shown, writeLine)
    if not isinstance(document.get(route["list"]), list):
        return PullFailed(loaded, route, state, notADocument, writeLine)

    # The ETag is stored only once the sheet has taken the body, so a mapping that stops part way
    # gets the same body again on the next pull instead of a 304. Only the exception's type is
    # logged, as its message could quote a cell value.
    try:
        handOff(document)
    except Exception as ex:
        return PullFailed(loaded, route, state, "the cell mapping stopped (" + type(ex).__name__ + ")", writeLine)
    state["etag"] = etag
    PullSucceeded(loaded, route, state, route["name"] + " changed: " + str(len(bodyText)) + " bytes, "
                  + str(len(document[route["list"]])) + " " + route["entries"], writeLine)
    return PULL_CHANGED


def PullDelivery(loaded, state, fetch, parseJson, handOff, writeLine):
    """Pull the delivery route once and hand a good schema 1 body to the Messages sheet."""
    return PullRoute(loaded, DELIVERY_ROUTE, state, fetch, parseJson, handOff, writeLine)


def PullRuleValues(loaded, state, fetch, parseJson, handOff, writeLine):
    """Pull the Business Rule values route once, with its own state and ETag, and hand a good
    schema 1 body (BRE-12) to the Rule Values sheet. A 404 writes nothing (A-23)."""
    return PullRoute(loaded, RULE_VALUES_ROUTE, state, fetch, parseJson, handOff, writeLine)

# ******************************************************************************
# Helper Functions: cell mapping
# ******************************************************************************

# Section 4.2 "Message slot cells"; Overlay and Ticker slots have no AslPath.

MESSAGE_SLOT_CELLS = ("Rank", "Type", "TextEN", "TextFR", "TextColour", "BackColour", "ShowSec",
                      "HideSec", "EndUtc", "AslPath", "VersionId")
PART_SLOT_CELLS = tuple(cell for cell in MESSAGE_SLOT_CELLS if cell != "AslPath")
COUNTER_CELLS = ("CounterId", "Open", "PageKind", "PageShowSec", "WordmarkPath", "StdTextEN",
                 "StdTextFR", "StdTextColour", "StdBackColour", "ImageEN", "ImageFR", "Sunflower",
                 "Destination", "AllianceLogoPath", "PublishId")
COUNTER_MESSAGE_CELLS = MESSAGE_SLOT_CELLS[1:]

# The Messages sheet, block by block from column A (section 4.2): (label prefix, cells, the route
# part the block maps, slot index). A block with no part is written blank: in 1.1 that is the
# counter columns CQ to FI, which the 1.2 spec fills (AM-05) and reshapes (AM-15) here.
# Columns never move, so a later block is only ever appended at the end.
MESSAGES_LAYOUT = (
    ("",       ("DisplayId",),        None,         None),
    ("FS1_",   MESSAGE_SLOT_CELLS,    "fullScreen", 0),
    ("FS2_",   MESSAGE_SLOT_CELLS,    "fullScreen", 1),
    ("FS3_",   MESSAGE_SLOT_CELLS,    "fullScreen", 2),
    ("OV1_",   PART_SLOT_CELLS,       "overlay",    0),
    ("OV2_",   PART_SLOT_CELLS,       "overlay",    1),
    ("OV3_",   PART_SLOT_CELLS,       "overlay",    2),
    ("TK1_",   PART_SLOT_CELLS,       "ticker",     0),
    ("TK2_",   PART_SLOT_CELLS,       "ticker",     1),
    ("TK3_",   PART_SLOT_CELLS,       "ticker",     2),
    ("",       ("Layout",),           None,         None),
    ("CA_",    COUNTER_CELLS,         None,         None),
    ("CA_M1_", COUNTER_MESSAGE_CELLS, None,         None),
    ("CA_M2_", COUNTER_MESSAGE_CELLS, None,         None),
    ("CB_",    COUNTER_CELLS,         None,         None),
    ("CB_M1_", COUNTER_MESSAGE_CELLS, None,         None),
    ("CB_M2_", COUNTER_MESSAGE_CELLS, None,         None),
)

# One entry per column, in column order: (label, cell name, part, slot index).
MESSAGES_COLUMNS = [(prefix + cell, cell, part, slot)
                    for prefix, cells, part, slot in MESSAGES_LAYOUT
                    for cell in cells]
MESSAGES_LABELS = [label for label, cell, part, slot in MESSAGES_COLUMNS]

# Section 4.2, rule 2: these are written and compared as numbers, because Moxie reads "5" back
# as 5.0. Every other cell is text.
NUMBER_CELLS = frozenset(("DisplayId", "Rank", "ShowSec", "HideSec", "VersionId", "Open",
                          "PageShowSec", "Sunflower", "PublishId"))

# Rule 4: colours are #RRGGBB, upper case.
COLOUR_CELLS = frozenset(("TextColour", "BackColour", "StdTextColour", "StdBackColour"))

# The cells that reveal or hide what the rest of a slot, group or row holds, with their place in
# the write order: ids first, then Open, then Layout when switching on, the reverse when off.
SWITCH_CELLS = {"VersionId": 0, "PublishId": 0, "Open": 1, "Layout": 2}

# Message slot cell -> delivery route field (section 4.2 "Message slot cells"). TextEN and TextFR
# come from texts, by the configured language codes.
SLOT_FIELDS = {
    "Rank": "rank", "Type": "typeCode", "TextColour": "textColour",
    "BackColour": "backgroundColour", "ShowSec": "showSeconds", "HideSec": "hideSeconds",
    "EndUtc": "endUtc", "AslPath": "aslPath", "VersionId": "shownVersionId",
}

# A blank cell (rule 5): never "null", 0 or any other placeholder.
BLANK = ""


def AsNumber(value):
    """A cell or route value as a number, or None when it is not one. Moxie may hand a cell back
    as a .NET number or as text such as "5.0"."""
    if IsNumber(value):
        return value
    if value is None or isinstance(value, bool):
        return None
    try:
        return float(str(value).strip())
    except ValueError:
        return None


def IsBlank(value):
    return value is None or (isinstance(value, str) and value.strip() == "")


def CellValue(cell, raw):
    """The value a cell takes from a route value, under the rules for every cell (section 4.2).
    Text, paths and EndUtc pass through exactly as the route gives them (rules 3 and 6)."""
    if raw is None:
        return BLANK
    if cell in NUMBER_CELLS:
        number = AsNumber(raw)
        if number is None:
            return BLANK
        # A whole number goes in as one, so the sheet shows 5 rather than 5.0.
        return int(number) if number == int(number) else number
    if cell in COLOUR_CELLS:
        return str(raw).upper()
    return raw if isinstance(raw, str) else str(raw)


def TextMatches(current, wanted):
    """True when a cell already holds the text, or blank, it should, so it is not written (rule 1)."""
    if wanted == BLANK:
        return IsBlank(current)
    if IsBlank(current):
        return False
    if isinstance(current, str):
        return current == wanted
    # Moxie took text that looks like a number as a number; reading it back as text ("5.0")
    # would rewrite the cell on every pull.
    return AsNumber(current) is not None and AsNumber(current) == AsNumber(wanted)


def NumberMatches(current, wanted):
    """True when a cell already holds the number it should; Moxie may read it back as 5.0 or "5"."""
    return not IsBlank(current) and AsNumber(current) == wanted


def CellMatches(cell, current, wanted):
    """True when a Messages cell already holds the value it should, so it is not written (rule 1)."""
    if cell in NUMBER_CELLS and wanted != BLANK:
        return NumberMatches(current, wanted)
    return TextMatches(current, wanted)


def PartEntries(display, part):
    entries = display.get(part)
    return entries if isinstance(entries, list) else []


def SlotCellValue(loaded, cell, entry):
    if not isinstance(entry, dict):
        return BLANK
    if cell == "TextEN" or cell == "TextFR":
        texts = entry.get("texts")
        if not isinstance(texts, dict):
            return BLANK
        code = loaded["englishLanguageCode"] if cell == "TextEN" else loaded["frenchLanguageCode"]
        return CellValue(cell, texts.get(code))
    return CellValue(cell, entry.get(SLOT_FIELDS[cell]))


def RowValues(loaded, display):
    """The value of every column of a display's row, in column order.

    Slot n holds entry n of its part's array as Director ordered it, so slots are compact without
    the script keeping any slot state. Blocks with no part (the 1.2 counter columns) are blank."""
    values = []
    for label, cell, part, slot in MESSAGES_COLUMNS:
        if cell == "DisplayId":
            values.append(CellValue(cell, display.get("displayId")))
        elif part is None:
            values.append(BLANK)
        else:
            entries = PartEntries(display, part)
            values.append(SlotCellValue(loaded, cell, entries[slot]) if slot < len(entries) else BLANK)
    return values


def TurnsOff(cell, value):
    return value == BLANK or (cell == "Open" and value == 0) or (cell == "Layout" and value == "Closed")


def OrderedWrites(row, current, wanted):
    """The writes that bring one row from its current values to the wanted ones, in the write
    order of section 4.2: switch cells turning content off first, then every other changed cell,
    then the switch cells turning content on, so new text never lands after its VersionId.

    Returns (column, row, value) tuples, columns from 1. Each cell is written at most once, so a
    cell is never cleared and rewritten."""
    off, content, on = [], [], []
    for index in range(len(MESSAGES_COLUMNS)):
        cell = MESSAGES_COLUMNS[index][1]
        if CellMatches(cell, current[index], wanted[index]):
            continue
        write = (index + 1, row, wanted[index])
        order = SWITCH_CELLS.get(cell)
        if order is None:
            content.append(write)
        elif TurnsOff(cell, wanted[index]):
            off.append((-order, write))
        else:
            on.append((order, write))
    # sorted is stable, so cells of the same kind keep their column order.
    off = [write for order, write in sorted(off, key=lambda item: item[0])]
    on = [write for order, write in sorted(on, key=lambda item: item[0])]
    return off + content + on


def RowKey(value):
    """Column A's DisplayId as a lookup key: as a number, so 1101 and a read-back 1101.0 match."""
    number = AsNumber(value)
    return number if number is not None else str(value).strip()


def DisplayIdText(key):
    return str(int(key)) if IsNumber(key) and key == int(key) else str(key)


def LabelWrites(labels, readCell):
    """The writes that put a sheet's labels in row 1 from column A, for the cells that differ."""
    return [(index + 1, 1, labels[index]) for index in range(len(labels))
            if not TextMatches(readCell(index + 1, 1), labels[index])]


def ColumnAKeys(readCell):
    """Column A from row 2 down, in row order. The script only ever appends, so the first blank
    column A cell below the labels is the bottom of the sheet."""
    keys = []
    row = 2
    while not IsBlank(readCell(1, row)):
        keys.append(readCell(1, row))
        row += 1
    return keys


def FindRows(readCell):
    """The rows already on the sheet, by DisplayId, and the row a new display is appended at.
    A display listed twice keeps its first row."""
    keys = ColumnAKeys(readCell)
    rows = {}
    for index in range(len(keys)):
        key = RowKey(keys[index])
        if key not in rows:
            rows[key] = index + 2
    return rows, len(keys) + 2


def PlanDeliveryWrites(loaded, document, readCell):
    """Every cell write a delivery document needs, in order, and the notes to log.

    readCell(column, row) returns a cell's current value, columns and rows from 1; it is passed
    in because the real one reads a DataPipe sheet. Rows are found by column A and new displays
    appended; rows are never moved or removed, and a display missing from the document keeps its
    row untouched (section 4.2 "Rows", INK-15)."""
    writes = LabelWrites(MESSAGES_LABELS, readCell)
    notes = []

    rows, nextRow = FindRows(readCell)
    seen = set()
    for display in document["displays"]:
        displayId = display.get("displayId") if isinstance(display, dict) else None
        if not IsNumber(displayId):
            notes.append("a display without a numeric displayId was skipped")
            continue
        if displayId in seen:
            notes.append("display " + DisplayIdText(displayId) + " is listed twice; the first entry was used")
            continue
        seen.add(displayId)

        row = rows.get(displayId)
        if row is None:
            row = nextRow
            nextRow += 1
            notes.append("display " + DisplayIdText(displayId) + " is new: appended as row " + str(row))

        current = [readCell(index + 1, row) for index in range(len(MESSAGES_COLUMNS))]
        writes.extend(OrderedWrites(row, current, RowValues(loaded, display)))

    missing = sorted((row, key) for key, row in rows.items() if key not in seen)
    if missing:
        notes.append("displays missing from the delivery, rows left untouched: "
                     + ", ".join(DisplayIdText(key) + " (row " + str(row) + ")" for row, key in missing))
    return writes, notes


def ApplyWrites(loaded, sheetName, plan, writeCell, writeLine):
    """Make a sheet's planned writes in order and log what support needs to know. plan is the
    (writes, notes) a Plan*Writes function returns. Returns the number of cells written: 0 for a
    steady document.

    A write that raises stops the mapping; the pull then keeps its ETag, and the next pull
    compares against what did land and writes only the rest."""
    writes, notes = plan
    for column, row, value in writes:
        writeCell(column, row, value)
    for note in notes:
        writeLine(FormatLogLine(note, loaded))
    if loaded["debug"]:
        writeLine(FormatLogLine(sheetName + ": " + str(len(writes)) + " cells written", loaded))
    return len(writes)

# ******************************************************************************
# Helper Functions: Business Rule values sheet
# ******************************************************************************

# Section 4.2 "Business Rule values sheet": labels in row 1, then one row per key in the order
# the route sends them (BRE-12: catalogue order).
RULE_VALUES_LABELS = ("key", "value", "unit")


def RuleValueCell(raw):
    """A rule's value as a cell. The route gives each value its JSON type, so a number is written
    and compared as a number (Moxie reads "5" back as 5.0) and text passes through (rule 2)."""
    if raw is None or isinstance(raw, (dict, list)):
        return BLANK
    if isinstance(raw, bool):
        return 1 if raw else 0
    if IsNumber(raw):
        return int(raw) if raw == int(raw) else raw
    return raw if isinstance(raw, str) else str(raw)


def RuleValueMatches(current, wanted):
    if IsNumber(wanted):
        return NumberMatches(current, wanted)
    return TextMatches(current, wanted)


def TextCell(raw):
    if raw is None or isinstance(raw, (dict, list)):
        return BLANK
    return raw if isinstance(raw, str) else str(raw)


def PlanRuleValuesWrites(document, readCell):
    """Every cell write a Business Rule values document needs, in order, and the notes to log.

    readCell(column, row) reads the Rule Values sheet only, columns and rows from 1, so nothing
    here can reach a Message cell (INK-13). Row n + 1 holds the document's nth key; rows left over
    from a longer document are blanked, value and unit before key, so a mapping stopped part way
    still finds them by column A at the next pull."""
    writes = LabelWrites(RULE_VALUES_LABELS, readCell)
    notes = []

    rules = []
    seen = set()
    keyless = 0
    for entry in document["values"]:
        key = entry.get("key") if isinstance(entry, dict) else None
        if not isinstance(key, str) or IsBlank(key):
            keyless += 1
            continue
        if key in seen:
            notes.append("rule value " + key + " is listed twice; the first entry was used")
            continue
        seen.add(key)
        rules.append((key, RuleValueCell(entry.get("value")), TextCell(entry.get("unit"))))
    if keyless:
        notes.insert(0, str(keyless) + " rule values without a key were skipped")

    oldKeys = ColumnAKeys(readCell)

    for index in range(len(rules)):
        row = index + 2
        key, value, unit = rules[index]
        if not TextMatches(readCell(1, row), key):
            writes.append((1, row, key))
        if not RuleValueMatches(readCell(2, row), value):
            writes.append((2, row, value))
        if not TextMatches(readCell(3, row), unit):
            writes.append((3, row, unit))

    for row in range(len(rules) + 2, len(oldKeys) + 2):
        for column in (2, 3, 1):
            if not IsBlank(readCell(column, row)):
                writes.append((column, row, BLANK))

    dropped = [str(key) for key in oldKeys if str(key) not in seen]
    if dropped:
        notes.append("rule values no longer sent, rows blanked or reused: " + ", ".join(dropped))
    return writes, notes

# ******************************************************************************
# Helper Functions: DataPipe
# ******************************************************************************

class SheetNotFound(Exception):
    """A configured sheet is not in the DataPipe folder. Raised so the pull keeps its ETag and the
    document is written once the sheet exists."""

    def __init__(self, sheetName):
        Exception.__init__(self, sheetName)
        self.sheetName = sheetName


def ResolveDataPipeFolder(loaded, globalPath, localPath):
    """The DataPipe folder the sheets live in. "Global" (the default, section 4.2) and "Local" name
    Moxie's own folders; any other value is a folder path given as GetSheet takes it."""
    folder = loaded["dataPipeFolder"]
    if folder.lower() == "global":
        return globalPath
    if folder.lower() == "local":
        return localPath
    return folder

# ******************************************************************************
# Helper Functions: doorbell and loop
# ******************************************************************************

def RunDeliveryLoop(loaded, waitForNudge, pullOnce, writeLine, keepRunning=None):
    """The script's one writer: pull at start, then wait for a nudge for up to the safety-net
    interval and pull again, for as long as the script runs (INK-02).

    waitForNudge(seconds) returns True when a nudge ended the wait and False when the safety net
    ran out; pullOnce() is the pull and cell mapping. Both are passed in because the real ones are
    .NET (AutoResetEvent) and Moxie; keepRunning is for the tests, which cannot wait forever."""
    state = {"failing": False}
    PullInLoop(loaded, state, pullOnce, writeLine)
    while keepRunning is None or keepRunning():
        nudged = waitForNudge(loaded["safetyNetIntervalSeconds"])
        if loaded["debug"]:
            writeLine(FormatLogLine("pulling after a nudge" if nudged else "pulling on the safety net", loaded))
        PullInLoop(loaded, state, pullOnce, writeLine)


def PullEach(pulls):
    """Run every pull in order, as the loop's one pull, so all of them stay on the one writer.
    A pull that raises does not keep the later ones from running; the first exception is raised
    again once all have run, so the loop still logs it. The delivery pull goes first, so a nudge's
    change lands before the Business Rule values route is asked."""
    first = None
    for pull in pulls:
        try:
            pull()
        except Exception as ex:
            if first is None:
                first = ex
    if first is not None:
        raise first


def PullInLoop(loaded, state, pullOnce, writeLine):
    """One pull that cannot end the loop. PullRoute already turns every expected failure into a
    logged failed pull, so an exception here is a fault in the script; it is logged once per run of
    failures, by type only since its message could quote a cell value, and the loop carries on."""
    try:
        pullOnce()
    except Exception as ex:
        if not state["failing"]:
            writeLine(FormatLogLine("delivery loop: the pull raised " + type(ex).__name__ + "; carrying on", loaded))
        state["failing"] = True
        return
    state["failing"] = False


class ListenerClosed(Exception):
    """The doorbell listener has been stopped, so its thread ends."""
    pass


def HandleNudge(answer, signal):
    """Answer a nudge 200 at once and wake the loop. A nudge carries no data and only causes a
    pull (INK-09), so its body is not read and the listener never touches the sheet; a forged
    nudge costs one extra pull. The loop is woken even when the answer fails."""
    try:
        answer(200)
    except Exception:
        pass
    finally:
        signal()


def ServeNudges(loaded, accept, signal, writeLine):
    """The listener thread: take each request and answer it as a nudge until the listener closes.

    accept() blocks for the next request and returns its answer(status) function, or raises
    ListenerClosed once the listener has stopped; it is passed in because the real one is .NET
    (HttpListener.GetContext). A request that fails before it is accepted is skipped."""
    while True:
        try:
            answer = accept()
        except ListenerClosed:
            writeLine(FormatLogLine("doorbell listener stopped", loaded))
            return
        except Exception as ex:
            if loaded["debug"]:
                writeLine(FormatLogLine("doorbell request skipped (" + type(ex).__name__ + ")", loaded))
            continue
        HandleNudge(answer, signal)


class ListenerPortInUse(Exception):
    """The doorbell port is already taken, so this copy of the script does not run."""
    pass


# HttpListener's Win32 error codes for a taken address: 183 (ERROR_ALREADY_EXISTS) when the URL is
# already registered, 32 (ERROR_SHARING_VIOLATION) when another program bound the port directly.
# Access denied (5) is a missing URL ACL: a deployment fault, reported rather than passed over.
PORT_IN_USE_ERRORS = frozenset((32, 183))


def PortInUseError(errorCode):
    return errorCode in PORT_IN_USE_ERRORS


def ListenerPrefix(loaded):
    """The doorbell's HttpListener prefix, http://*:<port>/<path>/, which needs a slash each side
    of the path however it was written in the configuration."""
    return "http://*:" + str(loaded["listenerPort"]) + "/" + loaded["listenerPath"].strip("/") + "/"


# How a run of the script ended, for the tests.
RUN_ALREADY_RUNNING = "already running"
RUN_PORT_IN_USE = "port in use"
RUN_STOPPED = "stopped"


def RunScript(acquireSingleCopy, startUp, startListener, newNudgeEvent, pullOnce, writeLine,
              keepRunning=None):
    """Run the script as one long-lived copy: start up, start the doorbell listener when it is on,
    then run the loop. A Moxie timed task starts the script again and again as a watchdog, so a
    copy that finds another running exits at once and writes nothing.

    acquireSingleCopy() returns a release function, or None when another copy holds the script;
    the port alone cannot be the guard, because with the listener off there is no port.
    newNudgeEvent() returns (waitForNudge, signal); startListener(loaded, prefix, signal) starts
    the listener thread and returns its stop function, or raises ListenerPortInUse. All are passed
    in because the real ones are .NET.

    The listener is stopped however the run ends, Moxie aborting the script included, so the port
    is free for the copy the timed task starts next."""
    release = acquireSingleCopy()
    if release is None:
        return RUN_ALREADY_RUNNING
    stopListener = None
    try:
        loaded = startUp()
        waitForNudge, signal = newNudgeEvent()
        if loaded["listenerEnabled"]:
            prefix = ListenerPrefix(loaded)
            try:
                stopListener = startListener(loaded, prefix, signal)
            except ListenerPortInUse:
                # Not raised, so a timed task that keeps meeting a taken port fills no error log;
                # still one line, as no other copy of this script holds the port.
                writeLine(FormatLogLine("not running: the doorbell port " + str(loaded["listenerPort"])
                                        + " is in use by another program", loaded))
                return RUN_PORT_IN_USE
            except Exception as ex:
                writeLine(FormatLogLine("doorbell listener cannot start on " + prefix + " ("
                                        + str(ex) + "); the URL ACL and firewall rule are set at deployment", loaded))
                raise
            if loaded["debug"]:
                writeLine(FormatLogLine("doorbell listening on " + prefix, loaded))
        RunDeliveryLoop(loaded, waitForNudge, pullOnce, writeLine, keepRunning)
        return RUN_STOPPED
    finally:
        try:
            if stopListener is not None:
                stopListener()
        finally:
            release()

# ******************************************************************************
# Helper Functions: .NET bindings (Moxie only)
# ******************************************************************************

def JsonToPython(token):
    """Turn a Newtonsoft token into plain Python values, so the helpers above see the same types
    under IronPython as under the repository's CPython tests."""
    if token is None:
        return None
    kind = token.Type
    if kind == JTokenType.Object:
        result = {}
        for prop in token.Properties():
            result[prop.Name] = JsonToPython(prop.Value)
        return result
    if kind == JTokenType.Array:
        return [JsonToPython(item) for item in token]
    if kind == JTokenType.Null:
        return None
    if kind == JTokenType.Boolean:
        return bool(token.Value)
    if kind == JTokenType.Integer:
        return int(str(token.Value))
    if kind == JTokenType.Float:
        return float(str(token.Value))
    return str(token.Value)


def ParseJson(text):
    return JsonToPython(JsonConvert.DeserializeObject(text, jsonSettings))


def FetchDirector(loaded, url, ifNoneMatch):
    """GET a Director route, returning (status, etag, bodyText) as PullDelivery expects.

    HttpWebRequest raises a WebException for every status outside 2xx, 304 included, so a raised
    exception that carries a response is read as an answer; one without a response (timeout, DNS,
    refused connection, certificate refused) is passed on as Director being unreachable. Only a
    200's body is read: an error page is never needed and must not reach a log."""
    request = NewDirectorRequest(loaded, url)
    if ifNoneMatch:
        request.Headers["If-None-Match"] = ifNoneMatch
    try:
        response = request.GetResponse()
    except WebException as ex:
        if ex.Response is None:
            raise
        response = ex.Response
    try:
        status = int(response.StatusCode)
        etag = response.Headers["ETag"]
        bodyText = None
        if status == 200:
            reader = StreamReader(response.GetResponseStream(), Encoding.UTF8)
            try:
                bodyText = reader.ReadToEnd()
            finally:
                reader.Close()
        return status, etag, bodyText
    finally:
        response.Close()


def WriteDeliveryToSheet(loaded, document):
    """Lay a checked delivery document into the Messages sheet.

    PullDelivery calls this only with a dictionary of schema 1 with a displays list. Raising tells
    it the sheet did not take the body, so the ETag is not stored and the body comes again."""
    readCell, writeCell = SheetCells(loaded, loaded["messagesSheetName"])
    ApplyWrites(loaded, "Messages sheet", PlanDeliveryWrites(loaded, document, readCell), writeCell, WriteLine)


def WriteRuleValuesToSheet(loaded, document):
    """Lay a checked Business Rule values document into the Rule Values sheet. It gets that
    sheet's cells only, so it cannot touch a Message cell (INK-13). Called only after a 200, so in
    1.1, while the route answers 404, the sheet is never opened."""
    readCell, writeCell = SheetCells(loaded, loaded["ruleValuesSheetName"])
    ApplyWrites(loaded, "Rule Values sheet", PlanRuleValuesWrites(document, readCell), writeCell, WriteLine)


def SheetCells(loaded, sheetName):
    """(readCell, writeCell) for one DataPipe sheet, columns and rows from 1; raises SheetNotFound
    when the sheet is not in the configured folder.

    Cells are addressed from A1 as CellLocationConverter places it, so the mappings' columns and
    rows from 1 do not depend on whether Moxie counts sheet cells from 0 or 1."""
    folder = ResolveDataPipeFolder(loaded, Environment.GlobalDataPipePath, Environment.LocalDataPipePath)
    sheet = Application.Data.GetSheet(folder, sheetName)
    if sheet is None:
        raise SheetNotFound(sheetName)
    origin = CellLocationConverter.ToCellLocation("A1")

    def ReadCell(column, row):
        return sheet[origin.Column + column - 1, origin.Row + row - 1].Value

    def WriteCell(column, row, value):
        sheet[origin.Column + column - 1, origin.Row + row - 1].Value = value
    return ReadCell, WriteCell


# One copy per machine, whichever Moxie process or script engine starts it. Global needs the
# service's right to create global objects; Local (one Windows session) is the fallback.
SINGLE_COPY_NAMES = (r"Global\Radiant.GtaaMessageDelivery", r"Local\Radiant.GtaaMessageDelivery")


def AcquireSingleCopy():
    """Take the named mutex that marks the running copy, or return None when another copy holds it.

    A mutex belongs to the thread that took it and is abandoned when that thread ends, so a copy
    that died without releasing it is replaced by the next timed-task start, as a watchdog should."""
    mutex = None
    for name in SINGLE_COPY_NAMES:
        try:
            mutex = Mutex(False, name)
            break
        except Exception:
            continue
    try:
        acquired = mutex.WaitOne(0)
    except AbandonedMutexException:
        acquired = True
    if not acquired:
        mutex.Close()
        return None

    def Release():
        try:
            mutex.ReleaseMutex()
        finally:
            mutex.Close()
    return Release


def NewNudgeEvent():
    """An AutoResetEvent: it wakes the loop once and resets, so nudges that arrive while a pull is
    running cause one more pull, not one each."""
    nudge = AutoResetEvent(False)

    def WaitForNudge(seconds):
        return nudge.WaitOne(int(seconds * 1000))

    def Signal():
        nudge.Set()
    return WaitForNudge, Signal


def AnswerNudge(response, status):
    """The listener's single response point: every request gets its status and an empty body, and
    the response is closed whatever fails, so Director's 2 second nudge never waits on the script."""
    try:
        response.StatusCode = status
        response.ContentLength64 = 0
    finally:
        try:
            response.Close()
        except Exception:
            pass


def StartDoorbellListener(loaded, prefix, signal):
    """Start the doorbell listener on its own background thread and return its stop function.

    GetContext blocks until a request arrives, which is simpler and more dependable under
    IronPython than BeginGetContext. Stopping the listener makes GetContext raise, which ends the
    thread through ListenerClosed."""
    listener = HttpListener()
    listener.Prefixes.Add(prefix)
    try:
        listener.Start()
    except HttpListenerException as ex:
        listener.Close()
        if PortInUseError(ex.ErrorCode):
            raise ListenerPortInUse(prefix)
        raise

    def Accept():
        try:
            context = listener.GetContext()
        except Exception:
            if not listener.IsListening:
                raise ListenerClosed()
            raise
        return lambda status: AnswerNudge(context.Response, status)

    thread = Thread(ThreadStart(lambda: ServeNudges(loaded, Accept, signal, WriteLine)))
    thread.IsBackground = True
    thread.Start()

    def Stop():
        try:
            listener.Stop()
        finally:
            listener.Close()
    return Stop


def ReadText(path):
    return File.ReadAllText(path)


def WriteLine(line):
    Debug.WriteLine(line)

# ******************************************************************************
# Main
# ******************************************************************************

def FetchFromDirector(url, ifNoneMatch):
    return FetchDirector(config, url, ifNoneMatch)


def PullDeliveryOnce():
    return PullDelivery(config, deliveryState, FetchFromDirector, ParseJson,
                        lambda document: WriteDeliveryToSheet(config, document), WriteLine)


def PullRuleValuesOnce():
    return PullRuleValues(config, ruleValuesState, FetchFromDirector, ParseJson,
                          lambda document: WriteRuleValuesToSheet(config, document), WriteLine)


def PullOnce():
    # Both routes in the loop's one pull, so the doorbell and the safety net pull each (INK-13)
    # and every cell is written by the one loop thread.
    PullEach((PullDeliveryOnce, PullRuleValuesOnce))


def StartScript():
    global config, deliveryState, ruleValuesState
    config = StartUp(configPath, ReadText, ParseJson, WriteLine)
    deliveryState = NewPullState()
    ruleValuesState = NewPullState()

    AddTls12(ServicePointManager, SecurityProtocolType)
    return config


def Main():
    # Runs until Moxie stops the script; a Moxie timed task starts it again if it ever ends.
    RunScript(AcquireSingleCopy, StartScript, StartDoorbellListener, NewNudgeEvent, PullOnce, WriteLine)


if RUNNING_IN_MOXIE:
    Main()
