// Minimal unlit, opaque shader that renders the mesh vertex colours (built-in render
// pipeline). The chunk mesher bakes per-block colour + per-face shading into the vertex
// colours, so no scene lighting or textures are needed for the blocky look (M21).
Shader "Spacecraft/VertexColorOpaque"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Sc_Light; // global day/night × sun-colour × weather tint (alpha>0.5 = set)

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 l = _Sc_Light;
                if (l.a < 0.5) l = fixed4(1, 1, 1, 1); // default to no tint until set
                return fixed4(i.color.rgb * l.rgb, i.color.a);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
