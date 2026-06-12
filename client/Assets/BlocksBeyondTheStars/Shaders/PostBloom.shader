// Bloom helper passes for the custom post-FX stack (PostFx.cs). Pass 0 isolates the bright pixels
// (threshold + soft knee); pass 1 is a separable Gaussian blur whose direction C# sets per blit.
// Built-in render pipeline; always-included (Shader.Find at runtime).
Shader "BlocksBeyondTheStars/PostBloom"
{
    Properties { _MainTex ("Tex", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0 — bright-pass prefilter.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Threshold;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float br = max(c.r, max(c.g, c.b));
                float soft = max(0.0, br - _Threshold);
                float contrib = soft / max(br, 1e-4);
                return fixed4(c * contrib, 1.0);
            }
            ENDCG
        }

        // Pass 1 — separable Gaussian blur (5-tap, linear weights). _BlurDir = texel step on one axis.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _BlurDir;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 d = _BlurDir.xy;
                float3 sum = tex2D(_MainTex, i.uv).rgb * 0.227027;
                sum += tex2D(_MainTex, i.uv + d * 1.384615).rgb * 0.316216;
                sum += tex2D(_MainTex, i.uv - d * 1.384615).rgb * 0.316216;
                sum += tex2D(_MainTex, i.uv + d * 3.230769).rgb * 0.070270;
                sum += tex2D(_MainTex, i.uv - d * 3.230769).rgb * 0.070270;
                return fixed4(sum, 1.0);
            }
            ENDCG
        }
    }
}
