const plus = (a, b) => a.map((x, i) => x + b[i]);
const minus = (a, b) => a.map((x, i) => x - b[i]);
const scale = (a, s) => a.map((x) => x * s);
const dot = (a, b) => a.reduce((s, x, i) => s + x * b[i], 0);
const length = (a) => Math.hypot(...a);
const unit = (a) => scale(a, 1 / length(a));
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
function xyz(p) {
    const v = Array.isArray(p) ? p : [p?.x, p?.y, p?.z];
    return v.length === 3 && v.every(Number.isFinite) ? v : null;
}

function validCalibration(f) {
    return (
        f &&
        Number.isFinite(f.w) &&
        f.w > 0 &&
        Number.isFinite(f.h) &&
        f.h > 0 &&
        Number.isFinite(f.fx) &&
        f.fx > 0 &&
        Number.isFinite(f.fy) &&
        f.fy > 0 &&
        Number.isFinite(f.cx) &&
        Number.isFinite(f.cy) &&
        Number.isFinite(f.widthM) &&
        f.widthM > 0 &&
        Number.isFinite(f.heightM) &&
        f.heightM > 0 &&
        xyz(f.center) &&
        xyz(f.right) &&
        xyz(f.up) &&
        Array.isArray(f.own)
    );
}

// MediaPipe's metric points are centered on the hand. Solve the perspective equations
// tx = nx_i * tz - (x_i - nx_i * z_i) and ty analogously for the whole hand.
// The true keypoints in image space then anchor the estimated fingertip to its camera ray.
function cameraHand(f, img, world) {
    if (
        img.length !== 21 ||
        world.length !== 21 ||
        !world.every(xyz) ||
        !img.every((q) => q && Number.isFinite(q.x) && Number.isFinite(q.y))
    )
        return null;
    // A partly cropped hand can have extrapolated joints outside the photograph.
    // Require the actual pointing finger to be in the captured field of view.
    if ([6, 7, 8].some((i) => img[i].x < 0 || img[i].x > 1 || img[i].y < 0 || img[i].y > 1)) return null;
    const p = world.map(xyz);
    const norm = img.map((q) => [(q.x * f.w - f.cx) / f.fx, (q.y * f.h - f.cy) / f.fy]);
    let nx = 0,
        ny = 0,
        ax = 0,
        ay = 0;
    const base = norm.map(([x, y], i) => {
        const b = [p[i][0] - x * p[i][2], p[i][1] - y * p[i][2]];
        nx += x / 21;
        ny += y / 21;
        ax += b[0] / 21;
        ay += b[1] / 21;
        return b;
    });
    let covariance = 0,
        variance = 0;
    for (let i = 0; i < 21; i++) {
        const dx = norm[i][0] - nx,
            dy = norm[i][1] - ny;
        covariance += dx * (base[i][0] - ax) + dy * (base[i][1] - ay);
        variance += dx * dx + dy * dy;
    }
    const tz = covariance / variance;
    if (!Number.isFinite(tz) || tz < 0.12 || tz > 3.5) return null;
    const t = [nx * tz - ax, ny * tz - ay, tz];
    const px = img.map((q) => [q.x * f.w, q.y * f.h]);
    const palmPx = Math.max(length(minus(px[5], px[17])), length(minus(px[0], px[9])));
    if (palmPx < 9) return null;
    let squaredError = 0;
    for (let i = 0; i < 21; i++) {
        const q = plus(p[i], t);
        if (q[2] < 0.05) return null;
        squaredError += (f.fx * (q[0] / q[2] - norm[i][0])) ** 2 + (f.fy * (q[1] / q[2] - norm[i][1])) ** 2;
    }
    if (Math.sqrt(squaredError / 21) > Math.max(3, 0.24 * palmPx)) return null;
    const camera = norm.map(([x, y], i) => scale([x, y, 1], tz + p[i][2]));
    return { p, camera, px, palmPx };
}

function finger(p, i) {
    const a = minus(p[i + 1], p[i]),
        b = minus(p[i + 2], p[i + 1]),
        c = minus(p[i + 3], p[i + 2]);
    const total = length(a) + length(b) + length(c);
    return {
        reach: length(minus(p[i + 3], p[i])) / total,
        lower: dot(unit(a), unit(b)),
        upper: dot(unit(b), unit(c)),
    };
}
function pointing(p) {
    const index = finger(p, 5),
        middle = finger(p, 9);
    const folded = (k) => k.reach < 0.82 || k.lower < 0.4;
    return (
        index.reach > 0.88 &&
        index.lower > 0.66 &&
        index.upper > 0.52 &&
        folded(middle) &&
        (folded(finger(p, 13)) || folded(finger(p, 17)))
    );
}

