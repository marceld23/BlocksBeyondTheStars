# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Load a local WebGL build in a headless Chromium and keep what the console says (#2390).

The one real check of the browser build: which graphics API the engine started on (WebGPU or WebGL 2), whether
the page threw, and what the first frames look like. Serves the build folder itself (plain files, so build with
BBS_WEBGL_FAST_LOCAL=1 — no Brotli), opens it with WebGPU enabled, takes a screenshot every five seconds and
writes the console to <out>/console-<mode>.log.

    uv run --no-project --with playwright python -I scripts/webgl-browser-check.py client/Build/WebGL <out> [--seconds 90]
        [--mode webgpu|webgl2] [--channel chromium]

Playwright's default *headless shell* has no DXC libraries, so WebGPU device creation fails there and the page
takes the template's WebGL 2 fallback (`[BBS] graphics: WebGPU probe failed … falling back to WebGL 2`); the full
browser (`--channel chromium`, new-headless mode) runs the WebGPU path. Install once with
`uv run --no-project --with playwright python -m playwright install chromium`. `--mode webgl2` appends
`?bbsGpu=webgl2`, which forces the fallback on any browser.
"""

import argparse
import http.server
import os
import socketserver
import threading
import time

from playwright.sync_api import sync_playwright


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("build", help="the WebGL build folder (holds index.html)")
    ap.add_argument("out", help="where screenshots and console logs go")
    ap.add_argument(
        "--seconds",
        type=int,
        default=90,
        help="how long to watch after the page loaded",
    )
    ap.add_argument("--mode", choices=["webgpu", "webgl2"], default="webgpu")
    ap.add_argument(
        "--channel",
        default=None,
        help="'chromium' = the full browser (WebGPU works), default = the headless shell",
    )
    ap.add_argument("--port", type=int, default=8765)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    class Quiet(http.server.SimpleHTTPRequestHandler):
        def log_message(self, *args):
            pass

    def handler(*x, **k):
        return Quiet(*x, directory=a.build, **k)

    socketserver.TCPServer.allow_reuse_address = True
    httpd = socketserver.ThreadingTCPServer(("127.0.0.1", a.port), handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()

    lines: list[str] = []
    try:
        with sync_playwright() as pw:
            args = [
                "--ignore-gpu-blocklist",
                "--use-angle=d3d11",
                "--enable-gpu-rasterization",
                "--enable-unsafe-webgpu",
                "--enable-features=WebGPU",
                "--autoplay-policy=no-user-gesture-required",
            ]
            launch = {"headless": True, "args": args}
            if a.channel:
                launch["channel"] = a.channel
            browser = pw.chromium.launch(**launch)
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.on("console", lambda m: lines.append(f"[{m.type}] {m.text}"))
            page.on("pageerror", lambda e: lines.append(f"[pageerror] {e}"))
            url = f"http://127.0.0.1:{a.port}/index.html" + (
                "?bbsGpu=webgl2" if a.mode == "webgl2" else ""
            )
            page.goto(url, wait_until="load", timeout=120000)
            t0 = time.time()
            while time.time() - t0 < a.seconds:
                time.sleep(5)
                page.screenshot(
                    path=os.path.join(
                        a.out, f"{a.mode}-{int(time.time() - t0):03d}.png"
                    )
                )
            with open(
                os.path.join(a.out, f"console-{a.mode}.log"), "w", encoding="utf-8"
            ) as f:
                f.write("\n".join(lines))
            browser.close()
    finally:
        httpd.shutdown()

    hits = [
        l
        for l in lines
        if "[Graphics]" in l
        or "[BBS] graphics" in l
        or "pageerror" in l
        or "Exception" in l
    ]
    print(f"{a.mode}: {len(lines)} console lines")
    for h in hits[:40]:
        print("  " + h[:300])
    return (
        0
        if any("[Graphics]" in l for l in lines)
        and not any("pageerror" in l for l in lines)
        else 1
    )


if __name__ == "__main__":
    raise SystemExit(main())
