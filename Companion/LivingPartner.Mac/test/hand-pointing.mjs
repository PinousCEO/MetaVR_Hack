import assert from "node:assert/strict";
import test from "node:test";
import { guestAim, stableAim } from "../wwwroot/table/hand-geometry.mjs";

const add = (a, b) => a.map((value, i) => value + b[i]);
const sub = (a, b) => a.map((value, i) => value - b[i]);
const mul = (a, s) => a.map((value) => value * s);
const unit = (a) => mul(a, 1 / Math.hypot(...a));
const camera = {
    w: 960,
    h: 720,
    fx: 740,
    fy: 710,
    cx: 466,
    cy: 358,
    center: [0, 0, 1.9],
    right: [1, 0, 0],
    up: [0, -1, 0],
    widthM: 1.12,
    heightM: 0.65,
    own: [],
    round: 1,
    revision: 2,
    seq: 1,
    monitorId: "tv",
};
const direction = unit([0.19, -0.09, 0.98]);
const translate = [-0.06, 0.04, 1.2];
const pointObject = ([x, y, z]) => ({ x, y, z });
function handPoints(ray = direction) {
    const p = [
        [0, 0.05, 0],
        [-0.025, 0.032, -0.006],
        [-0.043, 0.012, 0.008],
        [-0.044, 0, 0.02],
        [-0.032, 0.008, 0.022],
    ];
    for (const [finger, x] of [-0.028, -0.004, 0.017, 0.037].entries()) {
        const mcp = [x, -0.032 + finger * 0.003, 0];
        p.push(mcp);
        for (const segment of finger === 0
            ? [0.045, 0.026, 0.021].map((n) => mul(ray, n))
            : [
                  [0, -0.025, 0.02],
                  [0, 0.017, 0.025],
                  [0.003, 0.02, -0.007],
              ])
            p.push(add(p.at(-1), segment));
    }
    return p;
}
function observed(p = handPoints(), translation = translate, cam = camera) {
    return {
        worldLandmarks: [p.map(pointObject)],
        landmarks: [
            p.map((relative) => {
                const [x, y, z] = add(relative, translation);
                return {
                    x: (cam.cx + (cam.fx * x) / z) / cam.w,
                    y: (cam.cy + (cam.fy * y) / z) / cam.h,
                    z: 0,
                };
            }),
        ],
    };
}
function checkPoint(actual, u, v) {
    assert.ok(actual, "expected a TV intersection");
    assert.ok(Math.abs(actual.u - u) < 1e-8, `expected u=${u}, received ${actual.u}`);
    assert.ok(Math.abs(actual.v - v) < 1e-8, `expected v=${v}, received ${actual.v}`);
}
function stacked(a, b) {
    return {
        landmarks: [...a.landmarks, ...b.landmarks],
        worldLandmarks: [...a.worldLandmarks, ...b.worldLandmarks],
    };
}
const vectorRotation = ([x, y, z]) => [0.96 * x + 0.28 * z, y, -0.28 * x + 0.96 * z];
const p = handPoints();
const tip = add(p[8], translate);
const hit = add(tip, mul(direction, (camera.center[2] - tip[2]) / direction[2]));
const expectedU = 0.5 + hit[0] / camera.widthM,
    expectedV = 0.5 + hit[1] / camera.heightM;

