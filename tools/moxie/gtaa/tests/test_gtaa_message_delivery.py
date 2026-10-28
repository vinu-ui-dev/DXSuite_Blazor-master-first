# CPython tests for the host-independent parts of GtaaMessageDelivery.py.
#
# The script itself runs under IronPython inside Moxie, which these tests cannot start. Everything
# that needs .NET or Moxie sits behind small functions passed in at the seam (file read, JSON parse,
# log line), so the logic is exercised here with plain Python stand-ins.

import base64
import hashlib
import json
import os
import re
import socket
import sys
import threading
import time
import tokenize
import unittest

SCRIPT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, SCRIPT_DIR)
sys.dont_write_bytecode = True

import GtaaMessageDelivery as script  # noqa: E402

SECRET = "k3y-Th4t-Must-N3ver-Print"
CONFIG_PATH = r"C:\ProgramData\Radiant\GtaaMessageDelivery\config.json"


def Load(document, path=CONFIG_PATH):
    """Load a config from a Python dict as if it were the JSON file on disk."""
    text = json.dumps(document)
    return script.LoadConfig(path, lambda p: text, json.loads)


def Minimal(**overrides):
    document = {"directorBaseUrl": "https://director.example.test", "apiKey": SECRET}
    document.update(overrides)
    return document


class RefusesToStartWithoutAKey(unittest.TestCase):

    def test_missing_key_is_refused(self):
        document = Minimal()
        del document["apiKey"]
        with self.assertRaises(script.ConfigError) as caught:
            Load(document)
        self.assertIn("apiKey", str(caught.exception))

    def test_blank_key_is_refused(self):
        for blank in ("", "   "):
            with self.assertRaises(script.ConfigError):
                Load(Minimal(apiKey=blank))

    def test_key_that_is_not_text_is_refused_without_echoing_it(self):
        with self.assertRaises(script.ConfigError) as caught:
            Load(Minimal(apiKey=123456789))
        self.assertIn("apiKey", str(caught.exception))
        self.assertNotIn("123456789", str(caught.exception))

    def test_no_refusal_message_contains_the_key(self):
        broken = [
            Minimal(directorBaseUrl=""),
            Minimal(directorBaseUrl=SECRET),
            Minimal(safetyNetIntervalSeconds="twelve"),
            Minimal(listenerEnabled=True),
        ]
        for document in broken:
            with self.assertRaises(script.ConfigError) as caught:
                Load(document)
            self.assertNotIn(SECRET, str(caught.exception))


class ConfigDefaults(unittest.TestCase):

    def test_minimal_file_takes_the_spec_defaults(self):
        config = Load(Minimal())
        # Section 6.8 (12 s safety net, 5 s timeout), section 4.2 (sheet names), INK-03,
        # INK-13 (route paths), A-04 (pull only until the doorbell is allowed).
        self.assertEqual(config["safetyNetIntervalSeconds"], 12)
        self.assertEqual(config["httpTimeoutSeconds"], 5)
        self.assertEqual(config["messagesSheetName"], "GTAA Messages")
        self.assertEqual(config["ruleValuesSheetName"], "GTAA Rule Values")
        self.assertEqual(config["deliveryPath"], "/api/messaging/delivery")
        self.assertEqual(config["ruleValuesPath"], "/api/messaging/rule-values")
        self.assertEqual(config["dataPipeFolder"], "Global")
        # Director keys texts by the codes of MessageLanguageCatalogue.GtaaLanguages (D-02).
        self.assertEqual(config["englishLanguageCode"], "eng")
        self.assertEqual(config["frenchLanguageCode"], "fra")
        self.assertIs(config["listenerEnabled"], False)
        self.assertIs(config["debug"], False)

    def test_values_in_the_file_win_over_defaults(self):
        config = Load(Minimal(safetyNetIntervalSeconds=15, httpTimeoutSeconds=3,
                              englishLanguageCode="en-CA", listenerEnabled=True,
                              listenerPort=8091, listenerPath="doorbell", debug=True))
        self.assertEqual(config["safetyNetIntervalSeconds"], 15)
        self.assertEqual(config["httpTimeoutSeconds"], 3)
        self.assertEqual(config["englishLanguageCode"], "en-CA")
        self.assertEqual(config["listenerPort"], 8091)
        self.assertEqual(config["listenerPath"], "doorbell")
        self.assertIs(config["debug"], True)

    def test_a_port_written_with_a_decimal_point_is_a_whole_port(self):
        config = Load(Minimal(listenerEnabled=True, listenerPort=8091.0))
        self.assertEqual(str(config["listenerPort"]), "8091")

    def test_null_counts_as_absent(self):
        config = Load(Minimal(httpTimeoutSeconds=None))
        self.assertEqual(config["httpTimeoutSeconds"], 5)

    def test_unknown_keys_are_ignored_so_later_versions_can_add_keys(self):
        config = Load(Minimal(counterColumns={"enabled": True}, _comment="notes"))
        self.assertEqual(config["apiKey"], SECRET)

    def test_values_of_the_wrong_kind_are_refused(self):
        broken = {
            "directorBaseUrl": "http://director.example.test",
            "deliveryPath": "api/messaging/delivery",
            "httpTimeoutSeconds": 0,
            "safetyNetIntervalSeconds": True,
            "listenerPort": 70000,
            "listenerEnabled": "yes",
            "messagesSheetName": "",
        }
        for name, value in broken.items():
            with self.assertRaises(script.ConfigError) as caught:
                Load(Minimal(**{name: value}))
            self.assertIn(name, str(caught.exception))


class ConfigFileProblems(unittest.TestCase):

    def test_missing_file_is_refused(self):
        def Missing(path):
            raise IOError("Could not find file '" + path + "'.")
        with self.assertRaises(script.ConfigError) as caught:
            script.LoadConfig(CONFIG_PATH, Missing, json.loads)
        self.assertIn(CONFIG_PATH, str(caught.exception))

    def test_invalid_json_is_refused_without_quoting_the_parser(self):
        # A parser's message can quote the text near the fault, which may be the key itself.
        text = '{"apiKey": "' + SECRET + '" "directorBaseUrl": }'
        with self.assertRaises(script.ConfigError) as caught:
            script.LoadConfig(CONFIG_PATH, lambda p: text, json.loads)
        self.assertIn("not valid JSON", str(caught.exception))
        self.assertNotIn(SECRET, str(caught.exception))

    def test_a_document_that_is_not_an_object_is_refused(self):
        with self.assertRaises(script.ConfigError):
            script.LoadConfig(CONFIG_PATH, lambda p: "[1, 2]", json.loads)


class StartUpNeverPrintsTheKey(unittest.TestCase):

    def StartUp(self, document):
        lines = []
        text = json.dumps(document)
        try:
            config = script.StartUp(CONFIG_PATH, lambda p: text, json.loads, lines.append)
        except script.ConfigError:
            config = None
        return config, lines

    def test_start_logs_the_settings_but_not_the_key(self):
        config, lines = self.StartUp(Minimal())
        self.assertIsNotNone(config)
        self.assertEqual(len(lines), 1)
        self.assertIn("https://director.example.test", lines[0])
        self.assertIn("listener off", lines[0])
        self.assertNotIn(SECRET, lines[0])

    def test_refusal_is_logged_and_raised(self):
        document = Minimal()
        del document["apiKey"]
        config, lines = self.StartUp(document)
        self.assertIsNone(config)
        self.assertEqual(len(lines), 1)
        self.assertIn("not starting", lines[0])
        self.assertIn("apiKey", lines[0])

    def test_refusal_for_another_key_does_not_print_the_key(self):
        config, lines = self.StartUp(Minimal(httpTimeoutSeconds=-1))
        self.assertIsNone(config)
        self.assertNotIn(SECRET, "".join(lines))

    def test_log_lines_mask_the_key_wherever_it_appears(self):
        config = Load(Minimal())
        line = script.FormatLogLine("request failed: X-API-KEY " + SECRET + " rejected", config)
        self.assertNotIn(SECRET, line)
        self.assertIn("request failed", line)

    def test_log_lines_before_the_config_is_loaded_are_passed_through(self):
        self.assertIn("starting", script.FormatLogLine("starting", None))


class RouteUrls(unittest.TestCase):

    def test_route_is_joined_to_the_base_with_one_slash(self):
        for base in ("https://director.example.test", "https://director.example.test/"):
            config = Load(Minimal(directorBaseUrl=base))
            self.assertEqual(script.RouteUrl(config, "deliveryPath"),
                             "https://director.example.test/api/messaging/delivery")

    def test_base_url_with_a_virtual_directory_keeps_it(self):
        config = Load(Minimal(directorBaseUrl="https://host.example.test/director/"))
        self.assertEqual(script.RouteUrl(config, "ruleValuesPath"),
                         "https://host.example.test/director/api/messaging/rule-values")


class DataPipeFolder(unittest.TestCase):

    GLOBAL = "||Data|Global"
    LOCAL = "||Data|Local"

    def Resolve(self, value):
        return script.ResolveDataPipeFolder(Load(Minimal(dataPipeFolder=value)), self.GLOBAL, self.LOCAL)

    def test_global_and_local_name_moxies_own_folders(self):
        self.assertEqual(self.Resolve("Global"), self.GLOBAL)
        self.assertEqual(self.Resolve("global"), self.GLOBAL)
        self.assertEqual(self.Resolve("Local"), self.LOCAL)

    def test_anything_else_is_a_folder_path_used_as_written(self):
        self.assertEqual(self.Resolve("||Data|Workspaces|GTAA|Sheets"), "||Data|Workspaces|GTAA|Sheets")


