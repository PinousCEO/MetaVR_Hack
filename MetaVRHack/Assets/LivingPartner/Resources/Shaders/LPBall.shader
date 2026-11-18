// The ball of the tennis game (Quest + Windows): a small toon sphere in the same picture-book look as the partner -
// two flat tones, a drawn seam and an ink outline - with the same clip plane, so the PC draws the part of the ball
// that is behind the glass and the headset the part in front of it.
// The mesh is a sphere of diameter 1 around the origin; the object's scale is the ball's diameter in metres.
Shader "LivingPartner/Ball"
{
    Properties
    {
        _Football ("Football", Float) = 0
        _Color ("Color", Color) = (0.87, 0.91, 0.47, 1)
        _ShadeTint ("Shade Tint", Color) = (0.80, 0.74, 0.62, 1)
        _SeamColor ("Seam Color", Color) = (1.0, 0.98, 0.92, 1)
        _OutlineColor ("Outline Color", Color) = (0.20, 0.13, 0.16, 1)
        _OutlineWidth ("Outline Width (metres)", Float) = 0.0011
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float4 _ShadeTint;
        float4 _SeamColor;
        float4 _OutlineColor;
        float _OutlineWidth;
        float4 _ClipPlane;   // xyz = plane normal (world), w = -dot(n, pointOnPlane)
        float _Football;
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 pw = o.positionWS + normalize(TransformObjectToWorldNormal(v.normalOS)) * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(pw);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_ClipSign != 0) clip(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w));
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

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_ClipSign != 0) clip(_ClipSign * (dot(_ClipPlane.xyz, i.positionWS) + _ClipPlane.w));
                bool front = IS_FRONT_VFACE(face, true, false);
                // the inside of the ball where the glass cuts it: one flat colour, like the partner's cut face
                if (!front) return half4(_SeamColor.rgb, 1);
                float3 n = normalize(i.normalWS);
                float3 l = dot(_LightDirWS.xyz, _LightDirWS.xyz) < 1e-6 ? normalize(float3(-0.35, 0.8, -0.5)) : normalize(_LightDirWS.xyz);
                float lit = smoothstep(0.40, 0.44, dot(n, l) * 0.5 + 0.5);
                float3 col = lerp(_Color.rgb * _ShadeTint.rgb, _Color.rgb, lit);
                // the seam of a tennis ball: one curve that winds round it (it also shows the ball turning)
                float3 o = normalize(i.normalOS);
                if (_Football > 0.5)
                {
                    // Faces around each icosahedron vertex form dark pentagons. The next-nearest
                    // vertex cuts a fine seam between the white panels, which rotates with the mesh.
                    float3 a = abs(o);
                    float3 v0 = float3(0.0, 0.525731, 0.850651);
                    float3 ranks = float3(dot(a,v0), dot(a,v0.yzx), dot(a,v0.zxy));
                    float nearest = max(ranks.x,max(ranks.y,ranks.z));
                    float second = max(min(ranks.x,ranks.y),min(max(ranks.x,ranks.y),ranks.z));
                    float gap = nearest - second;
                    float patch = smoothstep(0.104,0.117,gap) * smoothstep(0.90,0.925,nearest);
                    float seamLine = (1.0 - smoothstep(0.002,0.014,gap)) * 0.28;
                    float3 leather = lerp(float3(0.96,0.94,0.87),float3(0.028,0.045,0.075),max(patch,seamLine));
                    return half4(leather * lerp(_ShadeTint.rgb,1.0,lit),1);
                }
                float d = abs(o.y - 0.52 * sin(2.0 * atan2(o.z, o.x)));
                float seam = 1.0 - smoothstep(0.055, 0.085, d);
                col = lerp(col, _SeamColor.rgb * lerp(0.86, 1.0, lit), seam);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
