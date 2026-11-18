// Converts the linear premultiplied camera target into sRGB-encoded premultiplied BGRA for UpdateLayeredWindow,
// so anti-aliased edges composite on the Windows desktop without bright/dark fringes.
Shader "Hidden/LivingPartner/Encode"
{
    Properties { _MainTex ("Tex", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _SrcIsLinear;
            float _FlipY;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                o.pos = TransformObjectToHClip(v.pos.xyz);
                o.uv = v.uv;
                if (_FlipY > 0.5) o.uv.y = 1.0 - o.uv.y;
                return o;
            }
            float4 frag(V i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float a = saturate(c.a);
                if (a < 1.0 / 510.0) return float4(0, 0, 0, 0);
                float3 straight = saturate(c.rgb / a);
                if (_SrcIsLinear > 0.5) straight = LinearToSRGB(straight);
                return float4(straight * a, a);
            }
            ENDHLSL
        }
    }
}
