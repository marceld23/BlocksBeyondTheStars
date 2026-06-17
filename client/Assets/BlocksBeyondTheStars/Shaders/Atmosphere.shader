// Additive atmospheric scattering glow drawn on a dome behind the world on planets with air. It does NOT
// replace the camera's sky colour (Sky.cs still clears to the day/night sky) — it ADDS a horizon brightening
// band + a soft sun-scattering halo (Mie) that warms toward orange when the sun is low, so dawn/dusk glow and
// the horizon read like a real atmosphere instead of a flat fill. Additive + ZWrite Off in the Background queue,
// so opaque geometry paints over it and it can only ever brighten the open sky (never black it out). Reads the
// same sky globals Sky.cs sets: _Sc_SunDir (dir TO the sun), _Sc_Sky (sky colour), _Sc_Light (sun colour ×
// brightness; dark at night → the glow self-fades). Dual-pipeline (URP + Built-in RP).
Shader "BlocksBeyondTheStars/Atmosphere"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend One One // additive — only ever brightens the sky

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Brightness;
            float4 _Sc_SunDir; // world-space direction TO the sun
            float4 _Sc_Sky;    // current sky colour
            float4 _Sc_Light;  // sun colour × day brightness (a>0.5 = set)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = normalize(v.positionOS.xyz); // dome is camera-centred → object dir ≈ view dir
                return o;
            }

            half3 Scatter(float3 d)
            {
                float3 sun = normalize(_Sc_SunDir.xyz);
                float3 sky = _Sc_Sky.rgb;
                float3 sunCol = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float up = saturate(d.y);
                float horizon = pow(1.0 - up, 3.0);            // bright at the horizon, fades up to the zenith
                float sd = saturate(dot(d, sun));
                float mie = pow(sd, 8.0) * 0.5 + pow(sd, 64.0) * 1.2; // broad halo + tight near-sun glow
                float sunLow = saturate(1.0 - abs(sun.y) * 2.0);      // 1 when the sun sits near the horizon
                float3 warm = lerp(float3(1, 1, 1), float3(1.0, 0.55, 0.25), sunLow); // sunset warming

                float3 glow = sunCol * warm * mie;
                float3 horizonCol = sky * horizon * 0.5;
                // A warm horizon band along the sun's azimuth at dawn/dusk.
                float azim = saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-5), normalize(float3(sun.x, 0, sun.z) + 1e-5)));
                horizonCol += sunCol * warm * horizon * pow(azim, 3.0) * sunLow * 1.1;
                return glow + horizonCol;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(Scatter(normalize(i.dir)) * _Brightness, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback) ----------------
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Brightness;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Light;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_Sc_SunDir.xyz);
                float3 sky = _Sc_Sky.rgb;
                float3 sunCol = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float up = saturate(d.y);
                float horizon = pow(1.0 - up, 3.0);
                float sd = saturate(dot(d, sun));
                float mie = pow(sd, 8.0) * 0.5 + pow(sd, 64.0) * 1.2;
                float sunLow = saturate(1.0 - abs(sun.y) * 2.0);
                float3 warm = lerp(float3(1, 1, 1), float3(1.0, 0.55, 0.25), sunLow);

                float3 glow = sunCol * warm * mie;
                float3 horizonCol = sky * horizon * 0.5;
                float azim = saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-5), normalize(float3(sun.x, 0, sun.z) + 1e-5)));
                horizonCol += sunCol * warm * horizon * pow(azim, 3.0) * sunLow * 1.1;
                return fixed4((glow + horizonCol) * _Brightness, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