class Tls12(unittest.TestCase):
    """Moxie runs every script in one process, so TLS 1.2 is added to the protocols already set
    there, never put in their place."""

    class Protocols(object):
        Ssl3, Tls, Tls11, Tls12 = 48, 192, 768, 3072

    class ServicePoints(object):
        SecurityProtocol = 48 | 192

    def test_tls_1_2_is_added_to_the_protocols_already_set(self):
        servicePoints = self.ServicePoints()
        script.AddTls12(servicePoints, self.Protocols)
        self.assertEqual(servicePoints.SecurityProtocol, 48 | 192 | 3072)

    def test_adding_it_again_changes_nothing(self):
        servicePoints = self.ServicePoints()
        servicePoints.SecurityProtocol = 3072 | 768
        script.AddTls12(servicePoints, self.Protocols)
        self.assertEqual(servicePoints.SecurityProtocol, 3072 | 768)

    def test_the_script_never_replaces_the_process_protocols(self):
        with open(os.path.join(SCRIPT_DIR, "GtaaMessageDelivery.py")) as source:
            text = source.read()
        self.assertIsNone(re.search(r"SecurityProtocol\s*=\s*SecurityProtocolType", text))


class CertificateValidation(unittest.TestCase):
    # SslPolicyErrors: 0 None, 1 RemoteCertificateNotAvailable, 2 RemoteCertificateNameMismatch,
    # 4 RemoteCertificateChainErrors.

    def test_only_a_certificate_with_no_policy_errors_is_accepted(self):
        self.assertTrue(script.CertificateAccepted(0))
        for errors in (1, 2, 4, 6):
            self.assertFalse(script.CertificateAccepted(errors))


BODY_MARKER = "Body-Text-That-Must-Never-Be-Logged"


def DeliveryBody(schema=1, overlayVersion=501):
    """A delivery document in the section 4.2 / INK-04 shape: an Emergency over an Overlay and a
    Ticker on one display, nothing on the other."""
    def Entry(rank, typeCode, english, versionId, aslPath=None, fullScreen=False):
        entry = {
            "rank": rank, "typeCode": typeCode,
            "texts": {"eng": english, "fra": english + " (fr)"},
            "textColour": "#FFFFFF", "backgroundColour": "#C00000",
            "showSeconds": 5, "hideSeconds": 0,
            "endUtc": "2026-10-17T21:30:00Z", "shownVersionId": versionId,
        }
        if fullScreen:
            entry["aslPath"] = aslPath
        return entry
    return {
        "schema": schema,
        "displays": [
            {"displayId": 101,
             "fullScreen": [Entry(1, "EMERGENCY", "Evacuate " + BODY_MARKER, 500, None, True)],
             "overlay": [Entry(2, "IRROPS", "Gate change", overlayVersion)],
             "ticker": [Entry(3, "PAGING", "Paging Ms Smith", 502)]},
            {"displayId": 102, "fullScreen": [], "overlay": [], "ticker": []},
        ],
    }


class StubDeliveryRoute(object):
    """Director's delivery route as INK-03 to INK-05 describe it:
    a hash ETag (SHA-256, base64), 304 with no body on a matching If-None-Match, and switchable
    failures. It records each request so tests can check the headers the pull sent."""

    def __init__(self, document):
        self.requests = []
        self.failure = None
        self.Publish(document)

    def Publish(self, document):
        self.body = json.dumps(document)
        self.etag = '"' + base64.b64encode(hashlib.sha256(self.body.encode("utf-8")).digest()).decode("ascii") + '"'

    def Fetch(self, url, ifNoneMatch):
        self.requests.append((url, ifNoneMatch))
        if self.failure == "timeout":
            raise socket.timeout("The operation has timed out")
        if self.failure == "unreachable":
            raise IOError("Unable to connect to the remote server")
        if isinstance(self.failure, Exception):
            raise self.failure
        if isinstance(self.failure, int):
            return self.failure, None, "<html>error page " + BODY_MARKER + "</html>"
        if ifNoneMatch == self.etag:
            return 304, self.etag, None
        return 200, self.etag, self.body


class RecordingSheet(object):
    """Stands in for the cell mapping: each hand-off counts as writing the document."""

    def __init__(self):
        self.received = []

    def HandOff(self, document):
        self.received.append(document)


class PullWithETag(unittest.TestCase):
    """The pull, its ETag, and the cases where nothing may reach the sheet (INK-05, INK-11,
    INK-15)."""

    def setUp(self):
        self.config = Load(Minimal(directorBaseUrl="https://director.example.test/"))
        self.route = StubDeliveryRoute(DeliveryBody())
        self.sheet = RecordingSheet()
        self.state = script.NewPullState()
        self.lines = []

    def Pull(self):
        return script.PullDelivery(self.config, self.state, self.route.Fetch, json.loads,
                                   self.sheet.HandOff, self.lines.append)

    def test_a_first_pull_hands_the_body_to_the_cell_mapping_and_stores_the_etag(self):
        outcome = self.Pull()
        self.assertEqual(outcome, script.PULL_CHANGED)
        self.assertEqual(self.route.requests,
                         [("https://director.example.test/api/messaging/delivery", None)])
        self.assertEqual(self.sheet.received, [DeliveryBody()])
        self.assertEqual(self.state["etag"], self.route.etag)

    def test_a_304_writes_nothing(self):
        self.Pull()
        outcome = self.Pull()
        self.assertEqual(outcome, script.PULL_UNCHANGED)
        self.assertEqual(self.route.requests[1][1], self.route.etag)
        self.assertEqual(len(self.sheet.received), 1)
        self.assertEqual(self.state["etag"], self.route.etag)

    def test_an_error_a_5xx_or_a_timeout_keeps_every_value(self):
        self.Pull()
        goodEtag = self.state["etag"]
        # Director changes while the link is down: none of it may reach the sheet.
        self.route.Publish(DeliveryBody(overlayVersion=777))
        for failure in ("timeout", "unreachable", 500, 503, 401, 404):
            with self.subTest(failure=failure):
                self.route.failure = failure
                self.assertEqual(self.Pull(), script.PULL_FAILED)
                self.assertEqual(len(self.sheet.received), 1)
                self.assertEqual(self.state["etag"], goodEtag)

    def test_an_unknown_schema_keeps_every_value(self):
        self.Pull()
        goodEtag = self.state["etag"]
        # INK-04: removing or changing a field raises schema, so a body of any other schema cannot
        # be mapped safely, however much of it looks familiar.
        for schema in (2, 0, "1", True, None):
            with self.subTest(schema=schema):
                self.route.Publish(DeliveryBody(schema=schema))
                self.assertEqual(self.Pull(), script.PULL_FAILED)
                self.assertEqual(len(self.sheet.received), 1)
                self.assertEqual(self.state["etag"], goodEtag)

    def test_a_200_that_is_not_a_delivery_document_keeps_every_value(self):
        self.Pull()
        goodEtag = self.state["etag"]
        for body in ("<html>proxy login " + BODY_MARKER + "</html>", "", "[1, 2]",
                     '{"schema": 1}', '{"schema": 1, "displays": {}}'):
            with self.subTest(body=body):
                self.route.body = body
                self.route.etag = '"other"'
                self.assertEqual(self.Pull(), script.PULL_FAILED)
                self.assertEqual(len(self.sheet.received), 1)
                self.assertEqual(self.state["etag"], goodEtag)

    def test_a_body_the_cell_mapping_could_not_finish_is_pulled_again(self):
        def Broken(document):
            raise IOError("sheet locked")
        outcome = script.PullDelivery(self.config, self.state, self.route.Fetch, json.loads,
                                      Broken, self.lines.append)
        self.assertEqual(outcome, script.PULL_FAILED)
        self.assertIsNone(self.state["etag"])
        self.assertEqual(self.Pull(), script.PULL_CHANGED)
        self.assertEqual(self.route.requests[1][1], None)

    def test_the_first_good_pull_after_a_failure_catches_up(self):
        self.Pull()
        self.route.failure = 503
        self.Pull()
        self.route.Publish(DeliveryBody(overlayVersion=777))
        self.route.failure = None
        self.assertEqual(self.Pull(), script.PULL_CHANGED)
        self.assertEqual(self.sheet.received[-1], DeliveryBody(overlayVersion=777))
        self.assertEqual(self.state["etag"], self.route.etag)


class PullLogging(unittest.TestCase):
    """What support sees in the log from the pull, and what it must never see."""

    def setUp(self):
        self.route = StubDeliveryRoute(DeliveryBody())
        self.state = script.NewPullState()
        self.lines = []

    def Pull(self, debug=False):
        config = Load(Minimal(debug=debug))
        return script.PullDelivery(config, self.state, self.route.Fetch, json.loads,
                                   lambda document: None, self.lines.append)

    def test_with_debug_off_a_steady_link_logs_nothing(self):
        for _ in range(3):
            self.Pull()
        self.assertEqual(self.lines, [])

    def test_an_outage_is_logged_once_when_it_starts_and_once_when_it_ends(self):
        self.Pull()
        self.route.failure = 503
        for _ in range(3):
            self.Pull()
        self.assertEqual(len(self.lines), 1)
        self.assertIn("503", self.lines[0])
        self.assertIn("keeping the last values", self.lines[0])
        self.route.failure = None
        self.Pull()
        self.assertEqual(len(self.lines), 2)
        self.assertIn("recovered", self.lines[1])

    def test_an_unknown_schema_is_logged_by_number(self):
        self.route.Publish(DeliveryBody(schema=2))
        self.Pull()
        self.assertEqual(len(self.lines), 1)
        self.assertIn("schema 2", self.lines[0])

    def test_with_debug_on_every_pull_is_logged(self):
        self.Pull(debug=True)
        self.Pull(debug=True)
        self.route.failure = "timeout"
        self.Pull(debug=True)
        self.Pull(debug=True)
        self.assertEqual(len(self.lines), 4)
        self.assertIn("2 displays", self.lines[0])
        self.assertIn("304", self.lines[1])
        self.assertIn("timed out", self.lines[3])

    def test_no_line_ever_contains_the_key_or_the_body(self):
        cases = [None, None, "timeout", "unreachable", 500, 401,
                 IOError("The remote server rejected X-API-KEY " + SECRET)]
        for failure in cases:
            self.route.failure = failure
            self.Pull(debug=True)
        self.route.failure = None
        self.route.Publish(DeliveryBody(schema=7))
        self.Pull(debug=True)
        self.route.body = "<html>" + BODY_MARKER + "</html>"
        self.Pull(debug=True)
        self.assertGreater(len(self.lines), 0)
        for line in self.lines:
            self.assertNotIn(SECRET, line)
            self.assertNotIn(BODY_MARKER, line)
            self.assertNotIn("Evacuate", line)


