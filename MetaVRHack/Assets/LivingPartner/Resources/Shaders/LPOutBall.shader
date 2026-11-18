// What a glass game sends OUT of the screen at the user (MrGlassOut): a real toon ball in the picture-book look of
// the tennis ball - two flat tones, a small glint, an ink outline - so that it reads as a round thing in the room and
// not as a card. Three looks, chosen per ball (_Look):
//   0  a toy football: the ball's colour with round ink patches (the twelve corners of an icosahedron)
//   1  a ball with the game's own picture on it: the sticker is laid on four sides of the ball (it turns with it)
//   2  a plain ball with one band round it (shows it turning)
// The mesh is a sphere of diameter 1 around the origin; the object's scale is the ball's diameter in metres.
// Everything a ball needs is given per renderer (MaterialPropertyBlock): GlassOutBallView.
Shader "LivingPartner/OutBall"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Color ("Color", Color) = (1.0, 0.97, 0.90, 1)
        _ShadeTint ("Shade Tint", Color) = (0.80, 0.74, 0.70, 1)
        _PatchColor ("Patch Color", Color) = (0.29, 0.18, 0.15, 1)
        _OutlineColor ("Outline Color", Color) = (0.20, 0.13, 0.16, 1)
        _OutlineWidth ("Outline Width (share of the diameter)", Float) = 0.022
        _Look ("Look (0 football, 1 picture, 2 band)", Float) = 0
        _Flash ("Flash (0..1 towards white)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Color;
        float4 _ShadeTint;
        float4 _PatchColor;
        float4 _OutlineColor;
        float _OutlineWidth;
        float _Look;
        float _Flash;
        float4 _LightDirWS;  // direction TO the light, world space
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // (in the ball's own measure: the outline is a share of its diameter, whatever its size and squash)
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz + normalize(v.normalOS) * _OutlineWidth);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(lerp(_OutlineColor.rgb, float3(1, 1, 1), _Flash * 0.6), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float3 o = normalize(i.normalOS);
                float3 l = dot(_LightDirWS.xyz, _LightDirWS.xyz) < 1e-6 ? normalize(float3(-0.35, 0.8, -0.5)) : normalize(_LightDirWS.xyz);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndl = dot(n, l);
                float lit = smoothstep(0.42, 0.47, ndl * 0.5 + 0.5);

                float3 base = _Color.rgb;
                if (_Look < 0.5)
                {
                    // a toy football: round ink patches at the twelve corners of an icosahedron (0, +-1, +-phi) and its turns
                    float3 a = abs(o);
                    const float3 c0 = float3(0.0, 0.525731, 0.850651);
                    float d = max(dot(a, c0), max(dot(a, c0.yzx), dot(a, c0.zxy)));
                    float patch = smoothstep(0.950, 0.958, d);
                    base = lerp(base, _PatchColor.rgb, patch);
                    // a fine seam ring round every patch
                    float ring = smoothstep(0.900, 0.906, d) * (1.0 - smoothstep(0.912, 0.918, d));
                    base = lerp(base, lerp(base, _PatchColor.rgb, 0.40), ring);
                }
                else if (_Look < 1.5)
                {
                    // the game's picture, laid on the four sides of the ball (front, back, left, right)
                    float3 a = abs(o);
                    float2 uv; float w;
                    if (a.z >= a.x) { uv = float2(o.z < 0 ? o.x : -o.x, o.y); w = a.z; }
                    else            { uv = float2(o.x < 0 ? -o.z : o.z, o.y); w = a.x; }
                    uv = uv * 0.60 + 0.5;
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                    float al = saturate(t.a) * inside * smoothstep(0.705, 0.74, w);
                    base = lerp(base, t.rgb, al);
                }
                else
                {
                    // a plain ball with one band round it
                    float band = 1.0 - smoothstep(0.10, 0.13, abs(o.y - 0.30 * sin(2.0 * atan2(o.z, o.x))));
                    base = lerp(base, _PatchColor.rgb, band * _PatchColor.a);
                }

                float3 col = lerp(base * _ShadeTint.rgb, base, lit);
                // a small glint where the light sits, and a soft light rim: it is a round, shiny thing
                float glint = smoothstep(0.965, 0.980, dot(n, normalize(l + v)));
                col = lerp(col, float3(1, 1, 1), glint * 0.75);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * saturate(ndl * 0.5 + 0.8);
                col += rim * 0.10;
                col = lerp(col, float3(1.0, 0.98, 0.90), saturate(_Flash));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