test("distal index ray reaches known point on a TV with unequal focal lengths and off-center principal point", () => {
    checkPoint(guestAim(camera, observed()), expectedU, expectedV);
});
test("physical aim stays on the same cell after wearer turns their head or changes capture resolution", () => {
    const rotated = {
        ...camera,
        center: vectorRotation(camera.center),
        up: vectorRotation(camera.up),
        right: vectorRotation(camera.right),
    };
    checkPoint(
        guestAim(rotated, observed(p.map(vectorRotation), vectorRotation(translate), rotated)),
        expectedU,
        expectedV,
    );
    const smaller = { ...camera, w: 480, h: 360, cx: 233, cy: 179, fx: 370, fy: 355 };
    checkPoint(guestAim(smaller, observed(p, translate, smaller)), expectedU, expectedV);
});
test("contact within 4.5 cm of glass uses the observed fingertip pixel", () => {
    const close = { ...camera, center: [0, 0, tip[2] + 0.012] };
    const contact = mul(tip, close.center[2] / tip[2]);
    checkPoint(
        guestAim(close, observed()),
        0.5 + contact[0] / close.widthM,
        0.5 + contact[1] / close.heightM,
    );
});
test("a finger pointing away, nearly parallel to glass, or outside the TV yields no move", () => {
    for (const d of [
        [0, 0, -1],
        [1, 0, 0.05],
        [0.9, 0, 0.3],
    ])
        assert.equal(guestAim(camera, observed(handPoints(unit(d)))), null);
    assert.equal(guestAim({ ...camera, center: [0, 0, 1.0] }, observed()), null);
});
test("an open palm and a closed index finger cannot play", () => {
    const open = handPoints(),
        bent = handPoints();
    for (const k of [9, 13, 17])
        for (let i = 1; i <= 3; i++) open[k + i] = add(open[k], mul(direction, 0.024 * i));
    bent[7] = add(bent[6], [0, 0.023, -0.01]);
    bent[8] = add(bent[7], [0, 0.018, -0.007]);
    assert.equal(guestAim(camera, observed(open)), null);
    assert.equal(guestAim(camera, observed(bent)), null);
});
test("a second pointing guest cancels the guess even when that finger points off the TV", () => {
    const second = observed(handPoints(unit([0.92, 0, 0.2])), [0.3, -0.03, 1.13]);
    assert.equal(guestAim(camera, stacked(observed(), second)), null);
});
test("matched wearer wrist, palm, and tip are suppressed and another guest remains playable", () => {
    const ownTranslation = [0.04, -0.01, 0.36],
        absolute = p.map((q) => add(q, ownTranslation));
    const group = [absolute[0], mul(add(absolute[0], absolute[9]), 0.5), absolute[8]];
    const tracked = { ...camera, own: [group] };
    const wearer = observed(p, ownTranslation);
    assert.equal(guestAim(tracked, wearer), null);
    checkPoint(guestAim(tracked, stacked(wearer, observed())), expectedU, expectedV);
    assert.equal(guestAim(camera, wearer), null);
});
test("incomplete calibration, bad axes, minuscule or inconsistent landmarks are rejected", () => {
    for (const overrides of [
        { own: null },
        { right: [0, -1, 0] },
        { up: [0, 0, 0] },
        { widthM: 0 },
        { fx: 0 },
    ])
        assert.equal(guestAim({ ...camera, ...overrides }, observed()), null);
    const malformed = observed();
    malformed.landmarks[0][1].x += 0.5;
    assert.equal(guestAim(camera, malformed), null);
    const missing = observed();
    missing.landmarks[0][7] = null;
    assert.equal(guestAim(camera, missing), null);
    const clipped = { ...camera, cx: -100 };
    assert.equal(guestAim(clipped, observed(p, translate, clipped)), null);
    const tiny = { ...camera, fx: 20, fy: 20 };
    assert.equal(guestAim(tiny, observed(p, translate, tiny)), null);
});
test("stable aim filters jumps, dropped hands, duplicates and changes of turn or TV", () => {
    const filter = stableAim(),
        q = { u: 0.4, v: 0.65 },
        r = { u: 0.7, v: 0.5 };
    let seq = 0;
    const next = (aim, t, overrides) => filter.update({ ...camera, seq: ++seq, ...overrides }, aim, t);
    assert.equal(next(q, 0), null);
    checkPoint(next(q, 130), q.u, q.v);
    assert.equal(next(q, 200, { seq: 2 }), null);
    assert.equal(next(r, 260), null);
    checkPoint(next(r, 390), r.u, r.v);
    assert.equal(next(null, 500), null);
    assert.equal(next(r, 550), null);
    assert.equal(next(r, 1800), null);
    checkPoint(next(r, 1950), r.u, r.v);
    assert.equal(next(r, 2100, { revision: 3 }), null);
    assert.equal(next(r, 2200, { round: 2 }), null);
    assert.equal(next(r, 2300, { monitorId: "moved TV" }), null);
    filter.reset();
    assert.equal(next(r, 2400), null);
});