def ColumnNumber(letters):
    """Spreadsheet column letters to a 1-based number, written out here independently of the
    script so the layout test does not check the script against itself."""
    number = 0
    for letter in letters:
        number = number * 26 + (ord(letter) - ord("A") + 1)
    return number


def Label(letters):
    return script.MESSAGES_LABELS[ColumnNumber(letters) - 1]


class MessagesSheetLayout(unittest.TestCase):
    """Section 4.2 "Messages sheet layout": 165 columns, A to FI, one label per column."""

    def test_the_sheet_has_165_columns_from_a_to_fi(self):
        self.assertEqual(len(script.MESSAGES_LABELS), 165)
        self.assertEqual(ColumnNumber("FI"), 165)

    def test_each_block_starts_and_ends_where_section_4_2_puts_it(self):
        expected = {
            "A": "DisplayId",
            "B": "FS1_Rank", "L": "FS1_VersionId", "M": "FS2_Rank", "W": "FS2_VersionId",
            "X": "FS3_Rank", "AH": "FS3_VersionId",
            "AI": "OV1_Rank", "AR": "OV1_VersionId", "AS": "OV2_Rank", "BB": "OV2_VersionId",
            "BC": "OV3_Rank", "BL": "OV3_VersionId",
            "BM": "TK1_Rank", "BV": "TK1_VersionId", "BW": "TK2_Rank", "CF": "TK2_VersionId",
            "CG": "TK3_Rank", "CP": "TK3_VersionId",
            "CQ": "Layout",
            "CR": "CA_CounterId", "DF": "CA_PublishId",
            "DG": "CA_M1_Type", "DP": "CA_M1_VersionId", "DQ": "CA_M2_Type", "DZ": "CA_M2_VersionId",
            "EA": "CB_CounterId", "EO": "CB_PublishId",
            "EP": "CB_M1_Type", "EY": "CB_M1_VersionId", "EZ": "CB_M2_Type", "FI": "CB_M2_VersionId",
        }
        for letters, label in expected.items():
            with self.subTest(column=letters):
                self.assertEqual(Label(letters), label)

    def test_a_full_screen_slot_has_aslpath_and_overlay_and_ticker_slots_do_not(self):
        fullScreen = ["FS1_Rank", "FS1_Type", "FS1_TextEN", "FS1_TextFR", "FS1_TextColour",
                      "FS1_BackColour", "FS1_ShowSec", "FS1_HideSec", "FS1_EndUtc", "FS1_AslPath",
                      "FS1_VersionId"]
        start = ColumnNumber("B") - 1
        self.assertEqual(script.MESSAGES_LABELS[start:start + 11], fullScreen)
        start = ColumnNumber("AS") - 1
        self.assertEqual(script.MESSAGES_LABELS[start:start + 10],
                         [l.replace("FS1_", "OV2_") for l in fullScreen if not l.endswith("AslPath")])
        self.assertNotIn("TK1_AslPath", script.MESSAGES_LABELS)

    def test_labels_are_unique(self):
        self.assertEqual(len(set(script.MESSAGES_LABELS)), 165)


class FakeSheet(object):
    """A DataPipe sheet as the cell mapping sees it: cells addressed (column, row) from 1, blank
    cells read back as None, and numbers read back as floats, as Moxie reads "5" back as 5.0
    (section 4.2, rule 2). Every write is recorded in order."""

    def __init__(self):
        self.cells = {}
        self.writes = []

    def Read(self, column, row):
        return self.cells.get((column, row))

    def Write(self, column, row, value):
        self.writes.append((column, row, value))
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            value = float(value)
        self.cells[(column, row)] = None if value == "" else value

    def Cell(self, row, label):
        return self.cells.get((script.MESSAGES_LABELS.index(label) + 1, row))

    def Row(self, row):
        return dict((label, self.Cell(row, label)) for label in script.MESSAGES_LABELS)

    def WrittenLabels(self):
        """The labels of the cells written since the last call, in write order, as (row, label)."""
        written = [(row, script.MESSAGES_LABELS[column - 1]) for column, row, value in self.writes]
        self.writes = []
        return written


def Slot(rank, typeCode, english, versionId, french=None, textColour="#FFFFFF",
         backgroundColour="#C8102E", showSeconds=8, hideSeconds=4, endUtc=None, **extra):
    entry = {"rank": rank, "typeCode": typeCode,
             "texts": {"eng": english, "fra": french if french is not None else english + " FR"},
             "textColour": textColour, "backgroundColour": backgroundColour,
             "showSeconds": showSeconds, "hideSeconds": hideSeconds, "endUtc": endUtc,
             "shownVersionId": versionId}
    entry.update(extra)
    return entry


def Display(displayId, fullScreen=(), overlay=(), ticker=()):
    return {"displayId": displayId, "fullScreen": list(fullScreen), "overlay": list(overlay),
            "ticker": list(ticker)}


def Delivery(*displays):
    return {"schema": 1, "displays": list(displays)}


def ApplyDelivery(loaded, document, readCell, writeCell, writeLine):
    """The Messages sheet hand-off as WriteDeliveryToSheet makes it, on a stand-in sheet."""
    return script.ApplyWrites(loaded, "Messages sheet", script.PlanDeliveryWrites(loaded, document, readCell),
                              writeCell, writeLine)


EMERGENCY = Slot(1, "EMERGENCY", "Evacuate the terminal", 5001, french="Evacuez le terminal",
                 textColour="#ffffff", backgroundColour="#c8102e", showSeconds=10, hideSeconds=0,
                 endUtc="2026-10-17T21:30:00Z", aslPath=r"\\gtaa-share\ink\asl\evacuate.mp4")
IRROPS = Slot(2, "IRROPS", "AC 123 now departs from gate D24", 5002, textColour="#111111",
              backgroundColour="#FFC72C")
PAGING = Slot(3, "VPAGE", "Paging Ms Smith", 5003, backgroundColour="#003A70", showSeconds=12,
              hideSeconds=3)


class CellMappingCase(unittest.TestCase):

    def setUp(self):
        self.config = Load(Minimal())
        self.sheet = FakeSheet()
        self.lines = []

    def Apply(self, document, config=None):
        return ApplyDelivery(config or self.config, document, self.sheet.Read,
                             self.sheet.Write, self.lines.append)


class FirstDelivery(CellMappingCase):
    """An empty sheet takes the labels and one row per display (section 4.2)."""

    def test_row_1_takes_the_labels(self):
        self.Apply(Delivery(Display(1101)))
        self.assertEqual([self.sheet.cells.get((column, 1)) for column in range(1, 166)],
                         script.MESSAGES_LABELS)

    def test_each_display_gets_a_row_in_the_order_received(self):
        self.Apply(Delivery(Display(1101), Display(2205), Display(2310)))
        self.assertEqual([self.sheet.Cell(row, "DisplayId") for row in (2, 3, 4)],
                         [1101, 2205, 2310])

    def test_slot_cells_map_the_entry_fields(self):
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY], overlay=[IRROPS], ticker=[PAGING])))
        row = self.sheet.Row(2)
        self.assertEqual(row["FS1_Rank"], 1)
        self.assertEqual(row["FS1_Type"], "EMERGENCY")
        self.assertEqual(row["FS1_TextEN"], "Evacuate the terminal")
        self.assertEqual(row["FS1_TextFR"], "Evacuez le terminal")
        self.assertEqual(row["FS1_ShowSec"], 10)
        self.assertEqual(row["FS1_HideSec"], 0)
        self.assertEqual(row["FS1_EndUtc"], "2026-10-17T21:30:00Z")
        self.assertEqual(row["FS1_AslPath"], r"\\gtaa-share\ink\asl\evacuate.mp4")
        self.assertEqual(row["FS1_VersionId"], 5001)
        self.assertEqual(row["OV1_Type"], "IRROPS")
        self.assertEqual(row["OV1_TextEN"], "AC 123 now departs from gate D24")
        self.assertEqual(row["OV1_BackColour"], "#FFC72C")
        self.assertEqual(row["OV1_VersionId"], 5002)
        self.assertEqual(row["TK1_Rank"], 3)
        self.assertEqual(row["TK1_ShowSec"], 12)
        self.assertEqual(row["TK1_VersionId"], 5003)

    def test_numbers_are_written_as_numbers_and_text_as_text(self):
        self.Apply(Delivery(Display(1101, overlay=[IRROPS])))
        written = dict((script.MESSAGES_LABELS[column - 1], value)
                       for column, row, value in self.sheet.writes if row == 2)
        for label in ("DisplayId", "OV1_Rank", "OV1_ShowSec", "OV1_HideSec", "OV1_VersionId"):
            self.assertTrue(isinstance(written[label], int) and not isinstance(written[label], bool), label)
        for label in ("OV1_Type", "OV1_TextEN", "OV1_TextColour"):
            self.assertIsInstance(written[label], str, label)

    def test_colours_are_upper_case(self):
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY])))
        self.assertEqual(self.sheet.Cell(2, "FS1_TextColour"), "#FFFFFF")
        self.assertEqual(self.sheet.Cell(2, "FS1_BackColour"), "#C8102E")

    def test_text_is_read_by_the_configured_language_codes(self):
        entry = Slot(2, "IRROPS", "unused", 7001)
        entry["texts"] = {"en-CA": "Gate change", "fr-CA": "Changement de porte"}
        config = Load(Minimal(englishLanguageCode="en-CA", frenchLanguageCode="fr-CA"))
        self.Apply(Delivery(Display(1101, overlay=[entry])), config)
        self.assertEqual(self.sheet.Cell(2, "OV1_TextEN"), "Gate change")
        self.assertEqual(self.sheet.Cell(2, "OV1_TextFR"), "Changement de porte")

    def test_a_d02_body_fills_both_text_cells_with_the_default_codes(self):
        # Under D-02 Director keys each entry's texts by the catalogue codes eng and fra.
        entry = Slot(2, "IRROPS", "unused", 7002)
        entry["texts"] = {"eng": "Gate change", "fra": "Changement de porte"}
        self.Apply(Delivery(Display(1101, overlay=[entry])), Load(Minimal()))
        self.assertEqual(self.sheet.Cell(2, "OV1_TextEN"), "Gate change")
        self.assertEqual(self.sheet.Cell(2, "OV1_TextFR"), "Changement de porte")

    def test_empty_values_are_blank_cells_never_placeholders(self):
        entry = Slot(1, "EMERGENCY", "Evacuate", 5001, endUtc=None, aslPath=None)
        entry["texts"] = {"eng": "Evacuate"}
        self.Apply(Delivery(Display(1101, fullScreen=[entry])))
        for label in ("FS1_TextFR", "FS1_EndUtc", "FS1_AslPath", "FS2_Rank", "FS2_VersionId",
                      "OV1_HideSec", "OV1_VersionId", "Layout"):
            self.assertIsNone(self.sheet.Cell(2, label), label)
        values = [value for column, row, value in self.sheet.writes]
        for placeholder in ("null", "None", "NULL"):
            self.assertNotIn(placeholder, values)
        # DisplayId and FS1's 8 filled cells; a blank cell on an empty sheet is not written at all.
        self.assertEqual(len([1 for column, row, value in self.sheet.writes if row == 2]), 9)

    def test_paths_and_end_times_pass_through_exactly(self):
        entry = Slot(1, "EMERGENCY", "Evacuate", 5001, endUtc="2026-10-17T21:30:00.000Z",
                     aslPath="https://cdn.example.test/asl/Evacuate%20EN.mp4")
        self.Apply(Delivery(Display(1101, fullScreen=[entry])))
        self.assertEqual(self.sheet.Cell(2, "FS1_EndUtc"), "2026-10-17T21:30:00.000Z")
        self.assertEqual(self.sheet.Cell(2, "FS1_AslPath"), "https://cdn.example.test/asl/Evacuate%20EN.mp4")


