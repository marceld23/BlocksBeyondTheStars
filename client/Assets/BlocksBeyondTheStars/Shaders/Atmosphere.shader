// Additive atmospheric scattering glow drawn on a dome behind the world on planets with air. It does NOT
// replace the camera's sky colour (Sky.cs still clears to the day/night sky) — it ADDS a horizon brightening
// band + a soft sun-scattering halo (Mie) that warms toward orange when the sun is low, so dawn/dusk glow and
// the horizon read like a real atmosphere instead of a flat fill. Additive + ZWrite Off at the end of the opaque
// queue (Geometry+499, #1513): drawn after the terrain so the depth test rejects covered pixels instead of shading
// the whole sky first; it can only ever brighten the open sky (never black it out), and additive-over-opaque is
// order-independent, so the picture is unchanged. The vertex shader pushes the dome to the far plane (#1582), so
// covered pixels are rejected at ANY distance, not only inside the dome's radius. Reads the
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
        Tags { "RenderType" = "Background" "Queue" = "Geometry+499" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend One One // additive — only ever brightens the sky

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher (#573): per-MATERIAL properties in UnityPerMaterial; the _Sc_* globals below stay out.
            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
            CBUFFER_END

            float4 _Sc_SunDir; // world-space direction TO the sun
            float4 _Sc_Sky;    // current sky colour
            float4 _Sc_Light;  // sun colour × day brightness (a>0.5 = set)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                // #1582: the dome draws at the END of the opaque queue with ZWrite Off (#1513), so its depth test must
                // reject exactly the pixels an opaque draw already covers — at ANY distance, not only nearer than the
                // dome's 0.45 × far radius (a moon beyond it got stars painted across it). Push the vertex to the far
                // plane (a hair inside it, so no driver clips it): the dome is at infinity, its radius stops mattering.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1.0e-6;         // reversed-Z: far plane = 0
                #else
                    o.positionCS.z = o.positionCS.w * (1.0 - 1.0e-6); // far plane = w
                #endif
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
        Tags { "RenderType" = "Background" "Queue" = "Geometry+499" }
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
                #if UNITY_REVERSED_Z
                    o.pos.z = o.pos.w * 1.0e-6;         // #1582: dome at the far plane (reversed-Z: far = 0)
                #else
                    o.pos.z = o.pos.w * (1.0 - 1.0e-6); // #1582: dome at the far plane (far = w)
                #endif
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
