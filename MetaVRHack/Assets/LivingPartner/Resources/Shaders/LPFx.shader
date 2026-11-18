// Premultiplied-alpha motion-graphics shader for everything that is not the character: contact shadow, pointer,
// portal rings, ripples, sparkles, gradient panels and pills, radial progress. One quad, one mode.
// _Mode: 0 soft disc | 1 ring | 2 rounded paper panel with a drawn outline | 3 frame | 4 radial progress | 5 four-point sparkle
//        6 ripple | 7 dashed rotating ring | 8 soft glow blob
//        water: 9 rain streak | 10 splash ripple | 11 thin sheet of water | 12 flying droplet
// _Param: meaning depends on the mode (see below). _Color -> _Color2 is the gradient.
Shader "LivingPartner/Fx"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Color2 ("Color 2", Color) = (1, 1, 1, 1)
        _Mode ("Mode", Float) = 0
        _Param ("Param", Vector) = (0.8, 0.1, 0, 0)
        _Phase ("Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _Color2;
            float _Mode;
            float4 _Param;
            float _Phase;
            float4 _ClipPlane;
            float _ClipSign;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float3 hueMix(float t)
            {
                // _Color -> _Color2 -> _Color, smooth and periodic (t in turns)
                float k = 0.5 - 0.5 * cos(t * 6.2831853);
                return lerp(_Color.rgb, _Color2.rgb, k);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_ClipSign != 0) clip(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w));
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float ang = atan2(p.y, p.x) / 6.2831853;      // -0.5..0.5 turns
                float a = 0;
                float3 col = _Color.rgb;
                float glow = 0;

                if (_Mode < 0.5)
                {
                    a = 1.0 - smoothstep(_Param.x * 0.2, 1.0, r);                               // soft disc
                }
                else if (_Mode < 1.5)
                {
                    // ring: x radius, y width. Gradient runs round the ring and drifts with _Phase.
                    float w = max(_Param.y, 0.01);
                    float d = abs(r - _Param.x);
                    a = 1.0 - smoothstep(w * 0.45, w, d);
                    glow = exp(-d * d / (w * w * 3.5)) * 0.30;                                  // tight halo: the opening itself stays clear
                    col = hueMix(ang + _Phase);
                    a = max(a, glow) * step(r, 1.0);
                }
                else if (_Mode < 2.5)
                {
                    // gradient rounded panel: x corner radius (in short-side units), y border glow width, z aspect (w/h)
                    float asp = _Param.z > 0.001 ? _Param.z : 1.0;
                    float2 q = p * float2(max(asp, 1.0), max(1.0 / asp, 1.0));
                    float2 hs = float2(max(asp, 1.0), max(1.0 / asp, 1.0));
                    float rad = min(_Param.x, 1.0);
                    float2 dd = abs(q) - (hs - rad);
                    float d = length(max(dd, 0.0)) + min(max(dd.x, dd.y), 0.0) - rad;            // signed distance, <0 inside
                    float soft = _Param.w > 0.0 ? _Param.w : 0.02;                                 // w: edge softness (0 = the usual soft edge)
                    float inside = 1.0 - smoothstep(-soft, 0.0, d);
                    float t = saturate(i.uv.y * 0.75 + i.uv.x * 0.25);
                    col = lerp(_Color2.rgb, _Color.rgb, t);
                    // a thin outline just inside the edge, drawn in dark-brown ink (the line the partner itself is drawn with)
                    float bw = max(_Param.y, 0.0001);
                    float rim = (1.0 - smoothstep(bw * 0.7, bw, -d)) * inside * step(0.0005, _Param.y);
                    col = lerp(col, float3(0.070, 0.028, 0.020), rim * 0.94);
                    a = inside;
                    float al = lerp(lerp(_Color2.a, _Color.a, t), 1.0, rim);
                    return half4(col * a * al * i.color.a, a * al * i.color.a);
                }
                else if (_Mode < 3.5)
                {
                    float2 q = abs(p);
                    float e = max(q.x, q.y);
                    a = smoothstep(1.0 - _Param.y, 1.0 - _Param.y * 0.6, e);                     // frame
                }
                else if (_Mode < 4.5)
                {
                    // radial progress: x radius, y width, z progress 0..1 (starts at 12 o'clock, clockwise)
                    float w = max(_Param.y, 0.01);
                    float d = abs(r - _Param.x);
                    float ringA = 1.0 - smoothstep(w * 0.5, w, d);
                    float turn = frac(0.25 - ang);                                               // 0 at top, clockwise
                    float on = step(turn, _Param.z);
                    col = lerp(_Color.rgb, _Color2.rgb, turn);
                    a = ringA * lerp(0.16, 1.0, on);
                    col = lerp(float3(1, 1, 1) * 0.6, col, on);
                }
                else if (_Mode < 5.5)
                {
                    // four-point sparkle: x sharpness
                    float2 q = abs(p);
                    float star = saturate(1.0 - (pow(q.x, 0.55) + pow(q.y, 0.55)));
                    a = pow(star, max(_Param.x, 0.2)) * 2.2 + exp(-r * r * 9.0) * 0.6;
                    col = lerp(_Color2.rgb, float3(1, 1, 1), saturate(a - 0.6));
                    a = saturate(a) * step(r, 1.0);
                }
                else if (_Mode < 6.5)
                {
                    // ripple: z = age 0..1, expands and fades
                    float age = saturate(_Param.z);
                    float rad = lerp(_Param.x, 0.95, 1.0 - (1.0 - age) * (1.0 - age));   // starts on the ring, never inside the opening
                    float w = lerp(0.07, 0.02, age);
                    float d = abs(r - rad);
                    a = (1.0 - smoothstep(w * 0.4, w, d)) * (1.0 - age) * (1.0 - age);
                    col = hueMix(ang + age);
                    a *= step(r, 1.0);
                }
                else if (_Mode < 7.5)
                {
                    // dashed rotating ring: x radius, y width, z dash count, w duty
                    float w = max(_Param.y, 0.01);
                    float d = abs(r - _Param.x);
                    float dash = step(frac((ang + _Phase) * _Param.z), _Param.w);
                    a = (1.0 - smoothstep(w * 0.45, w, d)) * dash;
                    col = hueMix(ang * 2.0 - _Phase);
                    a *= step(r, 1.0);
                }
                else if (_Mode < 8.5)
                {
                    a = exp(-r * r * max(_Param.x, 0.5)) * step(r, 1.0);                         // soft glow blob
                    col = lerp(_Color2.rgb, _Color.rgb, saturate(1.0 - r));
                }
                else if (_Mode < 9.5)
                {
                    // rain streak as the eye sees falling rain: a thin soft line, brightest just above the head, fading up the tail
                    float y = i.uv.y;                                                            // 0 = head (bottom), 1 = tail (top)
                    float along = smoothstep(0.0, 0.07, y) * pow(saturate(1.0 - y), 1.7);
                    float body = exp(-p.x * p.x * 5.0) * along;
                    float core = exp(-p.x * p.x * 26.0) * smoothstep(0.01, 0.14, y) * pow(saturate(1.0 - y), 3.2);     // the bright refracted line
                    float al = saturate(body * 0.50 + core * 0.75) * _Color.a * i.color.a;
                    float3 c = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), saturate(core * 1.1));
                    return half4(c * al, al);
                }
                else if (_Mode < 10.5)
                {
                    // splash where a drop lands: a fine ripple ring that spreads and fades, a fainter second ring, and a few
                    // tiny droplets thrown outwards. x = number of droplets, z = age 0..1, w = seed
                    float age = saturate(_Param.z);
                    float grow = 1.0 - (1.0 - age) * (1.0 - age);
                    float fade = (1.0 - age) * (1.0 - age);
                    float R = lerp(0.10, 0.78, grow);
                    float w = lerp(0.060, 0.022, age);
                    float dr = (r - R) / w; float ring = exp(-dr * dr) * fade;
                    float R2 = lerp(0.04, 0.46, saturate(grow * 1.25 - 0.25));
                    float dr2 = (r - R2) / (w * 0.8); float ring2 = exp(-dr2 * dr2) * fade * step(0.2, age);
                    float lit = 0.5 + 0.5 * cos((ang - 0.14) * 6.2831853);                       // light comes from the upper left
                    float n = max(floor(_Param.x + 0.5), 3.0);
                    float a01 = ang + 0.5 + _Param.w;
                    float k = fmod(floor(a01 * n) + 64.0 * n, n);
                    float h = frac(sin(k * 12.9898 + _Param.w * 78.233) * 43758.5453);
                    float da = (frac(a01 * n) - 0.5) / n * 6.2831853;
                    float Rd = R + 0.05 + (0.06 + 0.12 * h) * grow;
                    float rd = 0.030 * (0.5 + 0.5 * h);
                    float dd = length(float2(r * cos(da) - Rd, r * sin(da)));
                    float db = dd / rd; float bead = exp(-db * db) * (1.0 - age) * step(0.35, h);
                    float al = saturate(ring * (0.30 + 0.55 * lit) + ring2 * 0.28 + bead * 0.85) * _Color.a * i.color.a * step(r, 1.0);
                    float3 c = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), saturate(ring * lit * 0.8 + bead * 0.7));
                    return half4(c * al, al);
                }
                else if (_Mode < 11.5)
                {
                    // thin sheet of water lying on a surface: almost clear, a slightly brighter rim where it stands up
                    // (meniscus), faint ripples moving outwards and one soft highlight. z = wetness 0..1
                    float wet = saturate(_Param.z);
                    float wob = 0.045 * sin(ang * 18.849556 + _Phase * 1.3) + 0.030 * sin(ang * 31.415927 - _Phase * 1.9);
                    float R = lerp(0.30, 0.86, wet) * (1.0 + wob);
                    float inside = 1.0 - smoothstep(R - 0.05, R, r);
                    float rim = smoothstep(R - 0.20, R - 0.05, r) * inside;
                    float ripple = 0.5 + 0.5 * sin((r * 6.0 - _Phase * 1.6) * 6.2831853);
                    float2 hp = (p - float2(-0.26, 0.30) * (R / 0.86)) * float2(1.0, 2.0);
                    float hl = exp(-dot(hp, hp) * 26.0) * inside;
                    float al = saturate(inside * (0.10 + 0.07 * ripple) + rim * 0.20 + hl * 0.55) * _Color.a * i.color.a;
                    float3 c = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), saturate(rim * 0.45 + hl + ripple * 0.10));
                    return half4(c * al, al);
                }
                else if (_Mode > 12.5)
                {
                    // A white-hot core, four tapered rays and a coloured corona. Drawn in one quad on both sides of the glass.
                    float ray = pow(max(0.0, 1.0 - min(abs(p.x), abs(p.y)) * 12.0), 3.0);
                    float corona = exp(-6.0 * r * r);
                    float rays = ray * pow(saturate(1.0 - r), 1.3);
                    float core = exp(-37.0 * r * r);
                    a = saturate(corona * 0.62 + rays * 0.95 + core) * (1.0 - smoothstep(0.84, 1.0, r));
                    col = lerp(_Color.rgb, _Color2.rgb, saturate(core + rays * 0.8));
                }
                else
                {
                    // a small flying droplet: clear in the middle, denser towards its edge, one bright point of light
                    float edge = 1.0 - smoothstep(0.62, 0.82, r);
                    float dens = 0.22 + 0.40 * smoothstep(0.25, 0.80, r);
                    float hl = exp(-dot(p - float2(-0.26, 0.28), p - float2(-0.26, 0.28)) * 26.0);
                    float al = saturate(edge * dens + hl * edge * 0.9) * _Color.a * i.color.a;
                    float3 c = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), saturate(hl * 1.2));
                    return half4(c * al, al);
                }
                float alpha = saturate(a * _Color.a * i.color.a);
                return half4(col * i.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
