// The things the partner owns (a desk, a chair, a record player ...): toy-like, in its own look. Two soft steps of
// light, and the same dark-brown ink line round the shape that the partner itself has. Works for the built-in shapes
// (vertex colours, white texture) and for generated models (their texture). _Ghost fades a thing that is still being
// made into a pale, see-through shape with a rising band of light.
Shader "LivingPartner/Prop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Ink ("Ink", Color) = (0.29, 0.18, 0.15, 1)
        _Outline ("Outline width (object units)", Float) = 0.012
        _Ghost ("Ghost", Range(0, 1)) = 0
        _Fill ("Made so far", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Color;
        float4 _Ink;
        float _Outline;
        float _Ghost;
        float _Fill;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; float3 positionOS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
        ENDHLSL

        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz + normalize(v.normalOS) * _Outline;
                o.positionCS = TransformObjectToHClip(p);
                o.uv = v.uv; o.normalWS = 0; o.color = v.color; o.positionOS = v.positionOS.xyz;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(_Ink.rgb, (1.0 - 0.75 * _Ghost) * _Color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.color = v.color; o.positionOS = v.positionOS.xyz;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 albedo = tex.rgb * i.color.rgb * _Color.rgb;
                float3 n = normalize(i.normalWS);
                float nl = dot(n, normalize(float3(-0.35, 0.80, -0.45))) * 0.5 + 0.5;
                // two soft steps: lit, and a warm shade (never black - it is a toy in a picture book)
                float lit = smoothstep(0.42, 0.50, nl) * 0.5 + smoothstep(0.70, 0.78, nl) * 0.5;
                float3 col = albedo * lerp(float3(0.62, 0.54, 0.56), float3(1.04, 1.02, 0.98), lit);
                // still being made: pale, and a band of light climbs it as far as it is done
                float band = exp(-pow((i.positionOS.y - _Fill) / 0.06, 2.0));
                col = lerp(col, float3(1.0, 0.95, 0.80), _Ghost * (0.55 + 0.45 * band));
                float a = lerp(1.0, 0.45 + 0.5 * band, _Ghost) * _Color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
