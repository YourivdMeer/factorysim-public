#!/usr/bin/env python3
"""Example test for the FactorySim test API (plan 21).

Opens a scene, connects the app to a PLC on 127.0.0.1, runs the simulation, presses Start,
waits, and judges the counters: PASS when items reached every remover and none fell.

Start the PLC first, for example the fake PLC:

    py tools/fake_plc/fake_plc.py --mode sorter --port 5020

Then switch the test API on in the app (PLC window, Test-API, or --api-port=5080) and run:

    py tools/api/run_test.py
    py tools/api/run_test.py --api-port 5080 --plc-port 5020 --seconds 60

The token is read from the app's user folder (api_token.txt) unless --token is given.
Standard library only, Python 3.9+. Exit code 0 on PASS, 1 on FAIL, 2 when the API cannot be used.
"""

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

HOST = "127.0.0.1"
TOKEN_HEADER = "X-FactorySim-Token"

# Never through a proxy: the API only answers on this machine.
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def default_token_file():
    appdata = os.environ.get("APPDATA", os.path.expanduser("~"))
    return os.path.join(appdata, "Godot", "app_userdata", "FactorySim", "api_token.txt")


class Api:
    def __init__(self, port, token):
        self.base = "http://%s:%d" % (HOST, port)
        self.token = token

    def call(self, method, path, body=None):
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header(TOKEN_HEADER, self.token)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        try:
            with OPENER.open(request, timeout=10) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            text = error.read().decode("utf-8", "replace")
            raise SystemExit("API %s %s -> %d %s" % (method, path, error.code, text)) from None

    @staticmethod
    def tag(name):
        return "/api/tags/" + urllib.parse.quote(name, safe="")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--api-port", type=int, default=5080, help="port of the test API (default 5080)")
    parser.add_argument("--token", help="token; default: read from the app's api_token.txt")
    parser.add_argument("--token-file", default=default_token_file(), help="where the app writes the token")
    parser.add_argument("--plc-port", type=int, default=5020, help="port of the Modbus server (default 5020)")
    parser.add_argument("--unit", type=int, default=255, help="Modbus unit id (default 255)")
    parser.add_argument("--scene", default="mvp_sorting", help="scene name (default mvp_sorting)")
    parser.add_argument("--seconds", type=float, default=60.0, help="how long to let it run (default 60)")
    args = parser.parse_args()

    token = args.token
    if token is None:
        try:
            with open(args.token_file, encoding="utf-8") as handle:
                token = handle.read().strip()
        except OSError as error:
            print("No token: %s. Is the test API on?" % error)
            return 2

    api = Api(args.api_port, token)
    try:
        state = api.call("GET", "/api/state")
    except urllib.error.URLError as error:
        print("Test API not reachable on %s:%d: %s" % (HOST, args.api_port, error.reason))
        return 2
    print("app: %s, scene %s" % (state["state"], state["scene"]))

    api.call("POST", "/api/scene/open", {"name": args.scene})
    print("opened %s" % args.scene)

    api.call("POST", "/api/plc/connect", {"port": args.plc_port, "unit": args.unit})
    deadline = time.monotonic() + 10.0
    while api.call("GET", "/api/plc")["status"] != "connected":
        if time.monotonic() > deadline:
            print("FAIL: no connection with the PLC on %s:%d" % (HOST, args.plc_port))
            return 1
        time.sleep(0.2)
    print("PLC connected on %s:%d" % (HOST, args.plc_port))

    api.call("POST", "/api/sim/run")
    # A short pulse on Start, like an operator pressing the button.
    api.call("PUT", Api.tag("Start button") + "/force", {"value": True})
    time.sleep(0.5)
    api.call("DELETE", Api.tag("Start button") + "/force")

    print("running for %.0f s ..." % args.seconds)
    time.sleep(args.seconds)
    counters = api.call("GET", "/api/counters")
    plc = api.call("GET", "/api/plc")

    api.call("POST", "/api/sim/pause")
    api.call("POST", "/api/plc/disconnect")

    removed = counters["removed"]
    print("spawned %d, removed %s, in scene %d, fallen %d, %.1f/min"
          % (counters["spawned"], json.dumps(removed), counters["inScene"], counters["fallen"], counters["perMinute"]))
    print("PLC: %d exchanges, %d errors" % (plc["exchanges"], plc["errors"]))
    passed = bool(removed) and all(count > 0 for count in removed.values()) and counters["fallen"] == 0
    print("RESULT: PASS" if passed else "RESULT: FAIL")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