class CompareFirst(CellMappingCase):
    """Section 4.2, rule 1: a cell is written only when its value differs, labels included."""

    def test_a_steady_response_writes_0_cells(self):
        document = Delivery(Display(1101, fullScreen=[EMERGENCY], overlay=[IRROPS], ticker=[PAGING]),
                            Display(2205))
        self.Apply(document)
        self.sheet.writes = []
        self.assertEqual(self.Apply(document), 0)
        self.assertEqual(self.sheet.writes, [])

    def test_numbers_are_compared_as_numbers(self):
        self.Apply(Delivery(Display(1101, overlay=[IRROPS])))
        # Moxie reads "8" back as 8.0, or hands back text such as "8.0".
        self.sheet.cells[(script.MESSAGES_LABELS.index("OV1_ShowSec") + 1, 2)] = "8.0"
        self.sheet.cells[(1, 2)] = "1101.0"
        self.sheet.writes = []
        self.assertEqual(self.Apply(Delivery(Display(1101, overlay=[IRROPS]))), 0)

    def test_colours_from_the_route_in_lower_case_match_upper_case_cells(self):
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY])))
        self.sheet.writes = []
        self.assertEqual(self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY]))), 0)

    def test_text_moxie_took_as_a_number_is_not_rewritten(self):
        entry = Slot(3, "VPAGE", "2026", 5003)
        self.Apply(Delivery(Display(1101, ticker=[entry])))
        self.sheet.cells[(script.MESSAGES_LABELS.index("TK1_TextEN") + 1, 2)] = 2026.0
        self.sheet.writes = []
        self.assertEqual(self.Apply(Delivery(Display(1101, ticker=[entry]))), 0)

    def test_only_a_label_that_differs_is_written(self):
        self.Apply(Delivery(Display(1101)))
        self.sheet.cells[(ColumnNumber("AI"), 1)] = "OV1_Rank (edited)"
        self.sheet.writes = []
        self.assertEqual(self.Apply(Delivery(Display(1101))), 1)
        self.assertEqual(self.sheet.writes, [(ColumnNumber("AI"), 1, "OV1_Rank")])

    def test_a_zero_is_a_value_not_a_blank(self):
        # HideSec 0 keeps the text on screen (Ink, section 4.2), so it must not read as blank.
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY])))
        self.assertEqual(self.sheet.Cell(2, "FS1_HideSec"), 0)
        self.sheet.writes = []
        changed = dict(EMERGENCY, hideSeconds=None)
        self.Apply(Delivery(Display(1101, fullScreen=[changed])))
        self.assertEqual(self.sheet.WrittenLabels(), [(2, "FS1_HideSec")])
        self.assertIsNone(self.sheet.Cell(2, "FS1_HideSec"))


def Tied(name, versionId, show, hide):
    return Slot(2, "IRROPS", "Overlay " + name, versionId, french="Superposition " + name,
                showSeconds=show, hideSeconds=hide)


A = Tied("A", 6001, 8, 4)
B = Tied("B", 6002, 6, 2)
C = Tied("C", 6003, 10, 0)
D = Tied("D", 6004, 7, 3)


class WriteOrder(CellMappingCase):
    """Section 4.2 "Write order" and "Slots": what a changed slot writes, in what order."""

    def setUp(self):
        CellMappingCase.setUp(self)
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY], overlay=[A, B, C], ticker=[PAGING])))
        self.sheet.WrittenLabels()

    def Next(self, overlay):
        self.Apply(Delivery(Display(1101, fullScreen=[EMERGENCY], overlay=overlay, ticker=[PAGING])))
        return self.sheet.WrittenLabels()

    def assertSlot(self, slot, entry):
        prefix = "OV" + str(slot) + "_"
        self.assertEqual(self.sheet.Cell(2, prefix + "TextEN"), entry["texts"]["eng"])
        self.assertEqual(self.sheet.Cell(2, prefix + "ShowSec"), entry["showSeconds"])
        self.assertEqual(self.sheet.Cell(2, prefix + "HideSec"), entry["hideSeconds"])
        self.assertEqual(self.sheet.Cell(2, prefix + "VersionId"), entry["shownVersionId"])

    def test_a_changed_overlay_slot_writes_only_the_cells_that_differ_then_its_versionid(self):
        revised = dict(B, texts={"eng": "Overlay B revised", "fra": B["texts"]["fra"]}, shownVersionId=6102)
        written = self.Next([A, revised, C])
        self.assertEqual(written, [(2, "OV2_TextEN"), (2, "OV2_VersionId")])

    def test_under_rotate_ending_the_oldest_moves_slots_2_and_3_up_and_blanks_slot_3_versionid_first(self):
        written = self.Next([B, C])
        self.assertEqual(written[0], (2, "OV3_VersionId"))
        self.assertEqual(written[-2:], [(2, "OV1_VersionId"), (2, "OV2_VersionId")])
        # Slot 1 takes B and slot 2 takes C; only their differing cells are written. B and C
        # share A's type, rank and colours, so those cells stay.
        self.assertEqual(set(written[1:-2]), set([
            (2, "OV1_TextEN"), (2, "OV1_TextFR"), (2, "OV1_ShowSec"), (2, "OV1_HideSec"),
            (2, "OV2_TextEN"), (2, "OV2_TextFR"), (2, "OV2_ShowSec"), (2, "OV2_HideSec"),
            (2, "OV3_Rank"), (2, "OV3_Type"), (2, "OV3_TextEN"), (2, "OV3_TextFR"),
            (2, "OV3_TextColour"), (2, "OV3_BackColour"), (2, "OV3_ShowSec"), (2, "OV3_HideSec"),
        ]))
        self.assertSlot(1, B)
        self.assertSlot(2, C)
        for cell in script.PART_SLOT_CELLS:
            self.assertIsNone(self.sheet.Cell(2, "OV3_" + cell), cell)

    def test_a_fourth_tie_drops_the_oldest_and_appends_the_newest(self):
        written = self.Next([B, C, D])
        self.assertEqual(written[-3:], [(2, "OV1_VersionId"), (2, "OV2_VersionId"), (2, "OV3_VersionId")])
        self.assertSlot(1, B)
        self.assertSlot(2, C)
        self.assertSlot(3, D)

    def test_new_text_never_lands_after_its_versionid(self):
        for overlay in ([B, C], [B, C, D], [D], [A, B, C], [], [C]):
            written = self.Next(overlay)
            for index in range(len(written)):
                row, label = written[index]
                if label.endswith("_VersionId") and self.sheet.Cell(2, label) is not None:
                    slot = label[:-len("VersionId")]
                    later = [l for r, l in written[index + 1:] if l.startswith(slot)]
                    self.assertEqual(later, [], (overlay, label))

    def test_content_going_off_blanks_every_versionid_before_any_other_cell(self):
        written = self.Next([])
        self.assertEqual(written[:3], [(2, "OV1_VersionId"), (2, "OV2_VersionId"), (2, "OV3_VersionId")])
        for cell in script.PART_SLOT_CELLS:
            for slot in (1, 2, 3):
                self.assertIsNone(self.sheet.Cell(2, "OV" + str(slot) + "_" + cell))
        self.assertEqual(self.sheet.Cell(2, "FS1_VersionId"), 5001)
        self.assertEqual(self.sheet.Cell(2, "TK1_VersionId"), 5003)

    def test_no_cell_is_ever_written_twice_in_one_delivery(self):
        for overlay in ([B, C], [B, C, D], [D], [], [A, B, C]):
            written = self.Next(overlay)
            self.assertEqual(len(written), len(set(written)), overlay)

    def test_the_script_keeps_no_slot_state(self):
        # A fresh start against the same sheet writes the same cells as the long-running script.
        self.Next([B, C])
        restarted = ApplyDelivery(self.config, Delivery(Display(1101, fullScreen=[EMERGENCY],
                                  overlay=[B, C], ticker=[PAGING])),
                                  self.sheet.Read, self.sheet.Write, self.lines.append)
        self.assertEqual(restarted, 0)


