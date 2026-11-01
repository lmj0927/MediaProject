Shader "Hidden/MediaProject/SceneOutline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "SceneOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _Strength;
            float _Thickness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _LineDarkness;
            float _FadeStart;
            float _FadeEnd;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // 화면 해상도 기준 픽셀 간격으로 이웃을 샘플링함
                float2 texel = _Thickness / _ScreenParams.xy;
                float2 offsets[4] = { float2(texel.x, 0), float2(-texel.x, 0), float2(0, texel.y), float2(0, -texel.y) };

                float d0 = EyeDepth(uv);
                float3 n0 = SampleSceneNormals(uv);
                float depthEdge = 0;
                float normalEdge = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float d = EyeDepth(uv + offsets[i]);
                    // 가까운 쪽 가장자리에만 선을 그려 선이 두 겹으로 생기지 않게 함
                    depthEdge = max(depthEdge, (d - d0) / max(d0, 0.001));
                    float3 n = SampleSceneNormals(uv + offsets[i]);
                    normalEdge = max(normalEdge, 1 - saturate(dot(n0, n)));
                }
                float edge = max(smoothstep(_DepthThreshold, _DepthThreshold * 2, depthEdge),
                                 smoothstep(_NormalThreshold, _NormalThreshold * 1.5, normalEdge));

                // 먼 곳은 선이 지저분해지므로 거리로 서서히 지움
                float fade = 1 - saturate((d0 - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                // 검은 선을 덮지 않고 원래 색을 어둡게 곱해 색상(H)을 유지함
                color.rgb *= lerp(1, _LineDarkness, saturate(edge * fade * _Strength));
                return color;
            }
            ENDHLSL
        }
    }
}
