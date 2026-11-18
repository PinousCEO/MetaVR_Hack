// A picture on a quad: the interface art that is drawn as a web page (tools/ui-art) and cut into textures - the tiles
// of the hand menu, its night-sky panel, the tab bar. Premultiplied output like LivingPartner/Fx, so the two layer
// together. _Round (w > 0.5) makes it a plain rounded rectangle instead of a picture. _Boost brightens it (a finger near a tile), _Shine (0..1) sends one soft band of light across it.
Shader "LivingPartner/Sprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Boost ("Boost", Float) = 0
        _Shine ("Shine", Float) = -1
        _Round ("Rounded rectangle: width, height, corner radius, on", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float _Boost;
            float _Shine;
            float4 _Round;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Round.w > 0.5)
                {
                    // GlassArt.Paper (the boxes of a scripted tool): no picture - a filled rounded rectangle _Round.x by _Round.y
                    // (the quad's own size, any unit) with the corner radius _Round.z, in _Color. The corners are a signed
                    // distance, so they stay round at any width and height; the edge is one pixel soft.
                    float2 q = abs(i.uv - 0.5) * _Round.xy - (_Round.xy * 0.5 - _Round.z);
                    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Round.z;
                    float pa = saturate(-d / max(fwidth(d), 1e-6)) * _Color.a;
                    return half4(saturate(_Color.rgb) * pa, pa);
                }
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                // one band of light, slanted, travelling from the lower left to the upper right while _Shine goes 0 -> 1
                float along = (i.uv.x + i.uv.y) * 0.5;
                float band = exp(-pow((along - (_Shine * 1.6 - 0.3)) / 0.07, 2.0)) * step(0.0, _Shine);
                c.rgb = c.rgb * (1.0 + 0.22 * _Boost) + (band * 0.42 + 0.10 * _Boost);
                return half4(saturate(c.rgb) * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
