Shader "LivingPartner/DemonFelt"
{
    Properties { _Weave ("Felt grain", 2D) = "gray" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Input { float4 p : POSITION; float3 n : NORMAL; half4 dye : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel { float4 h : SV_POSITION; float3 local : TEXCOORD0; half3 n : TEXCOORD1; half3 view : TEXCOORD2; half3 dye : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_Weave); SAMPLER(sampler_Weave);
            Pixel Vert(Input input)
            {
                Pixel o; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world=TransformObjectToWorld(input.p.xyz);
                o.h=TransformWorldToHClip(world); o.local=input.p.xyz;
                o.n=TransformObjectToWorldNormal(input.n); o.view=GetWorldSpaceNormalizeViewDir(world);
                o.dye=input.dye.rgb;
                return o;
            }
            half4 Frag(Pixel p) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(p);
                half3 n=normalize(p.n), view=normalize(p.view);
                half nl=dot(n, normalize(float3(-.45,.70,-.56)));
                half edge=1-saturate(dot(n,view));
                // Woven flecks from the hood illustration appear on curved surfaces and
                // recede into the mip chain at the far side of the room.
                float2 uv=p.local.xy * 3.4 + p.local.z*float2(1.75,.88);
                half grain=SAMPLE_TEXTURE2D(_Weave,sampler_Weave,uv).r;
                half3 paint=p.dye * lerp(half3(.70,.62,.67),half3(1.07,1.035,.99),smoothstep(-.68,.80,nl));
                paint *= 1+(grain-.5)*.14;
                paint+=half3(.055,.040,.025)*pow(edge,3)*saturate(n.y+.5);
                paint=lerp(paint,paint*half3(.63,.51,.61),smoothstep(.83,1,edge)*.46);
                #ifndef UNITY_COLORSPACE_GAMMA
                    paint=SRGBToLinear(paint);
                #endif
                return half4(paint,1);
            }
            ENDHLSL
        }
    }
}
