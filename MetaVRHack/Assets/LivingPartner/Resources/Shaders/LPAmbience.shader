// The surroundings. A large sphere round the user's head: everything outside the WORK AREA (the screens and the desk in
// front of them - a window given by a direction and its width and height in degrees) is dimmed towards a background
// colour and drawn over with a moving pattern; inside the window nothing is drawn at all, so the real desk and the
// real screens stay exactly as they are.
// _Pattern: 0 none | 1 spots (club lights) | 2 stars | 3 rain | 4 aurora | 5 snow | 6 petals | 7 bubbles
//           8 peak (a mountain top above a sea of clouds) | 9 coast (the seaside; at sunset when its colours are warm):
//             whole PLACES that replace the room, painted afresh for every picture - the sea runs, the clouds drift.
// The places are made of ONE small picture of noise (_Noise: Art/Textures/LPNoise2.asset, see AmbienceNoise in the
// Editor) that is looked up a few times per pixel instead of working noise out octave by octave:
//   the mountain top  at most 10 (five skylines for six ranges, the face of the range in front and its grain, three for the
//                     sea of clouds - four near by, where fewer ranges stand - or two for the high cloud); 2 on the summit underfoot
//   the seaside       5 above the horizon (three for the heaped clouds, one for the streaks, one round the horizon;
//                     6 on the dunes), 4 below it (the waterline, two sheets of small waves or of sand, the lace of foam)
// The places before worked noise out 22 times (mountain) and 10 times (seaside) per pixel, four hashes each, in loops.
// Every look-up names the size of copy it wants (the look-ups are of flat sheets and of circles round the user,
// whose sizes on the screen are known), so nothing is worked out for a pixel of the work area - the function returns
// before the first look-up there - and there is no seam where coordinates jump. Nothing moves the horizon: the sea,
// the foam, the clouds and the glitter move; the skylines, the shore and the sun stand still.
// A place is ANCHORED IN THE WORLD (_PlaceDir, _PlaceAt: fixed by the app when the place comes on): its sun, sea and peaks
// keep their bearing and its ground lies still in the room, whatever the head does; only the work area's window follows
// the screens. Everything is a matter of the direction each eye looks in (and, for the ground, of where that eye is).
Shader "LivingPartner/Ambience"
{
    Properties
    {
        _C0 ("Background", Color) = (0.08, 0.04, 0.18, 1)
        _C1 ("Colour 1", Color) = (1, 0.3, 0.64, 1)
        _C2 ("Colour 2", Color) = (0.3, 0.84, 1, 1)
        _Pattern ("Pattern", Float) = 0
        _Dim ("Dim", Range(0, 1)) = 0.6
        _Speed ("Speed", Float) = 1
        _Density ("Density", Float) = 1
        _Beat ("Beat", Range(0, 1)) = 0
        _Amount ("Amount", Range(0, 1)) = 0
        _WorkDir ("Work direction", Vector) = (0, 0, 1, 0)
        _WorkSize ("Work half width, up, down (degrees), edge", Vector) = (55, 30, 70, 9)
        _PlaceDir ("Where a place is anchored: direction (world), w = half width of the work area then (0 = follow the work area)", Vector) = (0, 0, 1, 0)
        _PlaceAt ("Where a place's ground lies: the point of it under the user when the place came on (world), w = how far below the eyes then (0 = it follows the eyes)", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _Noise ("Noise the places are made of", 2D) = "gray" {}
        _TimeShift ("Seconds added to the time (the Editor's preview)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-200" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Ambience"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            // LEqual, not Always: the dome is drawn after the opaque things (the partner, a ball - Geometry, they write
            // depth) and with Always it was painted OVER the partner wherever it stood outside the work area. Everything
            // else of the app (things of the desk: Transparent+10, boards and effects: Transparent) is drawn after the
            // dome (Transparent-200) and so over it, as before.
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            CBUFFER_START(UnityPerMaterial)
            float4 _C0; float4 _C1; float4 _C2;
            float _Pattern; float _Dim; float _Speed; float _Density; float _Beat; float _Amount;
            float4 _WorkDir; float4 _WorkSize; float4 _PlaceDir; float4 _PlaceAt;
            float _TimeShift;
            CBUFFER_END
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                // the direction this eye looks in, in the WORLD: everything drawn is a matter of that direction alone, so the
                // surroundings lie infinitely far away, the same for both eyes, and do not depend on where exactly the
                // sphere is or how it is turned (it is only something to draw on)
                o.dir = TransformObjectToWorld(v.positionOS.xyz) - GetCameraPositionWS();
                return o;
            }

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 hash2(float2 p) { return float2(hash(p), hash(p + 17.13)); }

            // little things scattered on a grid of the sky (turn, height): each cell may hold one, falling or still
            float drops(float2 uv, float2 cells, float fall, float size, float stretch, float t)
            {
                uv.y += t * fall;
                float2 g = uv * cells; float2 id = floor(g); float2 f = frac(g);
                float2 r = hash2(id);
                float2 c = float2(0.2 + 0.6 * r.x, 0.2 + 0.6 * r.y);
                float2 d = (f - c) * float2(1.0, 1.0 / stretch);
                return smoothstep(size, size * 0.3, length(d)) * step(0.45, r.x + r.y * 0.5);
            }

            // rain: thin streaks falling in columns. Every column has its own pace and its own start, so that no two
            // neighbours fall in step (a plain grid of drops read as rows of blocks); a streak is bright at its lower
            // end and fades upwards; only some places in a column hold one.
            float streaks(float2 uv, float cols, float rows, float fall, float width, float len, float keep, float t)
            {
                float gx = uv.x * cols; float col = floor(gx); float fx = frac(gx);
                float h1 = hash(float2(col, 3.7)), h2 = hash(float2(col, 9.1));
                float gy = (uv.y + t * fall * (0.75 + 0.5 * h1)) * rows + h2 * 7.0;
                float row = floor(gy); float fy = frac(gy);
                float cx = 0.25 + 0.5 * hash(float2(row, col + 5.3));
                float ax = smoothstep(width, width * 0.25, abs(fx - cx));
                float ay = smoothstep(0.0, 0.07, fy) * (1.0 - smoothstep(0.10, len, fy));
                return ax * ay * step(1.0 - keep, hash(float2(col + 1.3, row + 2.9)));
            }

            // ============================================================ the PLACES
            // The noise: r = soft hills (0..1), g, b = their slope along u and v (0.5 = level), a = finer billows (0..1).
            // lod: which copy (0 = the full picture; each step half the size) - always given, never left to the hardware,
            // so a look-up may stand behind an `if` and has no seam where its coordinates jump.
            float4 nz(float2 uv, float lod) { return SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv, max(lod, 0.0)); }

            // a direction: so many degrees to the right of the work area, so many degrees up
            float3 aim(float3 f, float3 r, float yawDeg, float upDeg)
            {
                float a = radians(yawDeg), e = radians(upDeg);
                return (f * cos(a) + r * sin(a)) * cos(e) + float3(0.0, sin(e), 0.0);
            }

            // The sky without its clouds - also what water and wet sand mirror. From _C0 overhead (deeper still straight
            // up) down to a band of light at the horizon (_C1; at dusk that band burns near the sun and is dull mauve
            // away from it), and the glow round the sun. S: where the sun is. dusk: 0 by day, 1 at sunset.
            float3 skyOf(float3 v, float3 S, float dusk)
            {
                float h = saturate(v.y);
                float sd = saturate(dot(v, S));
                float s2 = sd * sd, s4 = s2 * s2, s8 = s4 * s4, s32 = s8 * s8 * s8 * s8;
                float3 lowDay = lerp(_C0.rgb, lerp(_C1.rgb, float3(1.0, 1.0, 1.0), 0.45), 0.80);
                float3 lowDusk = lerp(lerp(_C0.rgb, _C1.rgb, 0.40), _C1.rgb * float3(1.15, 1.0, 0.85), saturate(s2 * 1.3));
                float3 low = lerp(lowDay, lowDusk, dusk);
                float3 high = _C0.rgb * lerp(float3(0.62, 0.76, 0.98), float3(0.80, 0.78, 1.0), dusk);
                float3 col = lerp(low, lerp(_C0.rgb, high, h), pow(h, lerp(0.42, 0.36, dusk)));
                col += _C1.rgb * (s8 * lerp(0.10, 0.34, dusk) + s32 * s32 * 0.45);
                return col;
            }

            float3 sunDisc(float3 v, float3 S, float3 light)
            {
                float sd = dot(v, S);
                return light * (smoothstep(0.99935, 0.99965, sd) * 1.6 + pow(saturate(sd), 900.0) * 0.6);
            }

            // Clouds on two sheets overhead (the nearer one heaped, the higher one streaked), drifting at different paces
            // and lit from the side the sun is on. n1 = the heaps here, n1s = the heaps a step towards the sun, n2 = what
            // frays them, n3 = the streaks. Returns what is seen of the sky through and round them.
            float3 clouds(float3 sky, float up, float4 n1, float4 n1s, float4 n2, float4 n3, float3 lit, float3 shade, float amount)
            {
                float wisp = smoothstep(0.56, 0.86, n3.a * 0.70 + n3.r * 0.40) * smoothstep(0.03, 0.22, up) * 0.42 * amount;
                sky = lerp(sky, lerp(sky, lit, 0.7), wisp);
                float dens = n1.r + (n2.a - 0.5) * 0.22;
                float edge = lerp(0.64, 0.48, saturate(amount * 0.5));
                float m = smoothstep(edge, edge + 0.13, dens) * smoothstep(0.015, 0.10, up);
                float sunSide = saturate(0.5 + (n1.r - n1s.r) * 6.0);                    // thinner towards the sun: that side is lit
                float thick = smoothstep(edge + 0.05, edge + 0.30, dens);
                float3 c = lerp(shade, lit, saturate(sunSide * (1.0 - 0.50 * thick) + 0.10 + 0.25 * (n2.a - 0.5)));
                return lerp(sky, c, m * 0.95);
            }

            // ------------------------------------------------------------ the seaside (and the sunset beach)
            float3 placeCoast(float3 d, float3 f, float3 r, float t, float wide, float lod0, float2 ring, float lodRing, float yawAll, float3 eye)
            {
                float dusk = smoothstep(0.65, 0.40, _C1.g / max(_C1.r, 0.01));                  // warm colours: the sun is going down
                float sunYaw = -(wide + 20.0);                                                  // just outside the work area, on the left
                float3 S = aim(f, r, sunYaw, lerp(17.0, 3.6, dusk));
                float2 sea = normalize(aim(f, r, sunYaw * 0.72, 0.0).xz), along = float2(-sea.y, sea.x);   // out to sea; along the shore
                float seaward = dot(ring, sea);                                                 // 1 looking out to sea, -1 looking inland
                float3 light = lerp(float3(1.0, 0.95, 0.86), _C1.rgb * float3(1.25, 1.0, 0.8), lerp(0.25, 0.92, dusk));
                float3 foamCol = lerp(float3(1.0, 1.0, 1.0), light, 0.20 + 0.5 * dusk) * lerp(1.0, 0.84, dusk);
                float3 sandTint = lerp(float3(1.0, 1.0, 1.0), float3(0.80, 0.50, 0.46), dusk);
                float3 level = normalize(float3(d.x, 0.0, d.z) + 1e-5);
                float3 haze = skyOf(level, S, dusk);                                            // what the far distance fades to
                float3 col;
                if (d.y > 0.0)
                {
                    // ---- the sky
                    float3 sky = skyOf(d, S, dusk) + sunDisc(d, S, light);
                    float cy = d.y + 0.11;
                    float2 cp = d.xz / cy;
                    float lodC = lod0 - 2.0 * log2(cy) - 0.5;
                    float2 u1 = cp * 0.062 + along * (t * 0.0045) + float2(0.13, 0.71);
                    float4 n1 = nz(u1, lodC - 4.0);
                    float4 n1s = nz(u1 + normalize(S.xz) * 0.022, lodC - 4.0);
                    float4 n2 = nz(cp * 0.24 + along * (t * 0.0105) + float2(0.4, 0.2), lodC - 2.05);
                    float2 hp = d.xz / (d.y + 0.30);
                    float4 n3 = nz(float2(dot(hp, sea) * 0.050, dot(hp, along) * 0.028) + along * (t * 0.0016) + float2(0.7, 0.3), lod0 - 2.0 * log2(d.y + 0.30) - 4.4);
                    float3 lit = lerp(float3(1.0, 0.99, 0.97), light * 1.12 + float3(0.10, 0.03, 0.05), dusk);
                    float3 shade = lerp(lerp(sky, float3(0.56, 0.64, 0.80), 0.70), lerp(_C0.rgb * 0.72, _C1.rgb * 0.55, 0.22), dusk);
                    col = clouds(sky, d.y, n1, n1s, n2, n3, lit, shade, _Density);
                    col = lerp(col, haze, exp(-d.y * 24.0) * 0.50);
                    // far land out at sea, and the dunes behind: one look-up round the horizon
                    float4 nr = nz(ring * 0.80 + float2(3.1, 1.7), lodRing - 0.32);
                    float isle = smoothstep(0.30, 0.85, seaward) * max(0.0, nr.a - 0.52) * 0.11;
                    col = lerp(col, lerp(haze, _C0.rgb * float3(0.30, 0.38, 0.52), 0.42 - 0.2 * dusk), smoothstep(isle + 0.0015, isle - 0.0015, d.y) * step(0.0008, isle));
                    float dune = smoothstep(0.10, -0.60, seaward) * (0.014 + 0.085 * nr.r);
                    if (d.y < dune + 0.004)
                    {
                        float4 gr = nz(float2(yawAll * 2.228, d.y * 9.0), lodRing + 1.2);         // (14 times round: marram grass on the crest)
                        float up01 = saturate(d.y / max(dune, 0.004));
                        float3 sand = lerp(float3(0.80, 0.64, 0.40), float3(0.94, 0.80, 0.55), up01) * sandTint;
                        float3 duneCol = sand * (0.90 + (0.5 * (nr.g - 0.5) + 1.2 * (gr.g - 0.5)) * dot(float2(ring.y, -ring.x), normalize(S.xz)));
                        float tuft = smoothstep(0.50, 0.66, gr.a + 0.35 * up01 - 0.22);
                        duneCol = lerp(duneCol, lerp(float3(0.34, 0.42, 0.20), float3(0.52, 0.56, 0.28), gr.r) * lerp(1.0, 0.55, dusk), tuft * 0.85);
                        col = lerp(col, lerp(duneCol, haze, 0.15), smoothstep(dune + 0.0015, dune - 0.0015, d.y) * step(0.0008, dune));
                    }
                }
                else
                {
                    // ---- the ground: a sheet one unit below the eyes - below where the eyes were when the place came on:
                    // it lies still in the room when the head moves (eye: where this eye is over it, in units).
                    // s = how far out towards the sea, l = along the shore
                    float inv = eye.y / max(-d.y, 0.004);
                    float2 P = eye.xz + d.xz * inv;
                    float s = dot(P, sea), l = dot(P, along), dist = length(P);
                    float lodG = lod0 + 2.0 * log2(inv) - 1.2;
                    // where the waterline lies and how the crests bend: a look-up along the shore only
                    float4 g = nz(float2(l * 0.020 + 0.21, 0.63 + t * 0.0010), max(lod0 + log2(inv) - 5.6, 2.5));
                    float shore = 2.4 + (g.r - 0.5) * 2.0;
                    float wash = 0.5 + 0.5 * sin(t * 0.70 + g.a * 7.0 + l * 0.11);              // the water runs up the sand and slides back
                    float reach = shore - 1.00;                                                 // as far up as it ever comes
                    float edge = shore - 1.00 * wash * wash;
                    float dp = s - edge;                                                        // out from the waterline (negative: on the sand)
                    bool wet = dp > 0.0;
                    float2 ua = wet ? P * 0.19 + sea * (t * 0.016) + along * (t * 0.011) : P * 1.7;
                    float2 ub = wet ? P * 0.47 - sea * (t * 0.021) + along * (t * 0.017) + float2(0.37, 0.11) : P * 0.055 + float2(0.5, 0.2);
                    float4 na = nz(ua, lodG + (wet ? -2.4 : 0.77));
                    float4 nb = nz(ub, lodG + (wet ? -1.1 : -4.2));
                    float4 lace = nz(P * 0.72 + sea * (t * 0.040) + float2(0.11, 0.57), lodG - 0.47);
                    if (wet)
                    {
                        // three trains of waves: the swell that rolls in to the shore, a second one across it, and a chop
                        float calm = saturate(1.25 - dist / 55.0);
                        float p1 = 1.75 * s + 1.00 * t + (g.a - 0.5) * 5.0;
                        float p2 = 2.7 * (s * 0.94 + l * 0.34) + 1.35 * t + 1.7;
                        float p3 = 5.1 * (s * 0.80 - l * 0.60) + 2.05 * t;
                        float c1 = cos(p1), c2 = cos(p2), c3 = cos(p3);
                        float ds = (0.070 * c1 + 0.050 * 0.94 * c2 + 0.034 * 0.80 * c3) * calm;
                        float dl = (0.050 * 0.34 * c2 - 0.034 * 0.60 * c3) * calm;
                        float2 ripples = (na.gb - 0.5) * 0.55 + (nb.gb - 0.5) * 0.75;             // the small waves: two sheets sliding over each other
                        float2 slope = sea * ds + along * dl + ripples;
                        float3 n = normalize(float3(-slope.x, 1.0, -slope.y));
                        float3 R = reflect(d, n); R.y = abs(R.y) + 0.015; R = normalize(R);
                        float fres = 0.025 + 0.975 * pow(1.0 - saturate(dot(-d, n)), 5.0);
                        float3 deep = _C0.rgb * float3(0.20, 0.42, 0.72) * lerp(1.0, 0.55, dusk);
                        float3 shallow = lerp(_C2.rgb, float3(1.0, 1.0, 1.0), 0.10) * lerp(1.0, 0.60, dusk);
                        float3 body = lerp(shallow, deep, 1.0 - exp(-dp * 0.26));
                        float3 sandUnder = float3(0.66, 0.54, 0.36) * sandTint;
                        body = lerp(sandUnder, body, saturate(dp * 1.1 + 0.25));                 // the sand shows through where it is shallow
                        col = lerp(body, skyOf(R, S, dusk), fres);
                        // the path of light under the sun: it breaks up on the small waves and sparkles
                        float glint = pow(saturate(dot(R, S)), lerp(260.0, 120.0, dusk)) * (0.35 + 2.4 * smoothstep(0.48, 0.72, lace.a));
                        col += light * glint * lerp(2.6, 3.6, dusk);
                        // foam: where the swell breaks near the shore, and the lace along the waterline
                        float crest = smoothstep(0.80, 0.995, sin(p1)) * exp(-dp * 0.45) * smoothstep(0.36, 0.60, lace.a + 0.16);
                        float rim = smoothstep(0.38, 0.0, dp) * smoothstep(0.26, 0.52, lace.a + 0.34 * (1.0 - saturate(dp / 0.38)));
                        col = lerp(col, foamCol, saturate(crest * 0.90 + rim));
                        col = lerp(col, haze, exp(d.y * 26.0) * 0.82);                           // sea and sky meet in a soft haze
                    }
                    else
                    {
                        // the beach: soft hollows and fine ripples lit from the side the sun is on; dark where the water
                        // has just been, with a sheen
                        float2 rip = (na.gb - 0.5) * 0.55 + (nb.gb - 0.5) * 0.9;
                        float3 ns = normalize(float3(-rip.x, 1.0, -rip.y));
                        float shadeS = 0.92 + lerp(0.55, 0.70, dusk) * (dot(ns, S) - S.y);
                        float3 dry = lerp(float3(0.80, 0.65, 0.42), float3(0.90, 0.76, 0.52), nb.r) * sandTint;
                        float damp = smoothstep(reach - 0.55, reach + 0.10, s);                  // where the water comes: damp sand
                        float film = smoothstep(0.85, 0.0, -dp) * damp;                          // just left by the water: it shines
                        col = dry * shadeS * lerp(1.0, 0.58, damp);
                        float3 up3 = normalize(float3(-rip.x * 0.10, 1.0, -rip.y * 0.10));
                        float3 Rs = reflect(d, up3); Rs.y = abs(Rs.y) + 0.01; Rs = normalize(Rs);
                        float sheen = (0.06 + 0.94 * pow(1.0 - saturate(-d.y), 4.0)) * (0.16 * damp + 0.62 * film);
                        col = lerp(col, skyOf(Rs, S, dusk) + light * pow(saturate(dot(Rs, S)), 90.0) * 1.5, sheen);
                        float strand = smoothstep(0.10, 0.0, -dp) * smoothstep(0.34, 0.60, lace.a + 0.2);     // the line of foam the water leaves
                        col = lerp(col, foamCol, strand * 0.9);
                        col = lerp(col, lerp(haze, dry, 0.45), exp(d.y * 30.0) * 0.6);
                    }
                }
                return col;
            }

            // ------------------------------------------------------------ the mountain top
            // The user stands on a summit of rock and old snow; round it a sea of clouds, and out of that six ranges,
            // each paler and bluer than the one in front of it, with one great snow peak just beside the work area.
            // A skyline is made of CRESTS: where the soft hills of the noise cross their middle height there is a pointed
            // summit, between two of them a round saddle - and on which side of a crest a face lies says whether the low
            // sun reaches it, so every peak is split into a lit and a shaded side along a rib that runs down from it.
            float crest(float h) { float c = 1.0 - saturate(abs(h - 0.5) * 4.4); return c * (0.62 + 0.38 * c); }
            // does the ground rise (+) or fall (-) in the direction `along` (of the picture of noise)?
            float rise(float4 h, float2 along) { return clamp((0.5 - h.r) * 30.0, -1.0, 1.0) * clamp(dot(h.gb - 0.5, along) * 18.0, -1.0, 1.0); }
            float skyline(float4 h, float env) { return crest(h.r) * env + (h.a - 0.55) * 0.28 - 0.40; }

            // One range, laid over what is behind it, and the clouds at its foot over the range. top: how high its skyline
            // stands here (the sine of the angle), foot: where the sea of clouds laps it, turn: how its face here is
            // turned to the sun (-1 away .. 1 towards), snowLine: from where up it is under snow, away: how much of it
            // the air between has taken (0 near .. 1 far), det: the grain of the face (fine: how much of it is seen),
            // wisp: the mist that rises from the clouds at its foot.
            float3 ridge(float3 col, float y, float top, float foot, float turn, float snowLine, float away, float4 det, float fine, float3 haze, float3 light, float3 deck, float wisp, float aa, float soft, float rim, float spark, float t)
            {
                float body = smoothstep(top + aa, top - aa, y);
                float depth = max(top - y, 0.0);
                float sun = smoothstep(-0.30, 0.30, turn);
                float rib = smoothstep(0.72, 0.88, det.a) * fine;                               // the ribs of rock that stand out of the face
                // rock: blue-grey in the shade, warm where the low sun reaches it; beds of it one over the other, dark cracks
                float3 rock = lerp(float3(0.115, 0.135, 0.215), float3(0.47, 0.39, 0.33) + light * 0.10, sun) * (0.70 + 0.60 * lerp(0.5, det.a, fine));
                rock *= 1.0 - fine * (0.45 * smoothstep(0.035, 0.008, abs(det.r - 0.5)) + 0.20 * smoothstep(0.10, 0.0, frac(det.r * 8.0 + depth * 18.0)));
                // snow above the snow line (its edge runs down the gullies), the ribs showing through it
                float snow = smoothstep(-0.012, 0.012, y - snowLine + ((det.r - 0.5) * 0.07 - rib * 0.03) * fine) * (1.0 - rib * 0.80);
                float3 snowCol = lerp(float3(0.42, 0.54, 0.86), float3(1.0, 0.96, 0.90) + light * 0.06, sun);
                snowCol += light * (spark * sun * smoothstep(0.012, 0.0, abs(det.r - 0.62)) * smoothstep(0.75, 1.0, sin(det.a * 45.0 + t * 2.0)));
                rock = lerp(rock, snowCol, snow * (0.96 - 0.10 * away));
                // the air between: the farther, the paler and bluer; and it gathers towards the foot of every range
                float low = saturate(depth / max(top - foot, 0.03));
                rock = lerp(rock, haze, saturate(away + low * low * 0.30 * (0.30 + 0.70 * away)));
                rock += light * (rim * smoothstep(0.012, 0.0, depth));                           // the skyline glows towards the sun
                col = lerp(col, rock, body);
                // the cloud that laps it: mist rising over the rock, and the bright crown of the bank in front
                float3 cloudTop = lerp(float3(1.0, 0.98, 0.94) + light * 0.06, haze, away * 0.85);
                col = lerp(col, lerp(cloudTop, haze, 0.35), saturate(wisp * exp(-max(y - foot, 0.0) / (soft * 2.0))) * body);
                return lerp(col, lerp(cloudTop, deck, saturate((foot - y) / (soft * 3.0))), smoothstep(foot + soft, foot - soft, y));
            }

            // dw: the direction looked in (world); f, r: where the place is anchored (forward, right); wide: the half
            // width of the work area when the place came on; eyeW: where this eye is over the place's ground (in units of
            // the height the eyes had over it). Look-ups of the noise: 2 on the summit underfoot (6 at its rim: the
            // clouds beyond it), 4 in the sea of clouds near by and 3 far off, 2 in the sky; where the ranges stand at
            // most 10 (three of the clouds or two of the sky, five skylines, the face of the range in front, its grain).
            float3 placePeak(float3 dw, float3 f, float3 r, float t, float wide, float lod0, float3 eyeW)
            {
                // the place's own frame: x to the right of where the work area was, z towards it - so the view is the
                // same picture whichever way the desk stands in the room
                float3 d = float3(dot(dw, r), dw.y, dot(dw, f));
                float y = d.y;
                float2 ring = normalize(d.xz + float2(1e-5, 0.0));
                float2 tang = float2(ring.y, -ring.x);                                          // the way round the horizon, to the right
                float psi = atan2(d.x, d.z);                                                    // (only in sines of whole multiples and in whole turns: no seam behind the user)
                float lodRing = lod0 - log2(max(sqrt(1.0 - y * y), 0.08));
                float aa = exp2(lod0) * 0.006 + 0.0012;
                float3 S = aim(float3(0.0, 0.0, 1.0), float3(1.0, 0.0, 0.0), wide + 22.0, 8.5);   // the sun: just outside the work area, on the right
                float2 sun2 = normalize(S.xz);
                float3 light = lerp(float3(1.0, 0.96, 0.88), _C1.rgb, 0.70);
                float side = clamp((ring.x * sun2.y - ring.y * sun2.x) * 3.0, -1.0, 1.0);       // looking to the right of the sun (+) or to its left (-)
                float cs = dot(ring, sun2);                                                     // 1 looking towards the sun, -1 with the sun behind
                float3 haze = lerp(skyOf(float3(ring.x, 0.0, ring.y), S, 0.0), _C2.rgb, 0.50);

                // ---- the summit underfoot: a ledge of rock in terraces and old snow - a sheet that lies in the room, one
                // unit below the eyes, and ends in a broken rim; beyond the rim, far below, the clouds. Looked up at the
                // size of what is seen: straight down, a pixel of the headset is about two thirds of a texel of the fine look-up.
                float3 ground = float3(0.0, 0.0, 0.0); float onLedge = 0.0;
                if (y < -0.12)
                {
                    float invG = eyeW.y / -y;
                    float2 G = float2(dot(eyeW, r), dot(eyeW, f)) + d.xz * invG; float distG = length(G);
                    float psiG = atan2(G.x, G.y);
                    float rimR = 1.85 + 0.30 * sin(2.0 * psiG + 1.0) + 0.20 * sin(5.0 * psiG + 0.4) + 0.10 * sin(11.0 * psiG + 2.0);
                    if (distG < rimR * 1.10)
                    {
                        float lodG = lod0 + 2.0 * log2(invG) - 1.0;
                        float4 g1 = nz(G * 0.31 + float2(0.21, 0.67), max(lodG - 1.69, 1.5));        // slabs and drifts
                        float4 g2 = nz(G * 2.3 + float2(0.50, 0.13), lodG + 1.20);                  // cracks and grain
                        float edge = rimR * (1.0 + (g1.a - 0.55) * 0.10 + (g2.a - 0.55) * 0.035);
                        float pix = exp2(lod0) * 0.0039 * (1.0 + distG * distG);                    // a pixel, measured along the ground
                        onLedge = smoothstep(edge + pix, edge - pix, distG);
                        // rock in terraces: every step of height is a slab of its own tone; the riser of a step that faces
                        // the low sun is a line of light, behind one that faces away lies its shadow
                        float v = g1.r * 9.0 + g2.r * 1.1;
                        float slab = floor(v), fr = v - slab;
                        float facing = dot(normalize((g1.gb - 0.5) * 2.8 + (g2.gb - 0.5) * 2.5 + float2(1e-4, 0.0)), sun2);     // + : the ground rises towards the sun
                        float3 stone = float3(0.50, 0.44, 0.40) * (0.66 + 0.40 * hash(float2(slab, 7.3))) * (0.86 + 0.26 * g2.a);
                        stone *= 1.0 - 0.40 * smoothstep(0.022, 0.004, abs(g2.r - 0.5));              // cracks
                        stone = lerp(stone, float3(0.15, 0.17, 0.27), smoothstep(0.68, 1.0, fr) * saturate(facing * 1.5) * 0.85);
                        stone += light * (smoothstep(0.16, 0.0, fr) * saturate(-facing * 1.5) * 0.30);
                        float drift = smoothstep(0.500, 0.535, g1.r + (g2.r - 0.5) * 0.10 + (g2.a - 0.60) * 0.05);
                        float sunS = saturate(0.62 - dot((g1.gb - 0.5) * 2.4 + (g2.gb - 0.5) * 0.8, sun2));
                        float3 snowG = lerp(float3(0.52, 0.64, 0.93), float3(1.0, 0.97, 0.92) + light * 0.05, sunS);
                        snowG += light * (sunS * smoothstep(0.014, 0.0, abs(g2.r - 0.62)) * smoothstep(0.75, 1.0, sin(g2.a * 45.0 + t * 2.0)));
                        ground = lerp(stone, snowG, drift * (1.0 - smoothstep(0.62, 0.86, distG / edge)));
                        ground = lerp(ground, float3(0.09, 0.10, 0.16), smoothstep(0.95, 1.0, distG / edge) * 0.85);   // the lip of the rim
                    }
                }
                if (onLedge > 0.998) return ground;                                             // (nothing else is looked up there)

                float3 col = haze; float3 deck = haze; float lap = 0.0; float wisp = 0.0;
                if (y > 0.0)
                {
                    // ---- the sky: deep overhead, pale at the horizon; the sun's glow and soft rays; thin high cloud
                    float3 sky = skyOf(d, S, 0.0) + sunDisc(d, S, light);
                    float3 toS = d - S;
                    float fan = atan2(toS.y, toS.x * sun2.y - toS.z * sun2.x + 1e-5);
                    float awayS = length(toS);
                    sky += light * pow(abs(sin(fan * 4.0 + sin(fan * 9.0) * 0.8 + t * 0.03)), 6.0) * exp(-awayS * 2.6) * 0.09 * smoothstep(0.03, 0.14, awayS);
                    float2 hp = d.xz / (y + 0.26);
                    float lodH = lod0 - 2.0 * log2(y + 0.26);
                    float2 hs = float2(hp.x * 0.80 + hp.y * 0.60, hp.y * 0.80 - hp.x * 0.60);       // along the streaks, across them
                    float4 c1 = nz(float2(hs.x * 0.14, hs.y * 0.42) + float2(t * 0.0010, 0.3), lodH - 0.25);   // where the cloud is
                    float4 c2 = nz(float2(hs.x * 0.45, hs.y * 1.35) + float2(t * 0.0022, 0.61), lodH + 1.40);  // its fibres (a little out of focus)
                    float cirrus = smoothstep(0.68, 0.96, c1.a * 0.45 + c1.r * 0.45 + c2.a * 0.45 - 0.10) * smoothstep(0.06, 0.32, y) * 0.36 * saturate(_Density);
                    float towards = saturate(0.5 + ((c1.g - 0.5) * 0.2 * (sun2.x * 0.80 + sun2.y * 0.60) + (c1.b - 0.5) * (sun2.y * 0.80 - sun2.x * 0.60)) * 3.0);
                    col = lerp(sky, lerp(lerp(sky, float3(0.70, 0.76, 0.88), 0.5), float3(1.0, 0.97, 0.92) + light * 0.10, towards), cirrus);
                    col = lerp(col, haze, exp(-y * 22.0) * 0.80);
                }
                else
                {
                    // ---- the sea of clouds: a sheet below. Banks (q1) and the puffs on them (q2: round tops, creased
                    // between) drift at their own paces; a puff is bright where it is turned to the sun (q2s: the same
                    // puffs a step towards the sun - higher there means this side is turned away) and blue where it is
                    // turned away and in the creases; near by, the small billows too (q3). Where a bank thins out, the
                    // valley shows far below.
                    float inv = 1.0 / max(-y, 0.012);
                    float2 P = d.xz * inv;
                    float lodP = lod0 + 2.0 * log2(inv) - 1.0;
                    float nearBy = smoothstep(-0.045, -0.11, y);
                    float4 q1 = nz(P * 0.045 + float2(0.80, 0.60) * (t * 0.0034) + float2(0.11, 0.37), lodP - 4.47);
                    float2 u2 = P * 0.17 + float2(0.96, -0.28) * (t * 0.017) + float2(0.63, 0.20);
                    float4 q2 = nz(u2, lodP - 2.56);
                    float4 q2s = nz(u2 + sun2 * 0.045, lodP - 2.56);
                    float4 q3 = float4(0.5, 0.5, 0.5, 0.55);
                    if (nearBy > 0.0) q3 = nz(P * 0.83 + float2(0.80, 0.60) * (t * 0.070) + float2(0.29, 0.83), lodP - 0.27);
                    float puff = (1.0 - q2.a) * 0.62 + q2.r * 0.38 + nearBy * ((0.55 - q3.a) * 0.22 + (q3.r - 0.5) * 0.12);
                    float puffS = (1.0 - q2s.a) * 0.62 + q2s.r * 0.38;
                    float H = q1.r * 0.55 + puff * 0.45;
                    float towards = (puff - puffS) * 2.6 - dot(q1.gb - 0.5, sun2) * 2.0 - dot(q3.gb - 0.5, sun2) * (nearBy * 0.9);
                    float sunC = saturate(0.58 + towards + (puff - 0.44) * 0.9 + (q1.r - 0.5) * 0.5);
                    sunC = sunC * sunC * (3.0 - 2.0 * sunC);
                    float3 shade = lerp(float3(0.47, 0.56, 0.82), float3(0.66, 0.74, 0.93), saturate(puff * 1.6));
                    deck = lerp(shade, float3(1.0, 0.975, 0.93) + light * 0.05, sunC);
                    float sunward = saturate(cs);
                    // the banks stand one behind the other: each rises from its shaded foot, behind the crown of the one in
                    // front of it, up to its own sunlit crown (rows by the distance; a crown stands as high as the cloud is
                    // heaped there). Towards the sun the faces are in shade and the crowns are rimmed with light.
                    float aaV = aa * 3.2 / -y;                                                   // a pixel, in rows
                    float rowAmt = smoothstep(-0.55, -0.30, y) * saturate(1.5 - aaV * 4.0);
                    float v = 2.2 * log2(inv) - (q1.r - 0.5) * 2.0 - (puff - 0.44) * 0.5 + 0.45 * sin(3.0 * psi + 1.0) + 0.30 * sin(7.0 * psi + 4.0);
                    float fr = frac(v);
                    float crown = smoothstep(0.74, 0.96, fr) * (1.0 - 0.5 * smoothstep(1.0 - aaV, 1.0, fr));
                    deck = lerp(deck, shade * 0.90, (1.0 - smoothstep(0.0, 0.55, fr)) * rowAmt * (0.50 + 0.30 * sunward - 0.15 * saturate(-cs)));
                    deck = lerp(deck, float3(1.0, 0.98, 0.94) + light * 0.08, crown * rowAmt * (0.70 + 0.30 * sunward));
                    float gap = smoothstep(0.385, 0.335, q1.r * 0.80 + puff * 0.25) * saturate(_Density);
                    deck = lerp(deck, lerp(float3(0.12, 0.22, 0.42), haze, 0.12), gap * 0.90);
                    deck += light * (sunward * sunward * (0.06 + 0.14 * sunC * (1.0 - gap)));    // warm towards the sun
                    float farOff = exp(y * 14.0);
                    deck = lerp(deck, haze, farOff * 0.80);
                    lap = (H - 0.47) * 2.2 * (1.0 - farOff * farOff);
                    wisp = saturate(0.5 + (H - 0.47) * 2.0) * 0.28 * (1.0 - farOff * farOff);
                    col = deck;
                }

                // ---- six ranges, the farthest first. A skyline is one look-up round the horizon (the two farthest
                // share one); a look-up is only made where its range can stand.
                float heroYaw = -(wide + 34.0);                                                 // the great peak: just outside the work area, on the left
                float hx = (degrees(psi) - heroYaw) / 27.0;
                float heroP = pow(saturate(1.0 - abs(hx)), 1.5) + 0.30 * pow(saturate(1.0 - abs(hx - 0.62) / 0.34), 1.4);
                float heroM = smoothstep(0.0, 0.22, heroP);
                float t0 = -2.0, t1 = -2.0, t2 = -2.0, t3 = -2.0, t4 = -2.0, t5 = -2.0;
                float4 h1 = float4(0.5, 0.5, 0.5, 0.5);
                if (y > -0.035 && y < 0.085)
                {
                    h1 = nz(ring * 2.2 + float2(0.17, 0.43), max(lodRing + 0.84, 1.0));
                    t0 = 0.004 + 0.050 * h1.a * (0.75 + 0.25 * sin(4.0 * psi + 1.3));
                    t1 = 0.030 + 0.055 * skyline(h1, 0.70 + 0.18 * sin(3.0 * psi + 0.7) + 0.12 * sin(8.0 * psi + 2.1));
                }
                if (y > -0.060 && y < 0.115)
                    t2 = 0.036 + 0.085 * skyline(nz(ring * 1.5 + float2(0.71, 0.09), max(lodRing + 0.28, 1.2)), 0.68 + 0.20 * sin(2.0 * psi + 4.0) + 0.12 * sin(7.0 * psi + 0.3));
                if (y > -0.100 && y < 0.150 + 0.34 * heroP)
                    t3 = 0.030 + 0.130 * skyline(nz(ring * 1.05 + float2(0.35, 0.77), max(lodRing - 0.23, 1.5)), 0.66 + 0.20 * sin(3.0 * psi + 2.6) + 0.14 * sin(5.0 * psi + 5.0)) * (1.0 - 0.5 * heroM) + 0.33 * heroP;
                if (y > -0.180 && y < 0.130)
                    t4 = -0.020 + 0.180 * skyline(nz(ring * 0.75 + float2(0.89, 0.31), max(lodRing - 0.72, 1.5)), 0.64 + 0.22 * sin(2.0 * psi + 0.9) + 0.14 * sin(5.0 * psi + 3.3)) * (1.0 - 0.5 * heroM) - 0.03 * heroM;
                if (y > -0.320 && y < 0.020)
                    t5 = -0.150 + 0.200 * skyline(nz(ring * 0.55 + float2(0.52, 0.66), max(lodRing - 1.16, 1.5)), 0.60 + 0.24 * sin(3.0 * psi + 5.2) + 0.16 * sin(4.0 * psi + 1.1));
                float f0 = -0.010 + lap * 0.008, f1 = -0.018 + lap * 0.014, f2 = -0.032 + lap * 0.024, f3 = -0.058 + lap * 0.040, f4 = -0.115 + lap * 0.065, f5 = -0.210 + lap * 0.100;
                // the range in front here: its face (the same noise as its skyline, followed down the slope: the ribs that
                // run down from its summits) and its grain (wrapped round the horizon a whole number of times and hung
                // from the skyline, leaning the way the slope falls)
                float front = 0.0;
                if (y < t5 + aa && y > f5 - 0.040) front = 5.0;
                else if (y < t4 + aa && y > f4 - 0.024) front = 4.0;
                else if (y < t3 + aa && y > f3 - 0.015) front = 3.0;
                else if (y < t2 + aa && y > f2 - 0.009) front = 2.0;
                float4 det = float4(0.5, 0.5, 0.5, 0.5);
                float turn = -cs * 0.25;
                if (front > 1.5)
                {
                    float kF = front > 4.5 ? 0.55 : front > 3.5 ? 0.75 : front > 2.5 ? 1.05 : 1.5;
                    float2 oF = front > 4.5 ? float2(0.52, 0.66) : front > 3.5 ? float2(0.89, 0.31) : front > 2.5 ? float2(0.35, 0.77) : float2(0.71, 0.09);
                    float topF = front > 4.5 ? t5 : front > 3.5 ? t4 : front > 2.5 ? t3 : t2;
                    float depth = max(topF - y, 0.0);
                    float4 hf = nz(ring * (kF * (1.0 + depth * 0.45)) + oF, max(lodRing + log2(kF) - 0.3, front > 2.5 ? 1.5 : 1.2));
                    float rs = rise(hf, tang);
                    float turnsN = front > 4.5 ? 15.0 : front > 3.5 ? 18.0 : front > 2.5 ? 21.0 : 26.0;
                    det = nz(float2(psi * 0.15915494 * turnsN + front * 0.37 + (rs > 0.0 ? 1.2 : -1.2) * depth, depth * 1.6 + front * 0.21), lodRing + log2(turnsN * 0.159) - 0.8);
                    float bump = (det.g - 0.5) * 5.0;
                    turn = (rs * 0.85 + bump) * side - cs * 0.25;
                    if (front > 2.5 && front < 3.5)
                    {
                        // the great peak: one rib from its summit down, wandering; the side the sun is on is lit
                        float crossH = sin(radians(heroYaw) - atan2(sun2.x, sun2.y));
                        float sideH = (crossH < 0.0 ? -1.0 : 1.0) * max(min(abs(crossH) * 3.0, 1.0), 0.75);
                        float xe = hx + (hf.r - 0.5) * 0.5 * saturate(depth * 5.0);
                        turn = lerp(turn, (-clamp(xe * 14.0, -1.0, 1.0) * 0.75 + rs * 0.30 + bump) * sideH - cs * 0.10, heroM);
                    }
                }
                float glow = saturate(cs); glow *= glow;
                col = ridge(col, y, t0, f0, -cs * 0.25, 0.018, 0.86, det, 0.0, haze, light, deck, wisp, aa, 0.004, glow * 0.10, 0.0, t);
                col = ridge(col, y, t1, f1, rise(h1, tang) * 0.85 * side - cs * 0.25, 0.022, 0.74, det, 0.0, haze, light, deck, wisp, aa, 0.006, glow * 0.14, 0.0, t);
                col = ridge(col, y, t2, f2, turn, 0.014, 0.58, det, 0.50, haze, light, deck, wisp, aa, 0.009, glow * 0.20, 0.0, t);
                col = ridge(col, y, t3, f3, turn, lerp(-0.008, -0.10, heroM), 0.40 - 0.08 * heroM, det, 0.85, haze, light, deck, wisp, aa, 0.015, glow * 0.25, 0.5 * heroM, t);
                col = ridge(col, y, t4, f4, turn, -0.075, 0.20, det, 1.0, haze, light, deck, wisp, aa, 0.024, glow * 0.30, 0.6, t);
                col = ridge(col, y, t5, f5, turn, -0.170, 0.05, det, 1.0, haze, light, deck, wisp, aa, 0.040, glow * 0.30, 1.0, t);
                // the glow of the low sun lies over everything on its side
                float sd = saturate(dot(d, S));
                col += light * (sd * sd * sd * sd * 0.08);
                return lerp(col, ground, onLedge);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                // the work area: a window round _WorkDir (level), so many degrees to each side, up and down
                float3 f = normalize(float3(_WorkDir.x, 0.0, _WorkDir.z) + 1e-5);
                float3 r = float3(f.z, 0.0, -f.x);
                float yaw = degrees(atan2(dot(d, r), dot(d, f)));
                float pitch = degrees(asin(clamp(d.y, -1.0, 1.0)));
                float e = max(_WorkSize.w, 0.5);
                float inside = (1.0 - smoothstep(_WorkSize.x - e, _WorkSize.x + e, abs(yaw)))
                             * (1.0 - smoothstep(_WorkSize.y - e, _WorkSize.y + e, pitch))
                             * (1.0 - smoothstep(_WorkSize.z - e, _WorkSize.z + e, -pitch));
                float outside = (1.0 - inside) * _Amount;

                float t = (_Time.y + _TimeShift) * _Speed;
                float yawAll = atan2(d.x, d.z);                           // -pi..pi all round
                float2 sky = float2(yawAll / 6.2831853 + 0.5, d.y * 0.5 + 0.5);
                float3 add = 0; float cover = 0;
                if (_Pattern > 7.5)
                {
                    // a place takes the whole room outside the work area (the pattern's dim says how much of the room shows
                    // through); inside the work area nothing of it is worked out at all
                    float covers = saturate(_Dim + 0.25) * outside;
                    if (covers < 0.003) return half4(0.0, 0.0, 0.0, 0.0);
                    // (what varies round the user is looked up on a circle or on a flat sheet above / below, never by the
                    // angle itself: an angle jumps where it wraps round, and the view would have a seam behind the user)
                    float2 ring = normalize(d.xz + float2(1e-5, 0.0));
                    // A place is ANCHORED IN THE WORLD: where its sun, its sea and its great peak are is fixed at the moment
                    // it comes on (_PlaceDir: where the work area was then, and how wide), and never follows the work
                    // area's window afterwards - that window is worked out from the head's position all the time, and a
                    // place hung on it swung round the user by several degrees with every turn of the head.
                    if (_PlaceDir.w > 0.5)
                    {
                        f = normalize(float3(_PlaceDir.x, 0.0, _PlaceDir.z) + 1e-5);
                        r = float3(f.z, 0.0, -f.x);
                        yaw = degrees(atan2(dot(d, r), dot(d, f)));
                    }
                    float wide = max(_PlaceDir.w > 0.5 ? _PlaceDir.w : _WorkSize.x, 40.0);
                    float lod0 = log2(256.0 * 2.0 / (_ScreenParams.y * abs(UNITY_MATRIX_P._m11)));     // texels of the noise per pixel, for a look-up that turns once per radian
                    float lodRing = lod0 - log2(max(sqrt(1.0 - d.y * d.y), 0.08));
                    // the ground of a place (the beach and the sea, the summit underfoot) is a sheet that LIES IN THE ROOM, one
                    // unit below where the eyes were when the place came on (_PlaceAt): each eye sees it from where that eye
                    // is, so it stays put when the head moves and has its true depth. (Not given: it hangs under the eyes.)
                    float3 eye = float3(0.0, 1.0, 0.0);
                    if (_PlaceAt.w > 0.01)
                    {
                        eye = (GetCameraPositionWS() - _PlaceAt.xyz) / _PlaceAt.w;
                        eye.y = max(eye.y, 0.25);
                    }
                    float3 scene;
                    if (_Pattern < 8.5) scene = placePeak(d, f, r, t, wide, lod0, eye);        // (an `if`: only the place that is on is worked out)
                    else scene = placeCoast(d, f, r, t, wide, lod0, ring, lodRing, yawAll, eye);
                    return half4(scene * covers, covers);
                }
                if (_Pattern > 0.5 && _Pattern < 1.5)
                {
                    // club: a few big soft lights wandering over the walls, thin beams sweeping round, small lights blinking
                    for (int k = 0; k < 5; k++)
                    {
                        float fk = k;
                        float3 c = normalize(float3(sin(t * (0.31 + 0.07 * fk) + fk * 2.1), 0.25 * sin(t * 0.5 + fk * 1.7) + 0.1, cos(t * (0.27 + 0.05 * fk) + fk * 1.3)));
                        float s = smoothstep(0.90, 0.985, dot(d, c));
                        add += lerp(_C1.rgb, _C2.rgb, frac(fk * 0.37 + t * 0.05)) * s * (0.55 + 0.45 * _Beat);
                    }
                    float beams = pow(abs(sin(yawAll * 3.0 + t * 0.9)), 40.0) * smoothstep(-0.2, 0.7, d.y);
                    add += _C2.rgb * beams * (0.35 + 0.5 * _Beat) * _Density;
                    float2 gid = floor(sky * float2(60.0, 30.0));
                    float dots = drops(sky, float2(60.0, 30.0) * _Density, 0.0, 0.16, 1.0, t) * (0.5 + 0.5 * sin(t * 3.0 + hash(gid) * 6.28));
                    add += _C1.rgb * dots * 0.6;
                }
                else if (_Pattern > 1.5 && _Pattern < 2.5)
                {
                    float2 g = sky * float2(140.0, 70.0) * _Density; float2 id = floor(g);
                    float tw = 0.5 + 0.5 * sin(t * 2.0 + hash(id) * 40.0);
                    float star = smoothstep(0.12, 0.02, length(frac(g) - 0.2 - 0.6 * hash2(id))) * step(0.80, hash(id + 3.1));
                    add += lerp(_C1.rgb, _C2.rgb, hash(id + 9.0)) * star * (0.4 + 0.6 * tw) * smoothstep(-0.15, 0.25, d.y);
                }
                else if (_Pattern > 2.5 && _Pattern < 3.5)
                {
                    // three layers at different distances: the near ones few, long and quick, the far ones many and fine
                    float level = 1.0 - smoothstep(0.80, 0.98, abs(d.y));                 // (the columns run together straight above and below)
                    float a1 = streaks(sky, 300.0 * _Density, 5.0, 0.55, 0.10, 0.55, 0.30, t);
                    float a2 = streaks(sky + 0.37, 150.0 * _Density, 3.5, 0.75, 0.07, 0.70, 0.24, t);
                    float a3 = streaks(sky + 0.71, 520.0 * _Density, 8.0, 0.42, 0.12, 0.50, 0.30, t);
                    add += (_C1.rgb * (a1 * 0.55 + a2 * 0.50) + _C2.rgb * a3 * 0.30) * level;
                    cover = 0.10;
                }
                else if (_Pattern > 3.5 && _Pattern < 4.5)
                {
                    float band = sin(yawAll * 2.0 + t * 0.4 + sin(yawAll * 5.0 - t * 0.3) * 0.8) * 0.5 + 0.5;
                    float h = smoothstep(0.05, 0.45, d.y) * (1.0 - smoothstep(0.55, 0.95, d.y));
                    float curtain = pow(band, 2.0) * h * (0.7 + 0.3 * sin(yawAll * 40.0 + t * 2.0));
                    add += lerp(_C1.rgb, _C2.rgb, saturate(d.y * 1.6)) * curtain * 0.9;
                    float2 g = sky * float2(140.0, 70.0); float2 id = floor(g);
                    add += _C2.rgb * smoothstep(0.08, 0.02, length(frac(g) - 0.5)) * step(0.9, hash(id)) * 0.5;
                }
                else if (_Pattern > 4.5)
                {
                    // snow, petals, bubbles: soft dots drifting down (or up), in two layers
                    float up = _Pattern > 6.5 ? -1.0 : 1.0;
                    float sway = sin(t * 0.7 + sky.y * 9.0) * 0.004;
                    float stretch = (_Pattern > 5.5 && _Pattern < 6.5) ? 1.8 : 1.0;
                    float a1 = drops(sky + float2(sway, 0.0), float2(46.0, 16.0) * _Density, 0.10 * up, 0.20, stretch, t);
                    float a2 = drops(sky + float2(-sway + 0.21, 0.4), float2(28.0, 10.0) * _Density, 0.07 * up, 0.18, 1.0, t);
                    add += _C1.rgb * a1 * 0.8 + _C2.rgb * a2 * 0.6;
                }
                float alpha = saturate(_Dim + cover) * outside;
                float3 col = _C0.rgb * alpha + add * outside;
                return half4(col, saturate(alpha + dot(add, float3(0.33, 0.33, 0.33)) * outside * 0.6));
            }
            ENDHLSL
        }
    }
}
