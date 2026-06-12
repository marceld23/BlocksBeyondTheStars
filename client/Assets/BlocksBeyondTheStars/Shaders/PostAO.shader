// Screen-space ambient occlusion for the custom post-FX stack (PostFx.cs). Reconstructs view-space
// position + normal from the camera's depth-normals buffer and samples a small hemisphere kernel to
// darken creases (voxel crevices, contact shadows). Outputs the occlusion factor (1 = lit) in red.
// Conservative: bad reconstruction degrades toward "no occlusion", never to black. Built-in RP;
// always-included. The camera must run with DepthTextureMode.DepthNormals (PostFx sets this).
Shader "BlocksBeyondTheStars/PostAO"
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

            sampler2D _CameraDepthNormalsTexture;
            float _AoRadius;
            float _AoIntensity;

            static const float2 K[8] =
            {
                float2( 1.0,  0.0), float2(-1.0,  0.0), float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 0.7,  0.7), float2(-0.7,  0.7), float2( 0.7, -0.7), float2(-0.7, -0.7)
            };

            void SampleDN(float2 uv, out float3 vpos, out float3 vnorm)
            {
                float depth;
                float3 n;
                DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), depth, n);
                float eyeZ = depth * _ProjectionParams.z; // linear01 * far = eye depth
                float2 ndc = uv * 2.0 - 1.0;
                vpos = float3(ndc.x / unity_CameraProjection._m00, ndc.y / unity_CameraProjection._m11, -1.0) * eyeZ;
                vnorm = normalize(n);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 P, N;
                SampleDN(i.uv, P, N);

                float occ = 0.0;
                [unroll]
                for (int s = 0; s < 8; s++)
                {
                    float2 off = K[s] * _AoRadius * 0.02;
                    float3 Q, Nq;
                    SampleDN(i.uv + off, Q, Nq);

                    float3 diff = Q - P;
                    float dist = length(diff);
                    float3 dir = diff / max(dist, 1e-4);
                    float atten = 1.0 / (1.0 + dist * dist);
                    occ += max(0.0, dot(N, dir)) * atten;
                }

                float ao = saturate(1.0 - occ * _AoIntensity / 8.0);
                return fixed4(ao, ao, ao, 1.0);
            }
            ENDCG
        }
    }
}
