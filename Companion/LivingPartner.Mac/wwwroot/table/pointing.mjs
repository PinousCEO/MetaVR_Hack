import { FilesetResolver, HandLandmarker } from "./hand/vision_bundle.mjs";
import { guestAim, stableAim } from "./hand-geometry.mjs";

export async function createPointingDetector() {
    const asset = (name) => new URL("./hand/" + name, import.meta.url).href;
    const hand = await HandLandmarker.createFromOptions(await FilesetResolver.forVisionTasks(asset("wasm")), {
        baseOptions: { modelAssetPath: asset("hand_landmarker.task"), delegate: "CPU" },
        runningMode: "IMAGE",
        numHands: 4,
        minHandDetectionConfidence: 0.55,
        minHandPresenceConfidence: 0.55,
    });
    const stable = stableAim();
    let closed = false,
        epoch = 0;
    const reset = () => {
        epoch++;
        stable.reset();
    };
    return {
        reset,
        close() {
            if (!closed) {
                closed = true;
                reset();
                hand.close();
            }
        },
        async detect(frame) {
            if (closed || !frame || !Number.isSafeInteger(frame.seq)) return null;
            const started = epoch;
            let image;
            try {
                if (typeof frame.jpeg !== "string" || frame.jpeg.length < 8 || frame.jpeg.length > 2000000)
                    return stable.update(frame, null, performance.now());
                const raw = atob(frame.jpeg);
                const bytes = Uint8Array.from(raw, (c) => c.charCodeAt(0));
                image = await createImageBitmap(new Blob([bytes], { type: "image/jpeg" }));
                if (closed || started !== epoch) return null;
                const aim =
                    image.width === frame.w && image.height === frame.h
                        ? guestAim(frame, hand.detect(image))
                        : null;
                return stable.update(frame, aim, performance.now());
            } catch {
                return started === epoch && !closed ? stable.update(frame, null, performance.now()) : null;
            } finally {
                image?.close();
            }
        },
    };
}
