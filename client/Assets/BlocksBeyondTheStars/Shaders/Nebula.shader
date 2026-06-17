// Procedural nebula backdrop for deep space — wispy coloured gas clouds + faint distant galaxies drawn on a
// dome behind the starfield, so the void reads with depth and colour instead of flat black. Additive, in the
// Background queue with ZWrite Off, so opaque geometry (ships, asteroids, planets) paints over it and it only
// shows in open sky. Per-vertex COLOR carries the per-region nebula hue; _Brightness fades the whole field in
// (full in space / airless / station, off on lived-in planet skies). Dual-pipeline (URP + Built-in RP) to match
// the rest of the project's shaders; only the active pipeline's SubShader compiles.
Shader "BlocksBeyondTheStars/Nebula"
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
        Blend One One // additive — clouds glow over the dark sky

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Brightness;

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; float4 color : COLOR; };

            // Cheap value noise (no textures) → a few octaves of fbm for the cloud structure.
            float h13(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(h13(i + float3(0,0,0)), h13(i + float3(1,0,0)), f.x),
                                 lerp(h13(i + float3(0,1,0)), h13(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h13(i + float3(0,0,1)), h13(i + float3(1,0,1)), f.x),
                                 lerp(h13(i + float3(0,1,1)), h13(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { s += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = normalize(v.positionOS.xyz);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float n = saturate(fbm(d * 3.0 + 13.7) * 1.7 - 0.62); // sparse large clouds
                float detail = fbm(d * 8.0 + 71.3);
                float cloud = n * (0.55 + 0.45 * detail);
                cloud = cloud * cloud; // square → soft wispy falloff
                float3 col = i.color.rgb * cloud;
                return half4(col * _Brightness, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback; the game ships on URP) ----------------
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

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; fixed4 color : COLOR; };

            float h13(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(h13(i + float3(0,0,0)), h13(i + float3(1,0,0)), f.x),
                                 lerp(h13(i + float3(0,1,0)), h13(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h13(i + float3(0,0,1)), h13(i + float3(1,0,1)), f.x),
                                 lerp(h13(i + float3(0,1,1)), h13(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float n = saturate(fbm(d * 3.0 + 13.7) * 1.7 - 0.62);
                float detail = fbm(d * 8.0 + 71.3);
                float cloud = n * (0.55 + 0.45 * detail);
                cloud = cloud * cloud;
                fixed3 col = i.color.rgb * cloud;
                return fixed4(col * _Brightness, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