class SheetRows(CellMappingCase):
    """Section 4.2 "Rows" and "Growth" (INK-15, MSG-10)."""

    def setUp(self):
        CellMappingCase.setUp(self)
        self.Apply(Delivery(Display(1101, overlay=[IRROPS]), Display(2205, ticker=[PAGING])))
        self.sheet.WrittenLabels()
        self.lines = []

    def test_a_new_display_is_appended_at_the_bottom(self):
        self.Apply(Delivery(Display(2310, overlay=[IRROPS]), Display(1101, overlay=[IRROPS]),
                            Display(2205, ticker=[PAGING])))
        self.assertEqual([self.sheet.Cell(row, "DisplayId") for row in (2, 3, 4)], [1101, 2205, 2310])
        self.assertEqual(set(row for row, label in self.sheet.WrittenLabels()), set([4]))
        self.assertEqual(len(self.lines), 1)
        self.assertIn("2310", self.lines[0])
        self.assertIn("row 4", self.lines[0])

    def test_rows_are_never_moved_or_sorted(self):
        self.assertEqual(self.Apply(Delivery(Display(2205, ticker=[PAGING]), Display(1101, overlay=[IRROPS]))), 0)
        self.assertEqual(self.sheet.Cell(2, "DisplayId"), 1101)
        self.assertEqual(self.sheet.Cell(3, "DisplayId"), 2205)

    def test_a_display_missing_from_the_response_keeps_its_row_and_is_logged(self):
        self.Apply(Delivery(Display(2205)))
        self.assertEqual(set(row for row, label in self.sheet.WrittenLabels()), set([3]))
        self.assertEqual(self.sheet.Cell(2, "OV1_VersionId"), 5002)
        self.assertEqual(len(self.lines), 1)
        self.assertIn("1101 (row 2)", self.lines[0])
        self.assertIn("untouched", self.lines[0])

    def test_a_display_with_nothing_to_show_goes_blank_through_normal_writes(self):
        self.Apply(Delivery(Display(1101), Display(2205, ticker=[PAGING])))
        self.assertEqual(self.sheet.WrittenLabels()[0], (2, "OV1_VersionId"))
        self.assertEqual(self.sheet.Cell(2, "DisplayId"), 1101)
        for label in script.MESSAGES_LABELS[1:]:
            self.assertIsNone(self.sheet.Cell(2, label), label)

    def test_the_counter_columns_are_written_blank_in_1_1(self):
        # Even a 1.2 shaped display (layout, counters) leaves CQ to FI blank, and a stray value
        # there is blanked, Layout first as it turns content off.
        self.sheet.cells[(ColumnNumber("CQ"), 2)] = "Halves"
        self.sheet.cells[(ColumnNumber("CR"), 2)] = "310"
        display = Display(1101, overlay=[IRROPS])
        display["layout"] = "PageA"
        display["counters"] = [{"counterId": "310", "open": True, "publishId": 9001}]
        self.Apply(Delivery(display, Display(2205, ticker=[PAGING])))
        self.assertEqual(self.sheet.WrittenLabels(), [(2, "Layout"), (2, "CA_CounterId")])
        for column in range(ColumnNumber("CQ"), ColumnNumber("FI") + 1):
            self.assertIsNone(self.sheet.cells.get((column, 2)))

    def test_parts_are_passed_through_as_received(self):
        # The script does no blanking of its own (MSG-10): an EndUtc already in the past is
        # Ink's to hide, and the slot keeps it until Director's delivery changes.
        past = Slot(2, "IRROPS", "Old news", 5009, endUtc="2000-01-01T00:00:00Z")
        self.Apply(Delivery(Display(1101, overlay=[past]), Display(2205, ticker=[PAGING])))
        self.assertEqual(self.sheet.Cell(2, "OV1_EndUtc"), "2000-01-01T00:00:00Z")
        self.assertEqual(self.sheet.Cell(2, "OV1_VersionId"), 5009)


class CellMappingLogging(CellMappingCase):

    def test_with_debug_on_the_write_count_is_logged(self):
        config = Load(Minimal(debug=True))
        document = Delivery(Display(1101, overlay=[IRROPS]))
        self.Apply(document, config)
        self.lines = []
        self.Apply(document, config)
        self.assertEqual(len(self.lines), 1)
        self.assertIn("0 cells written", self.lines[0])

    def test_with_debug_off_a_steady_delivery_logs_nothing(self):
        document = Delivery(Display(1101, overlay=[IRROPS]))
        self.Apply(document)
        self.lines = []
        self.Apply(document)
        self.assertEqual(self.lines, [])

    def test_no_line_carries_message_text(self):
        config = Load(Minimal(debug=True))
        marked = Slot(1, "EMERGENCY", "Evacuate " + BODY_MARKER, 5001)
        self.Apply(Delivery(Display(1101, fullScreen=[marked]), Display(2205)), config)
        self.Apply(Delivery(Display(2310, fullScreen=[marked])), config)
        self.assertGreater(len(self.lines), 0)
        for line in self.lines:
            self.assertNotIn(BODY_MARKER, line)


class PullIntoTheSheet(unittest.TestCase):
    """The pull and the cell mapping together, against the stub route."""

    def setUp(self):
        self.config = Load(Minimal())
        self.route = StubDeliveryRoute(Delivery(Display(1101, overlay=[A, B, C])))
        self.state = script.NewPullState()
        self.sheet = FakeSheet()
        self.lines = []

    def Pull(self, writeCell=None):
        return script.PullDelivery(
            self.config, self.state, self.route.Fetch, json.loads,
            lambda document: ApplyDelivery(self.config, document, self.sheet.Read,
                                           writeCell or self.sheet.Write, self.lines.append),
            self.lines.append)

    def test_a_mapping_stopped_part_way_is_finished_by_the_next_pull(self):
        self.Pull()
        self.route.Publish(Delivery(Display(1101, overlay=[B, C, D])))
        calls = []

        def Flaky(column, row, value):
            calls.append(1)
            if len(calls) > 3:
                raise IOError("sheet locked")
            self.sheet.Write(column, row, value)

        self.assertEqual(self.Pull(Flaky), script.PULL_FAILED)
        self.assertEqual(self.Pull(), script.PULL_CHANGED)
        self.assertEqual(self.sheet.Cell(2, "OV3_VersionId"), 6004)
        self.assertEqual(self.sheet.Cell(2, "OV3_TextEN"), "Overlay D")
        self.sheet.writes = []
        self.route.Publish(Delivery(Display(1101, overlay=[B, C, D])))
        self.route.etag = '"forced"'
        self.state["etag"] = None
        self.assertEqual(self.Pull(), script.PULL_CHANGED)
        self.assertEqual(self.sheet.writes, [])


class ScriptedWait(object):
    """Stands in for the nudge event's wait: answers each wait from a script (True = a nudge,
    False = the safety net ran out) and records the timeout asked for."""

    def __init__(self, *answers):
        self.answers = list(answers)
        self.timeouts = []

    def __call__(self, seconds):
        self.timeouts.append(seconds)
        return self.answers.pop(0)

    def Left(self):
        return len(self.answers) > 0


class DeliveryLoop(unittest.TestCase):
    """One loop does every pull, after a nudge or when the safety net runs out (INK-02)."""

    def setUp(self):
        self.config = Load(Minimal())
        self.pulls = []
        self.lines = []

    def Run(self, wait, config=None, pull=None):
        script.RunDeliveryLoop(config or self.config, wait, pull or (lambda: self.pulls.append(1)),
                               self.lines.append, wait.Left)

    def test_it_pulls_at_start_then_after_every_nudge_or_safety_net(self):
        wait = ScriptedWait(True, False, True)
        self.Run(wait)
        self.assertEqual(len(self.pulls), 4)

    def test_each_wait_lasts_at_most_the_safety_net_interval(self):
        wait = ScriptedWait(False, False)
        self.Run(wait)
        self.assertEqual(wait.timeouts, [12, 12])
        wait = ScriptedWait(False)
        self.Run(wait, Load(Minimal(safetyNetIntervalSeconds=15)))
        self.assertEqual(wait.timeouts, [15])

    def test_with_debug_on_each_pull_says_what_woke_it_and_with_debug_off_nothing(self):
        self.Run(ScriptedWait(True, False), Load(Minimal(debug=True)))
        self.assertEqual(len(self.lines), 2)
        self.assertIn("nudge", self.lines[0])
        self.assertIn("safety net", self.lines[1])
        self.lines = []
        self.Run(ScriptedWait(True, False))
        self.assertEqual(self.lines, [])

    def test_a_pull_that_raises_does_not_end_the_loop_and_is_logged_once_by_type(self):
        outcomes = [ValueError("cell text " + BODY_MARKER), ValueError("again"), None, KeyError("x")]

        def Pull():
            self.pulls.append(1)
            outcome = outcomes.pop(0)
            if outcome is not None:
                raise outcome

        self.Run(ScriptedWait(False, False, False), pull=Pull)
        self.assertEqual(len(self.pulls), 4)
        self.assertEqual(len(self.lines), 2)
        self.assertIn("ValueError", self.lines[0])
        self.assertIn("KeyError", self.lines[1])
        for line in self.lines:
            self.assertNotIn(BODY_MARKER, line)


