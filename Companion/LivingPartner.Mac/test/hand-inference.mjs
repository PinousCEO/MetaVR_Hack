// Node 22+ and Chrome/Chromium. Pass a folder with Google's MediaPipe test photographs,
// or --fetch-fixtures to download the four photographs for this test. Inference is offline.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { once } from "node:events";
import { access, mkdtemp, readFile, realpath, rm } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { basename, extname, join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const files = {
    fist: "43fa1cabf3f90d574accc9a56986e2ee48638ce59fc65af1846487f73bb2ef24",
    pointing_up: "ecf8ca2611d08fa25948a4fc10710af9120e88243a54da6356bacea17ff3e36e",
    right_hands: "4b5134daa4cb60465535239535f9f74c2842aba3aa5fd30bf04ef5678f93d87f",
    thumb_up: "5d673c081ab13b8a1812269ff57047066f9c33c07db5f4178089e8cb3fdc0291",
};
const fixturePath = process.argv[2];
if (!fixturePath) throw Error("Pass the local MediaPipe image folder or --fetch-fixtures.");
const images = new Map();
for (const [name, sha] of Object.entries(files)) {
    let bytes;
    if (fixturePath === "--fetch-fixtures") {
        const url = "https://storage.googleapis.com/mediapipe-assets/" + name + ".jpg";
        const response = await fetch(url);
        if (!response.ok) throw Error("Unable to download " + url);
        bytes = Buffer.from(await response.arrayBuffer());
    } else bytes = await readFile(join(fixturePath, name + ".jpg"));
    assert.equal(createHash("sha256").update(bytes).digest("hex"), sha, name + " photo checksum");
    images.set("/" + name + ".jpg", bytes);
}
const browsers = [
    process.env.CHROME_PATH,
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    join(process.env.ProgramFiles || ".", "Google/Chrome/Application/chrome.exe"),
    "/usr/bin/chromium",
    "/usr/bin/google-chrome",
];
let chromium;
for (const candidate of browsers.filter(Boolean))
    if (
        await access(candidate).then(
            () => true,
            () => false,
        )
    ) {
        chromium = candidate;
        break;
    }
if (!chromium) throw Error("Set CHROME_PATH to a local Chromium executable.");

// This function also runs in the test page.
async function inBrowser() {
    const { createPointingDetector } = await import("/table/pointing.mjs");
    const detector = await createPointingDetector();
    const payload = async (name, w, h) => {
        const blob = await (await fetch("/" + name + ".jpg")).blob();
        const reader = new FileReader();
        const jpeg = await new Promise((ok, no) => {
            reader.onload = () => ok(reader.result.split(",")[1]);
            reader.onerror = no;
            reader.readAsDataURL(blob);
        });
        return { jpeg, w, h, cx: w / 2, cy: h / 2 };
    };
    // The reference image shows a finger pointing up and slightly back toward the
    // camera. This calibrated, oblique plane intersects that known direction.
    const frame = {
        ...(await payload("pointing_up", 358, 376)),
        fx: 1200,
        fy: 1200,
        center: [0, -0.21, 0.81],
        right: [1, 0, 0],
        up: [0, 0.35, 0.937],
        widthM: 1,
        heightM: 0.5,
        own: [],
        round: 1,
        revision: 1,
        monitorId: "tv",
    };
    let seq = 0;
    const next = (overrides) => detector.detect({ ...frame, ...overrides, seq: ++seq });
    const noneTwice = async (overrides) =>
        (await next(overrides)) === null && (await next(overrides)) === null;
    const checks = {};
    checks.first = (await next({})) === null;
    const point = await next({});
    checks.point = !!point && Math.abs(point.u - 0.493) < 0.03 && Math.abs(point.v - 0.409) < 0.07;
    for (const [name, w, h] of [
        ["fist", 358, 354],
        ["thumb_up", 382, 406],
        ["right_hands", 720, 382],
    ])
        checks[name] = await noneTwice(await payload(name, w, h));
    // Independent wearer tracking samples at the wrist, palm center and tip.
    const anchor = (x, y) => [(x * 358 - 179) / 1200, (y * 376 - 188) / 1200, 1];
    const own = [[anchor(0.48, 0.746), anchor(0.437, 0.609), anchor(0.477, 0.198)]];
    checks.wearer = await noneTwice({ own });
    checks.corrupt = (await next({ jpeg: "invalid ***" })) === null;
    checks.dimensions = (await next({ h: 150 })) === null;
    detector.reset();
    checks.resume = !(await next({})) && !!(await next({}));
    detector.close();
    checks.closed = (await next({})) === null;
    return { checks, point };
}

const testDir = dirname(fileURLToPath(import.meta.url));
const table = resolve(testDir, "../wwwroot/table");
const extensions = {
    ".js": "text/javascript",
    ".mjs": "text/javascript",
    ".wasm": "application/wasm",
    ".task": "application/octet-stream",
};
const { promise: result, resolve: complete, reject: fail } = Promise.withResolvers();
const server = createServer(async (req, res) => {
    const url = new URL(req.url, "http://localhost").pathname;
    if (url === "/") {
        res.writeHead(200, {
            "Content-Type": "text/html",
            "Content-Security-Policy":
                "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'",
        });
        return res.end('<!doctype html><script type="module" src="/test.mjs"></script>');
    }
    if (url === "/test.mjs") {
        res.writeHead(200, { "Content-Type": "text/javascript" });
        return res.end(
            "const run=" +
                inBrowser.toString() +
                ";let outcome;try{outcome=await run()}catch(e){outcome={error:String(e)}}await fetch('/result',{method:'POST',body:JSON.stringify(outcome)});",
        );
    }
    if (url === "/result" && req.method === "POST") {
        let body = "";
        for await (const chunk of req) body += chunk;
        res.end();
        try {
            complete(JSON.parse(body));
        } catch (e) {
            fail(e);
        }
        return;
    }
    if (images.has(url)) {
        res.writeHead(200, { "Content-Type": "image/jpeg" });
        return res.end(images.get(url));
    }
    if (/^\/table\/(hand\/(wasm\/)?)?[a-zA-Z0-9._-]+$/.test(url)) {
        try {
            const bytes = await readFile(join(table, url.slice(7)));
            res.writeHead(200, { "Content-Type": extensions[extname(url)] });
            return res.end(bytes);
        } catch {}
    }
    res.writeHead(404);
    res.end();
});
await new Promise((ok) => server.listen(0, "127.0.0.1", ok));
const temporary = await realpath(tmpdir());
const profile = await mkdtemp(join(temporary, "bambi-hand-"));
const end = setTimeout(() => fail(Error("Timed out waiting for the local hand model.")), 90000);
const browser = spawn(
    chromium,
    [
        "--headless=new",
        "--disable-background-networking",
        "--no-first-run",
        "--use-gl=angle",
        "--use-angle=swiftshader",
        "--enable-unsafe-swiftshader",
        "--user-data-dir=" + profile,
        "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1",
        "http://127.0.0.1:" + server.address().port,
    ],
    { stdio: "ignore", windowsHide: true },
);
browser.on("error", fail);
const exited = once(browser, "exit");
try {
    const observed = await result;
    assert.ok(!observed.error, observed.error);
    for (const [name, passed] of Object.entries(observed.checks)) assert.equal(passed, true, name);
    console.log(
        Object.keys(observed.checks).length + " offline hand-inference checks passed",
        observed.point,
    );
} finally {
    clearTimeout(end);
    if (browser.exitCode === null) browser.kill();
    await exited;
    server.closeAllConnections();
    server.close();
    if (dirname(profile) === temporary && basename(profile).startsWith("bambi-hand-"))
        await rm(profile, { recursive: true, force: true, maxRetries: 4, retryDelay: 150 });
}