function couldBeWearer(f, hand) {
    const anchors = [hand.camera[0], scale(plus(hand.camera[0], hand.camera[9]), 0.5), hand.camera[8]];
    let tracked = 0;
    for (const own of f.own) {
        const group = Array.isArray(own) ? own : [own?.wrist, own?.palm, own?.tip];
        let valid = 0,
            near = 0;
        for (let i = 0; i < 3; i++) {
            const pos = xyz(group[i]);
            if (!pos || pos[2] <= 0.04) continue;
            valid++;
            const a = anchors[i];
            const dx = f.fx * (pos[0] / pos[2] - a[0] / a[2]),
                dy = f.fy * (pos[1] / pos[2] - a[1] / a[2]);
            // Also reject a projected overlap: monocular hand-size estimates and XR joint locations differ.
            if (length(minus(a, pos)) < 0.115 || Math.hypot(dx, dy) < Math.max(8, hand.palmPx * 0.68)) near++;
        }
        if (valid && near >= Math.min(2, valid)) return true;
        if (valid >= 2) tracked++;
    }
    // A nearby untracked hand could belong to the headset wearer. The guest stands at the TV.
    return tracked < 2 && length(anchors[0]) < 0.83;
}

function hitScreen(f, hand) {
    const right = unit(xyz(f.right)),
        up = unit(xyz(f.up)),
        center = xyz(f.center);
    if (!Number.isFinite(length(right) + length(up)) || Math.abs(dot(right, up)) > 0.06) return null;
    let normal = unit(cross(right, up));
    if (dot(normal, center) < 0) normal = scale(normal, -1);
    const origin = hand.camera[8],
        p = hand.p;
    // The first knuckle is deliberately excluded. Use the distal two index bones in metric camera axes.
    const ray = unit(plus(scale(unit(minus(p[7], p[6])), 0.56), scale(unit(minus(p[8], p[7])), 0.44)));
    const facing = dot(ray, normal);
    const remaining = dot(minus(center, origin), normal);
    if (remaining < -0.045) return null;
    let hit;
    if (Math.abs(remaining) < 0.045 && facing > -0.15) {
        // At the glass itself, the observed fingertip pixel supplies the contact point.
        const toTip = unit(origin),
            slope = dot(normal, toTip);
        if (slope < 0.12) return null;
        hit = scale(toTip, dot(center, normal) / slope);
    } else {
        if (facing < 0.18) return null;
        const travel = remaining / facing;
        if (travel < 0 || travel > 2.0) return null;
        hit = plus(origin, scale(ray, travel));
    }
    if (!hit.every(Number.isFinite) || hit[2] <= 0.03) return null;
    const local = minus(hit, center),
        u = 0.5 + dot(local, right) / f.widthM,
        v = 0.5 - dot(local, up) / f.heightM;
    return u >= 0 && u <= 1 && v >= 0 && v <= 1 ? { u, v } : null;
}

export function guestAim(frame, result) {
    if (
        !validCalibration(frame) ||
        !Array.isArray(result?.landmarks) ||
        !Array.isArray(result?.worldLandmarks)
    )
        return null;
    const candidates = [];
    for (let i = 0; i < result.landmarks.length; i++) {
        const image = result.landmarks[i],
            world = result.worldLandmarks[i];
        if (!Array.isArray(image) || !Array.isArray(world)) continue;
        const hand = cameraHand(frame, image, world);
        if (!hand || !pointing(hand.p) || couldBeWearer(frame, hand)) continue;
        candidates.push(hand);
    }
    return candidates.length === 1 ? hitScreen(frame, candidates[0]) : null;
}

export function stableAim() {
    let recent = [],
        stamp = -Infinity,
        seq = -1,
        session = "";
    function reset() {
        recent = [];
        stamp = -Infinity;
        seq = -1;
        session = "";
    }
    return {
        reset,
        update(frame, aim, now) {
            const key = JSON.stringify([frame.monitorId, frame.round, frame.revision]);
            if (key !== session) {
                reset();
                session = key;
            }
            if (!Number.isSafeInteger(frame.seq) || frame.seq <= seq) return null;
            seq = frame.seq;
            if (!aim || now - stamp > 1100) recent = [];
            stamp = now;
            if (!aim) return null;
            const radius = Math.min(0.038, frame.heightM * 0.035);
            if (
                recent.some(
                    (q) => Math.hypot((q.u - aim.u) * frame.widthM, (q.v - aim.v) * frame.heightM) > radius,
                )
            )
                recent = [];
            recent.push(aim);
            if (recent.length > 3) recent.shift();
            return recent.length < 2
                ? null
                : {
                      u: recent.reduce((n, q) => n + q.u, 0) / recent.length,
                      v: recent.reduce((n, q) => n + q.v, 0) / recent.length,
                  };
        },
    };
}
