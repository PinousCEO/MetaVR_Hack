// Shared partner shader (Quest + Windows): the model's own albedo texture under flat toon shading with a soft two-tone
// ramp, a crisp ink outline (inverted hull), and the portal clip plane - each renderer keeps only its side of the
// display surface, and where the body is cut it shows a solid cut face instead of a hollow shell. The light direction
// is set by the app in a shared frame so both renderers shade the character the same way.
// uv = albedo, uv2.x = status-light mask (1 on the part that takes the status colour), uv2.y = 1 on surfaces that are
// enclosed by the model and never seen from outside (part of the cut face while the body is cut); vertex colour = rgb
// shown instead of the texture, by the amount a (1 where the model's texture carries no colour).
Shader "LivingPartner/Partner"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _GlowColor ("Status Light Color", Color) = (0.3, 0.9, 1, 1)
        _GlowAmount ("Status Light Amount", Range(0, 1)) = 0
        _CapColor ("Cap Color", Color) = (0.55, 0.95, 1, 1)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _ShadeTint ("Shade Tint", Color) = (0.78, 0.76, 0.93, 1)
        _OutlineColor ("Outline Color", Color) = (0.13, 0.12, 0.22, 1)
        _OutlineWidth ("Outline Width (object units)", Float) = 0.011
        _Flat ("Flat (no shading)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _GlowColor;
        float _GlowAmount;
        float4 _CapColor;
        float4 _Tint;
        float4 _ShadeTint;
        float4 _OutlineColor;
        float _OutlineWidth;
        float _Flat;
        float4 _ClipPlane;   // xyz = plane normal (world), w = -dot(n, pointOnPlane)
        float _ClipSign;     // 0 = no clipping, +1 keep positive side, -1 keep negative side
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float4 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };   // where the surface is (not the set-back line); w = enclosed
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // no ink line on the surfaces the model keeps out of sight: where they fold over each other it would show as a black strip
                float3 p = v.positionOS.xyz + normalize(v.normalOS) * _OutlineWidth * (1.0 - saturate(v.color.a));
                float3 pw = TransformObjectToWorld(p);
                // the screen cuts the line where it cuts the surface, so a part that has not reached the glass leaves no line behind it
                o.positionWS = float4(TransformObjectToWorld(v.positionOS.xyz), v.uv2.y);
                // the line belongs behind the surface it outlines: set back a little along the line of sight, so that it
                // only shows past an edge and not where two parts of the body touch or sink into each other
                pw += normalize(pw - GetCameraPositionWS()) * _OutlineWidth * 2.5;
                o.positionCS = TransformWorldToHClip(pw);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_OutlineWidth - 0.00001);
                // while the body is cut, the enclosed parts are part of the cut face: no ink lines inside it
                if (_ClipSign != 0) clip(min(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS.xyz) + _ClipPlane.w), 0.5 - i.positionWS.w));
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float4 uv : TEXCOORD2;      // xy = albedo uv, z = status-light mask, w = enclosed
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
                o.color = v.color;
                o.uv = float4(v.uv, v.uv2.x, v.uv2.y);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float side = dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w;
                if (_ClipSign != 0) clip(_ClipSign * side);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                bool front = IS_FRONT_VFACE(face, true, false);
                // the inside of the cut body, where the cut-face quad (PartnerCut) is not drawn: the same flat colour
                if (_ClipSign != 0 && (!front || i.uv.w > 0.5)) return half4(_CapColor.rgb, 1);
                float3 base = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb, i.color.rgb, saturate(i.color.a)) * _Tint.rgb;
                // not being cut: the inside of an open part of the model (a cuff, the rim of the bonnet) is drawn as ordinary surface
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                // no light set (edit mode, e.g. the character gallery): the same default the apps start with
                float3 l = dot(_LightDirWS.xyz, _LightDirWS.xyz) < 1e-6 ? normalize(float3(-0.35, 0.8, -0.5)) : normalize(_LightDirWS.xyz);
                float ndl = dot(n, l) * 0.5 + 0.5;
                // two flat tones with a narrow soft edge, plus a small bright band on the lit side (cel look)
                float lit = smoothstep(0.415, 0.445, ndl);
                float3 col = lerp(base * _ShadeTint.rgb, base, lit);
                col = lerp(col, base, _Flat);
                col = lerp(col, _GlowColor.rgb * 1.15, i.uv.z * _GlowAmount);
                // thin glow line where the body meets the portal plane (boundary emphasis only)
                if (_ClipSign != 0) col += _CapColor.rgb * (1.0 - smoothstep(0.0, 0.004, abs(side))) * 0.8;
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Counts the body's surfaces behind each pixel while the body is cut (see PartnerCut.shader).
        Pass
        {
            Name "CutMark"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0
            Stencil { Ref 0 Comp Always PassFront DecrWrap PassBack IncrWrap }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                // not being cut: nothing to count, so the triangles are thrown away before they are rasterised
                o.positionCS = _ClipSign != 0 ? TransformWorldToHClip(o.positionWS) : float4(0, 0, 0, 0);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                if (_ClipSign != 0) clip(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w));
                return 0;
            }
            ENDHLSL
        }
    }
}
