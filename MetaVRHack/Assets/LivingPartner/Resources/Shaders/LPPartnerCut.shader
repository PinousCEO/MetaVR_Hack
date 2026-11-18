// The cut face of the partner while it passes through the screen: one flat quad lying on the display plane, drawn only
// where the plane is inside the body. The partner shader's "CutMark" pass counts the body's surfaces behind every pixel
// (+1 for a surface seen from inside, -1 for one seen from outside), so the count is not zero exactly where the line of
// sight enters the body through the cut. Drawn after the partner; it puts the count back to zero.
Shader "LivingPartner/PartnerCut"
{
    Properties
    {
        _CapColor ("Cut Face Color", Color) = (1, 0.86, 0.60, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+30" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "CutFace"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Stencil { Ref 0 Comp NotEqual Pass Zero }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _CapColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(_CapColor.rgb, 1); }
            ENDHLSL
        }
    }
}
