// Final composite for the custom post-FX stack (PostFx.cs): multiplies in ambient occlusion, adds the
// blurred bloom, applies exposure + ACES filmic tonemapping, and a soft vignette. Built-in RP;
// always-included.
Shader "Spacecraft/PostComposite"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            sampler2D _AoTex;
            float _BloomIntensity;
            float _Exposure;
            float _Tonemap;
            float _Vignette;

            // Narkowicz ACES filmic approximation.
            float3 ACES(float3 x)
            {
                const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 col = tex2D(_MainTex, i.uv).rgb;

                float ao = tex2D(_AoTex, i.uv).r; // 1 = unoccluded
                col *= ao;

                col += tex2D(_BloomTex, i.uv).rgb * _BloomIntensity;
                col *= _Exposure;

                if (_Tonemap > 0.5)
                {
                    col = ACES(col);
                }

                float2 dv = i.uv - 0.5;
                col *= 1.0 - _Vignette * saturate(dot(dv, dv) * 2.0);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
