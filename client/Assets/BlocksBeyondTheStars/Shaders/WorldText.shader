// World-space text (#2531): the built-in "GUI/Text Shader" (the font atlas' alpha × vertex colour × _Color), but
// DEPTH-TESTED. The font's own material uses ZTest [unity_GUIZTestMode], which never consults the depth buffer, so a
// TextMesh label (a sleeping animal's "z z z", a companion's name) shone straight through terrain and gave away
// where caves end and land begins. Same look, ZTest LEqual: walls and hills now hide the text like any other object.
// No depth write (it is a transparent overlay) and no culling (the label is a billboard either way round).
// One shared material (WorldTextMaterial.cs) with the font texture as _MainTex. Always-included in the player build
// (GraphicsSettings + BuildScript.RuntimeShaders): it is only ever reached via Shader.Find.
// DUAL-PIPELINE: URP HLSL first, Built-in CG below.
Shader "BlocksBeyondTheStars/WorldText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1, 1, 1, 1)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = i.color;
                col.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a; // dynamic font atlas: glyph coverage in alpha
                return col;
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;
                col.a *= tex2D(_MainTex, i.uv).a;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