class DoorbellListener(unittest.TestCase):
    """INK-09: the listener answers 200 at once and only signals the loop. It is never given
    the sheet or the pull, so it cannot write a cell; a nudge's body is never read."""

    def setUp(self):
        self.config = Load(Minimal(listenerEnabled=True, listenerPort=8091))
        self.events = []
        self.lines = []

    def Answer(self, status):
        self.events.append(("answer", status))

    def Signal(self):
        self.events.append(("signal",))

    def test_a_nudge_is_answered_200_and_signals_the_loop(self):
        script.HandleNudge(self.Answer, self.Signal)
        self.assertEqual(self.events, [("answer", 200), ("signal",)])

    def test_a_nudge_whose_answer_fails_still_signals_the_loop(self):
        def Broken(status):
            raise IOError("client went away")
        script.HandleNudge(Broken, self.Signal)
        self.assertEqual(self.events, [("signal",)])

    def test_it_serves_nudges_until_the_listener_closes(self):
        requests = [self.Answer, self.Answer, script.ListenerClosed()]

        def Accept():
            request = requests.pop(0)
            if isinstance(request, Exception):
                raise request
            return request

        script.ServeNudges(self.config, Accept, self.Signal, self.lines.append)
        self.assertEqual(self.events, [("answer", 200), ("signal",)] * 2)
        self.assertEqual(len(self.lines), 1)
        self.assertIn("listener stopped", self.lines[0])

    def test_a_request_that_fails_before_it_is_accepted_does_not_stop_the_listener(self):
        requests = [IOError("connection reset"), self.Answer, script.ListenerClosed()]

        def Accept():
            request = requests.pop(0)
            if isinstance(request, Exception):
                raise request
            return request

        script.ServeNudges(self.config, Accept, self.Signal, self.lines.append)
        self.assertEqual(self.events, [("answer", 200), ("signal",)])


class ScriptStart(unittest.TestCase):
    """One long-lived copy. A Moxie timed task starts the script again and again as a
    watchdog; a copy that finds another running, or the listener port in use, exits quietly. With
    the listener off the loop is pull only."""

    def setUp(self):
        self.events = []
        self.lines = []
        self.copyRunning = False
        self.listenerFailure = None
        self.document = Minimal()

    def Acquire(self):
        self.events.append("acquire")
        if self.copyRunning:
            return None
        return lambda: self.events.append("release")

    def StartUp(self):
        self.events.append("start up")
        self.loaded = Load(self.document)
        return self.loaded

    def StartListener(self, loaded, prefix, signal):
        self.events.append(("listener", prefix))
        if self.listenerFailure is not None:
            raise self.listenerFailure
        self.listenerConfig = loaded
        self.signal = signal
        return lambda: self.events.append("listener stopped")

    def NewNudgeEvent(self):
        return ScriptedWait(False), lambda: None

    def Pull(self):
        self.events.append("pull")

    def Run(self):
        return script.RunScript(self.Acquire, self.StartUp, self.StartListener, self.NewNudgeEvent,
                                self.Pull, self.lines.append, keepRunning=lambda: False)

    def test_with_the_listener_off_the_loop_is_pull_only(self):
        self.assertEqual(self.Run(), script.RUN_STOPPED)
        self.assertEqual(self.events, ["acquire", "start up", "pull", "release"])

    def test_with_the_listener_on_it_listens_on_the_configured_port_and_path(self):
        self.document = Minimal(listenerEnabled=True, listenerPort=8091)
        self.Run()
        self.assertEqual(self.events, ["acquire", "start up", ("listener", "http://*:8091/gtaa-doorbell/"),
                                       "pull", "listener stopped", "release"])

    def test_the_listener_is_given_the_configuration_start_up_loaded(self):
        self.document = Minimal(listenerEnabled=True, listenerPort=8091, debug=True)
        self.Run()
        self.assertIs(self.listenerConfig, self.loaded)

    def test_a_copy_stopped_part_way_frees_the_port_for_the_next_copy(self):
        # Moxie stops a script by aborting its thread, which surfaces in the loop as an exception.
        self.document = Minimal(listenerEnabled=True, listenerPort=8091)

        def Aborted():
            self.events.append("pull")
            raise SystemExit()

        self.Pull = Aborted
        with self.assertRaises(SystemExit):
            self.Run()
        self.assertEqual(self.events[-2:], ["listener stopped", "release"])

    def test_the_listener_path_is_used_with_one_slash_each_side(self):
        self.document = Minimal(listenerEnabled=True, listenerPort=8443, listenerPath="/doorbell/")
        self.Run()
        self.assertIn(("listener", "http://*:8443/doorbell/"), self.events)

    def test_a_second_copy_exits_quietly_before_anything_else(self):
        self.copyRunning = True
        self.assertEqual(self.Run(), script.RUN_ALREADY_RUNNING)
        self.assertEqual(self.events, ["acquire"])
        self.assertEqual(self.lines, [])

    def test_when_the_port_is_in_use_it_exits_without_raising_or_pulling(self):
        self.document = Minimal(listenerEnabled=True, listenerPort=8091)
        self.listenerFailure = script.ListenerPortInUse("http://*:8091/gtaa-doorbell/")
        self.assertEqual(self.Run(), script.RUN_PORT_IN_USE)
        self.assertNotIn("pull", self.events)
        self.assertEqual(self.events[-1], "release")
        self.assertEqual(len(self.lines), 1)
        self.assertIn("8091", self.lines[0])

    def test_any_other_listener_failure_is_logged_and_raised(self):
        self.document = Minimal(listenerEnabled=True, listenerPort=8091)
        self.listenerFailure = IOError("Access is denied")
        with self.assertRaises(IOError):
            self.Run()
        self.assertNotIn("pull", self.events)
        self.assertEqual(self.events[-1], "release")
        self.assertIn("doorbell listener cannot start", self.lines[-1])

    def test_a_configuration_refusal_releases_the_copy_and_raises(self):
        del self.document["apiKey"]
        with self.assertRaises(script.ConfigError):
            self.Run()
        self.assertEqual(self.events, ["acquire", "start up", "release"])

    def test_only_an_address_conflict_counts_as_the_port_in_use(self):
        # HttpListener's Win32 codes: 183 a URL registration conflict, 32 a port another program
        # bound directly. 5 (access denied) is a missing URL ACL, a deployment fault to report.
        self.assertTrue(script.PortInUseError(183))
        self.assertTrue(script.PortInUseError(32))
        self.assertFalse(script.PortInUseError(5))
        self.assertFalse(script.PortInUseError(None))


class AutoResetEventStandIn(object):
    """.NET's AutoResetEvent: Set wakes one waiter, or the next one if none is waiting, and the
    event resets as the waiter wakes, so nudges that arrive during a pull become one more pull."""

    def __init__(self):
        self.condition = threading.Condition()
        self.signalled = False

    def Set(self):
        with self.condition:
            self.signalled = True
            self.condition.notify()

    def WaitOne(self, seconds):
        with self.condition:
            if not self.signalled:
                self.condition.wait(seconds)
            signalled, self.signalled = self.signalled, False
            return signalled


class NudgeQueue(object):
    """Requests reaching the listener, for ServeNudges' accept: Ring() is Director's POST."""

    def __init__(self):
        self.condition = threading.Condition()
        self.pending = []
        self.answers = []
        self.closed = False

    def Ring(self):
        with self.condition:
            self.pending.append(1)
            self.condition.notify()

    def Close(self):
        with self.condition:
            self.closed = True
            self.condition.notify()

    def Accept(self):
        with self.condition:
            while not self.pending and not self.closed:
                self.condition.wait()
            if self.closed:
                raise script.ListenerClosed()
            self.pending.pop(0)
            return self.answers.append


class DoorbellDoneWhen(unittest.TestCase):
    """With real threads (INK-02, INK-09): the loop thread is the only writer; a nudge triggers a
    pull within 1 second; with the listener blocked a change lands within the safety-net interval."""

    def StartScript(self, document):
        self.route = StubDeliveryRoute(Delivery(Display(1101, overlay=[A])))
        self.sheet = FakeSheet()
        self.state = script.NewPullState()
        self.lines = []
        self.writerThreads = set()
        self.nudges = NudgeQueue()
        self.stopping = threading.Event()
        loaded = Load(document)

        def WriteCell(column, row, value):
            self.writerThreads.add(threading.current_thread().name)
            self.sheet.Write(column, row, value)

        def Pull():
            return script.PullDelivery(
                loaded, self.state, self.route.Fetch, json.loads,
                lambda body: ApplyDelivery(loaded, body, self.sheet.Read, WriteCell, self.lines.append),
                self.lines.append)

        def NewNudgeEvent():
            event = AutoResetEventStandIn()
            return event.WaitOne, event.Set

        def StartListener(loaded, prefix, signal):
            listener = threading.Thread(target=script.ServeNudges, name="listener",
                                        args=(loaded, self.nudges.Accept, signal, self.lines.append))
            listener.daemon = True
            listener.start()

        self.loop = threading.Thread(
            target=script.RunScript, name="loop",
            args=(lambda: (lambda: None), lambda: loaded, StartListener, NewNudgeEvent, Pull,
                  self.lines.append),
            kwargs={"keepRunning": lambda: not self.stopping.is_set()})
        self.loop.daemon = True
        self.loop.start()
        self.WaitFor(lambda: self.sheet.Cell(2, "OV1_VersionId") == A["shownVersionId"], 2)

    def tearDown(self):
        self.stopping.set()
        self.nudges.Close()
        self.nudges.Ring()

    def WaitFor(self, condition, seconds):
        deadline = time.time() + seconds
        while time.time() < deadline:
            if condition():
                return time.time()
            time.sleep(0.01)
        self.fail("not within " + str(seconds) + " s")

    def Change(self):
        self.route.Publish(Delivery(Display(1101, overlay=[B])))
        return time.time()

    def Landed(self):
        return self.sheet.Cell(2, "OV1_VersionId") == B["shownVersionId"]

    def test_a_nudge_triggers_a_pull_within_1_second(self):
        self.StartScript(Minimal(listenerEnabled=True, listenerPort=8091))
        changed = self.Change()
        self.nudges.Ring()
        landed = self.WaitFor(self.Landed, 1)
        self.assertLess(landed - changed, 1)
        self.assertEqual(self.nudges.answers, [200])

    def test_with_the_listener_blocked_a_change_lands_within_the_safety_net_interval(self):
        self.StartScript(Minimal(listenerEnabled=True, listenerPort=8091, safetyNetIntervalSeconds=0.5))
        changed = self.Change()
        landed = self.WaitFor(self.Landed, 2)
        self.assertLess(landed - changed, 0.5 + 0.3)

    def test_with_the_listener_off_the_safety_net_alone_delivers(self):
        self.StartScript(Minimal(safetyNetIntervalSeconds=0.5))
        self.Change()
        self.WaitFor(self.Landed, 2)

    def test_every_cell_is_written_by_the_loop_never_by_the_listener(self):
        self.StartScript(Minimal(listenerEnabled=True, listenerPort=8091))
        self.Change()
        for ring in range(5):
            self.nudges.Ring()
        self.WaitFor(self.Landed, 1)
        self.assertEqual(self.writerThreads, set(["loop"]))


