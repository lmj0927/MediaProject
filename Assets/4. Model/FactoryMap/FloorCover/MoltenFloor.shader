// 쇳물 바닥. 월드 좌표로 무늬를 직접 계산해서 텍스처 반복이 없고, 시간에 따라 무늬가 일렁이며 모양이 바뀜.
// 조명을 받지 않고 스스로 빛나는 색만 냄. 식은 껍질(검은 부분)도 같은 무늬에서 나옴.
Shader "RollWithUs/MoltenFloor"
{
    Properties
    {
        [HDR] _HotColor ("Hot Color", Color) = (2.6, 1.9, 0.5, 1)
        [HDR] _MidColor ("Mid Color", Color) = (2.2, 0.55, 0.05, 1)
        [HDR] _DeepColor ("Deep Color", Color) = (0.9, 0.07, 0.01, 1)
        _CrustColor ("Crust Color", Color) = (0.035, 0.025, 0.025, 1)
        _Scale ("Pattern Size (m)", Float) = 55
        _CrustAmount ("Crust Amount", Range(0, 1)) = 0.36
        _CrustEdge ("Crust Edge Softness", Range(0.001, 0.2)) = 0.02
        _FlowSpeed ("Flow Speed", Float) = 0.035
        _MorphSpeed ("Morph Speed", Float) = 0.06
        _Warp ("Warp Strength", Float) = 0.9
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _HotColor, _MidColor, _DeepColor, _CrustColor;
            float _Scale, _CrustAmount, _CrustEdge, _FlowSpeed, _MorphSpeed, _Warp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * valueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.7);
                    a *= 0.5;
                }
                return v / 0.9375;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.positionWS.xz / max(_Scale, 0.01);

                // 무늬 자리를 시간에 따라 서로 반대로 흐르는 두 잡음으로 밀어서, 흘러가면서 모양 자체가 바뀌게 함
                float2 warp = float2(fbm(p * 0.7 + float2(t * _MorphSpeed, 3.1)), fbm(p * 0.7 + float2(8.4, -t * _MorphSpeed)));
                float2 q = p + (warp - 0.5) * _Warp + float2(0.0, t * _FlowSpeed);
                float heat = fbm(q);
                // 아주 큰 얼룩: 구역마다 밝기와 껍질 양이 달라 보이게 함
                float region = fbm(p * 0.13 + float2(-t * _FlowSpeed * 0.2, 5.0));
                heat = saturate((heat - 0.5) * 1.9 + 0.5 + (region - 0.5) * 0.3);
                // 껍질 안쪽 잔금: 가는 잡음으로 껍질 가장자리를 거칠게 함
                heat += (valueNoise(q * 9.0) - 0.5) * 0.06;

                float crust = 1.0 - smoothstep(_CrustAmount - _CrustEdge, _CrustAmount + _CrustEdge, heat);
                float k = saturate((heat - _CrustAmount) / max(1.0 - _CrustAmount, 0.01));
                half3 glow = lerp(_DeepColor.rgb, _MidColor.rgb, saturate(k * 2.2));
                glow = lerp(glow, _HotColor.rgb, saturate(k * 2.2 - 1.0));
                // 껍질 가장자리는 달궈져서 붉게 비침
                half3 col = lerp(glow, _CrustColor.rgb, crust);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