def RuleValuesBody(schema=1, values=None):
    """A Business Rule values document in the 1.2 BRE-12 shape: schema 1 and rows of key, value
    and unit, in catalogue order."""
    if values is None:
        values = [
            {"key": "pa.display-seconds", "value": 10, "unit": "seconds"},
            {"key": "pa.hide-seconds", "value": 30, "unit": "seconds"},
            {"key": "flights.roll-on-minutes", "value": 240, "unit": "minutes"},
        ]
    return {"schema": schema, "values": values}


class RuleValuesPull(unittest.TestCase):
    """The Business Rule values route is pulled the same way as the delivery route, with its
    own ETag; a 404 (no route until 1.2, or the flag off) writes nothing (INK-13, A-23)."""

    def setUp(self):
        self.config = Load(Minimal(directorBaseUrl="https://director.example.test/"))
        self.route = StubDeliveryRoute(RuleValuesBody())
        self.sheet = RecordingSheet()
        self.state = script.NewPullState()
        self.lines = []

    def Pull(self, config=None):
        return script.PullRuleValues(config or self.config, self.state, self.route.Fetch, json.loads,
                                     self.sheet.HandOff, self.lines.append)

    def test_a_first_pull_hands_the_document_over_and_stores_its_own_etag(self):
        self.assertEqual(self.Pull(), script.PULL_CHANGED)
        self.assertEqual(self.route.requests,
                         [("https://director.example.test/api/messaging/rule-values", None)])
        self.assertEqual(self.sheet.received, [RuleValuesBody()])
        self.assertEqual(self.state["etag"], self.route.etag)

    def test_the_configured_path_is_pulled(self):
        self.Pull(Load(Minimal(ruleValuesPath="/director/api/rule-values")))
        self.assertEqual(self.route.requests[0][0], "https://director.example.test/director/api/rule-values")

    def test_a_304_writes_nothing(self):
        self.Pull()
        self.assertEqual(self.Pull(), script.PULL_UNCHANGED)
        self.assertEqual(self.route.requests[1][1], self.route.etag)
        self.assertEqual(len(self.sheet.received), 1)

    def test_a_404_writes_nothing_and_keeps_the_etag(self):
        self.Pull()
        goodEtag = self.state["etag"]
        self.route.Publish(RuleValuesBody(values=[]))
        self.route.failure = 404
        self.assertEqual(self.Pull(), script.PULL_ABSENT)
        self.assertEqual(len(self.sheet.received), 1)
        self.assertEqual(self.state["etag"], goodEtag)

    def test_an_error_or_a_timeout_keeps_every_value(self):
        self.Pull()
        goodEtag = self.state["etag"]
        self.route.Publish(RuleValuesBody(values=[]))
        for failure in ("timeout", "unreachable", 500, 503, 401, 403):
            with self.subTest(failure=failure):
                self.route.failure = failure
                self.assertEqual(self.Pull(), script.PULL_FAILED)
                self.assertEqual(len(self.sheet.received), 1)
                self.assertEqual(self.state["etag"], goodEtag)

    def test_an_unknown_schema_or_a_body_that_is_not_a_values_document_keeps_every_value(self):
        self.Pull()
        goodEtag = self.state["etag"]
        for schema in (2, 0, "1", True, None):
            with self.subTest(schema=schema):
                self.route.Publish(RuleValuesBody(schema=schema))
                self.assertEqual(self.Pull(), script.PULL_FAILED)
        for body in ("<html>" + BODY_MARKER + "</html>", "[]", '{"schema": 1}',
                     '{"schema": 1, "values": {}}', json.dumps(DeliveryBody())):
            with self.subTest(body=body):
                self.route.body = body
                self.route.etag = '"other"'
                self.assertEqual(self.Pull(), script.PULL_FAILED)
        self.assertEqual(len(self.sheet.received), 1)
        self.assertEqual(self.state["etag"], goodEtag)

    def test_a_document_the_sheet_could_not_take_is_pulled_again(self):
        def Broken(document):
            raise IOError("sheet locked")
        outcome = script.PullRuleValues(self.config, self.state, self.route.Fetch, json.loads,
                                        Broken, self.lines.append)
        self.assertEqual(outcome, script.PULL_FAILED)
        self.assertIsNone(self.state["etag"])
        self.assertEqual(self.Pull(), script.PULL_CHANGED)


class RuleValuesPullLogging(unittest.TestCase):
    """In 1.1 the route answers 404 at every pull, so a 404 is one line, never an outage per pull."""

    def setUp(self):
        self.route = StubDeliveryRoute(RuleValuesBody())
        self.state = script.NewPullState()
        self.lines = []

    def Pull(self, debug=False):
        config = Load(Minimal(debug=debug))
        return script.PullRuleValues(config, self.state, self.route.Fetch, json.loads,
                                     lambda document: None, self.lines.append)

    def test_a_404_at_every_pull_is_logged_once(self):
        self.route.failure = 404
        for _ in range(5):
            self.Pull()
        self.assertEqual(len(self.lines), 1)
        self.assertIn("rule values", self.lines[0])
        self.assertIn("404", self.lines[0])
        self.assertNotIn("failed", self.lines[0])

    def test_the_route_being_served_after_404_is_logged_once(self):
        self.route.failure = 404
        self.Pull()
        self.route.failure = None
        self.Pull()
        self.Pull()
        self.assertEqual(len(self.lines), 2)
        self.assertIn("now served", self.lines[1])

    def test_an_outage_is_logged_once_when_it_starts_and_once_when_it_ends(self):
        self.Pull()
        self.route.failure = 503
        for _ in range(3):
            self.Pull()
        self.route.failure = None
        self.Pull()
        self.assertEqual(len(self.lines), 2)
        self.assertIn("rule values pull failed: HTTP 503", self.lines[0])
        self.assertIn("rule values pull recovered", self.lines[1])

    def test_with_debug_on_every_pull_is_logged_and_no_line_has_the_key_or_the_body(self):
        self.route.Publish(RuleValuesBody(values=[{"key": "pa.display-seconds", "value": BODY_MARKER,
                                                   "unit": "seconds"}]))
        for failure in (None, None, 404, 404, "timeout",
                        IOError("The remote server rejected X-API-KEY " + SECRET)):
            self.route.failure = failure
            self.Pull(debug=True)
        self.assertEqual(len(self.lines), 6)
        self.assertIn("1 rows", self.lines[0])
        self.assertIn("304", self.lines[1])
        self.assertIn("404", self.lines[3])
        for line in self.lines:
            self.assertNotIn(SECRET, line)
            self.assertNotIn(BODY_MARKER, line)


def Rule(key, value, unit):
    return {"key": key, "value": value, "unit": unit}


def ApplyRuleValues(loaded, document, readCell, writeCell, writeLine):
    """The Rule Values sheet hand-off as WriteRuleValuesToSheet makes it, on a stand-in sheet."""
    return script.ApplyWrites(loaded, "Rule Values sheet", script.PlanRuleValuesWrites(document, readCell),
                              writeCell, writeLine)


class RuleValuesSheet(unittest.TestCase):
    """Section 4.2: columns A key, B value, C unit, one row per key in the order received,
    under the same cell rules as the Messages sheet."""

    def setUp(self):
        self.config = Load(Minimal())
        self.sheet = FakeSheet()
        self.lines = []

    def Apply(self, values, config=None):
        return ApplyRuleValues(config or self.config, RuleValuesBody(values=values),
                               self.sheet.Read, self.sheet.Write, self.lines.append)

    def Rows(self):
        rows = []
        row = 1
        while any(self.sheet.Read(column, row) is not None for column in (1, 2, 3)):
            rows.append(tuple(self.sheet.Read(column, row) for column in (1, 2, 3)))
            row += 1
        return rows

    def test_a_sample_document_writes_the_labels_and_its_rows_in_the_order_received(self):
        self.Apply(RuleValuesBody()["values"])
        self.assertEqual(self.Rows(), [
            ("key", "value", "unit"),
            ("pa.display-seconds", 10.0, "seconds"),
            ("pa.hide-seconds", 30.0, "seconds"),
            ("flights.roll-on-minutes", 240.0, "minutes"),
        ])

    def test_numbers_are_written_as_numbers_and_text_as_text(self):
        self.Apply([Rule("pa.display-seconds", 10, "seconds"), Rule("x.ratio", 1.5, "ratio"),
                    Rule("x.mode", "Rotate", None)])
        written = dict(((column, row), value) for column, row, value in self.sheet.writes)
        self.assertIs(type(written[(2, 2)]), int)
        self.assertEqual(written[(2, 3)], 1.5)
        self.assertEqual(written[(2, 4)], "Rotate")

    def test_a_steady_document_writes_0_cells(self):
        values = RuleValuesBody()["values"] + [Rule("x.mode", "5", "")]
        self.Apply(values)
        self.sheet.writes = []
        self.assertEqual(self.Apply(values), 0)
        self.assertEqual(self.sheet.writes, [])

    def test_numbers_are_compared_as_numbers(self):
        self.Apply([Rule("pa.display-seconds", 10, "seconds")])
        # Moxie may hand a number cell back as a float or as text.
        for readBack in (10.0, "10", "10.0"):
            with self.subTest(readBack=readBack):
                self.sheet.cells[(2, 2)] = readBack
                self.sheet.writes = []
                self.assertEqual(self.Apply([Rule("pa.display-seconds", 10, "seconds")]), 0)

    def test_a_changed_value_writes_only_that_cell(self):
        self.Apply(RuleValuesBody()["values"])
        self.sheet.writes = []
        values = RuleValuesBody()["values"]
        values[1] = Rule("pa.hide-seconds", 45, "seconds")
        self.Apply(values)
        self.assertEqual(self.sheet.writes, [(2, 3, 45)])

    def test_empty_values_are_blank_cells_never_placeholders(self):
        self.Apply([Rule("pa.display-seconds", None, None), {"key": "pa.hide-seconds"},
                    Rule("x.zero", 0, "seconds")])
        self.assertEqual(self.Rows()[1:], [("pa.display-seconds", None, None),
                                           ("pa.hide-seconds", None, None),
                                           ("x.zero", 0.0, "seconds")])
        self.assertNotIn("null", [value for column, row, value in self.sheet.writes])

    def test_a_key_no_longer_sent_leaves_no_row_behind_and_is_logged(self):
        self.Apply(RuleValuesBody()["values"])
        values = RuleValuesBody()["values"]
        del values[1]
        self.sheet.writes = []
        self.Apply(values)
        self.assertEqual(self.Rows()[1:], [("pa.display-seconds", 10.0, "seconds"),
                                           ("flights.roll-on-minutes", 240.0, "minutes")])
        # The key cell of a row going blank is written last, so a mapping stopped part way still
        # finds the row at the next pull.
        self.assertEqual(self.sheet.writes[-3:], [(2, 4, ""), (3, 4, ""), (1, 4, "")])
        self.assertEqual(len(self.lines), 1)
        self.assertIn("pa.hide-seconds", self.lines[0])

    def test_rows_without_a_key_and_repeated_keys_are_skipped_and_logged(self):
        self.Apply([Rule("pa.display-seconds", 10, "seconds"), {"value": 3}, "not a row",
                    Rule("", 1, "x"), Rule("pa.display-seconds", 99, "seconds"),
                    Rule("pa.hide-seconds", 30, "seconds")])
        self.assertEqual(self.Rows()[1:], [("pa.display-seconds", 10.0, "seconds"),
                                           ("pa.hide-seconds", 30.0, "seconds")])
        self.assertEqual(len(self.lines), 2)

    def test_with_debug_on_the_write_count_is_logged_and_with_debug_off_a_steady_document_logs_nothing(self):
        self.Apply(RuleValuesBody()["values"])
        self.Apply(RuleValuesBody()["values"])
        self.assertEqual(self.lines, [])
        self.Apply(RuleValuesBody()["values"], Load(Minimal(debug=True)))
        self.assertEqual(len(self.lines), 1)
        self.assertIn("0 cells written", self.lines[0])


class RuleValuesInTheLoop(unittest.TestCase):
    """The loop's one pull takes both routes, so the doorbell and the safety net
    pull the Business Rule values too (INK-13), and neither route's trouble reaches the other."""

    def setUp(self):
        self.config = Load(Minimal())
        self.delivery = StubDeliveryRoute(Delivery(Display(1101, overlay=[A])))
        self.rules = StubDeliveryRoute(RuleValuesBody())
        self.messagesSheet = FakeSheet()
        self.rulesSheet = FakeSheet()
        self.deliveryState = script.NewPullState()
        self.rulesState = script.NewPullState()
        self.lines = []
        self.ruleHandOff = lambda document: ApplyRuleValues(
            self.config, document, self.rulesSheet.Read, self.rulesSheet.Write, self.lines.append)

    def PullDelivery(self):
        return script.PullDelivery(
            self.config, self.deliveryState, self.delivery.Fetch, json.loads,
            lambda document: ApplyDelivery(self.config, document, self.messagesSheet.Read,
                                           self.messagesSheet.Write, self.lines.append),
            self.lines.append)

    def PullRules(self):
        return script.PullRuleValues(self.config, self.rulesState, self.rules.Fetch, json.loads,
                                     lambda document: self.ruleHandOff(document), self.lines.append)

    def PullOnce(self):
        return script.PullEach((self.PullDelivery, self.PullRules))

    def test_every_pull_of_the_loop_takes_both_routes_after_a_nudge_or_the_safety_net(self):
        wait = ScriptedWait(True, False)
        script.RunDeliveryLoop(self.config, wait, self.PullOnce, self.lines.append, wait.Left)
        self.assertEqual(len(self.delivery.requests), 3)
        self.assertEqual(len(self.rules.requests), 3)
        self.assertEqual(self.rulesSheet.Read(1, 2), "pa.display-seconds")

    def test_a_404_leaves_the_rule_values_sheet_as_it_was(self):
        self.PullOnce()
        before = dict(self.rulesSheet.cells)
        self.rulesSheet.writes = []
        self.rules.Publish(RuleValuesBody(values=[Rule("pa.display-seconds", 99, "seconds")]))
        self.rules.failure = 404
        for _ in range(3):
            self.PullOnce()
        self.assertEqual(self.rulesSheet.writes, [])
        self.assertEqual(self.rulesSheet.cells, before)

    def test_in_1_1_the_404_writes_nothing_while_the_delivery_lands(self):
        self.rules.failure = 404
        self.PullOnce()
        self.assertEqual(self.rulesSheet.writes, [])
        self.assertEqual(self.messagesSheet.Cell(2, "OV1_VersionId"), A["shownVersionId"])

    def test_a_business_rule_change_never_rewrites_a_message_cell(self):
        self.PullOnce()
        self.messagesSheet.writes = []
        self.rules.Publish(RuleValuesBody(values=[Rule("pa.display-seconds", 12, "seconds")]))
        self.PullOnce()
        self.assertEqual(self.messagesSheet.writes, [])
        self.assertEqual(self.rulesSheet.Read(2, 2), 12.0)

    def test_a_rule_values_failure_never_stops_the_delivery(self):
        self.PullOnce()

        def Raises(document):
            raise IOError("Rule Values sheet locked")
        failures = [("timeout", None), ("unreachable", None), (500, None), (404, None),
                    (None, Raises)]
        overlays = [B, C, D, A, B]
        for (failure, handOff), entry in zip(failures, overlays):
            with self.subTest(failure=failure):
                self.rules.failure = failure
                self.rules.Publish(RuleValuesBody(values=[Rule("pa.display-seconds", entry["shownVersionId"], "s")]))
                if handOff is not None:
                    self.ruleHandOff = handOff
                self.delivery.Publish(Delivery(Display(1101, overlay=[entry])))
                self.PullOnce()
                self.assertEqual(self.messagesSheet.Cell(2, "OV1_VersionId"), entry["shownVersionId"])

    def test_a_rule_values_pull_that_raises_does_not_stop_the_delivery_and_the_loop_carries_on(self):
        delivered = []

        def Broken():
            raise RuntimeError("fault in the script")
        wait = ScriptedWait(False, False)
        script.RunDeliveryLoop(self.config, wait,
                               lambda: script.PullEach((lambda: delivered.append(1), Broken)),
                               self.lines.append, wait.Left)
        self.assertEqual(len(delivered), 3)
        self.assertEqual(len(self.lines), 1)
        self.assertIn("RuntimeError", self.lines[0])


class PullEachRunsEveryPull(unittest.TestCase):

    def test_a_pull_that_raises_does_not_keep_the_next_from_running_and_is_raised_after(self):
        ran = []

        def First():
            ran.append("first")
            raise KeyError("first")

        def Second():
            ran.append("second")
            raise ValueError("second")
        with self.assertRaises(KeyError):
            script.PullEach((First, Second, lambda: ran.append("third")))
        self.assertEqual(ran, ["first", "second", "third"])


class ExampleConfig(unittest.TestCase):

    def setUp(self):
        with open(os.path.join(SCRIPT_DIR, "config.example.json"), encoding="utf-8") as example:
            self.text = example.read()
        self.document = json.loads(self.text)

    def test_as_shipped_it_does_not_start_because_it_has_no_key(self):
        with self.assertRaises(script.ConfigError) as caught:
            script.LoadConfig(CONFIG_PATH, lambda p: self.text, json.loads)
        self.assertIn("apiKey", str(caught.exception))

    def test_with_a_key_it_loads(self):
        self.document["apiKey"] = SECRET
        config = Load(self.document)
        self.assertEqual(config["apiKey"], SECRET)

    def test_it_lists_every_setting_the_script_reads(self):
        names = set(name for name, kind, default in script.SETTINGS)
        listed = set(name for name in self.document if not name.startswith("_"))
        self.assertEqual(listed, names)


class ScriptStaysIronPython27(unittest.TestCase):
    """CPython 3 parses syntax that Moxie's IronPython 2.7 rejects, so a passing run here would not
    show the script fails to compile in Moxie. This catches the common Python 3 only forms."""

    def test_no_python3_only_syntax(self):
        path = os.path.join(SCRIPT_DIR, "GtaaMessageDelivery.py")
        with open(path, "rb") as source:
            tokens = list(tokenize.tokenize(source.readline))
        found = []
        for token in tokens:
            if token.type == getattr(tokenize, "FSTRING_START", -1):
                found.append((token.start[0], "f-string"))
            elif token.type == tokenize.STRING and "f" in re.match("[A-Za-z]*", token.string).group(0).lower():
                found.append((token.start[0], "f-string"))
            elif token.type == tokenize.NAME and token.string in ("nonlocal", "async", "await"):
                found.append((token.start[0], token.string))
            elif token.type == tokenize.OP and token.string in ("->", ":="):
                found.append((token.start[0], token.string))
        self.assertEqual(found, [])

    def test_source_is_ascii(self):
        # IronPython 2.7 needs a coding line for anything else.
        with open(os.path.join(SCRIPT_DIR, "GtaaMessageDelivery.py"), "rb") as source:
            source.read().decode("ascii")


if __name__ == "__main__":
    unittest.main()
