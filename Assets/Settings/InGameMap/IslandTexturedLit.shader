Shader "MediaProject/Island Textured Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Palette", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.25
        _Metallic("Metallic", Range(0,1)) = 0
        _DetailAlbedoMap("Surface Detail (linear grayscale)", 2D) = "gray" {}
        _DetailMask("Cliff Detail (terrain only)", 2D) = "gray" {}
        _DetailAlbedoMapScale("Texture Strength", Range(0,2)) = 1
        _DetailNormalMapScale("Surface Relief (meters)", Range(0,0.3)) = 0.06
        [Toggle(_ISLAND_TERRAIN)] _IslandTerrain("Grass From Green Palette", Float) = 0
        [HideInInspector] _WorkflowMode("Workflow", Float) = 1
        [HideInInspector] _SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _BumpMap("Normal", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1
        [HideInInspector] _Parallax("Parallax", Float) = 0
        [HideInInspector] _OcclusionMap("Occlusion", 2D) = "white" {}
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Float) = 1
        [HideInInspector] _EmissionColor("Emission", Color) = (0,0,0,1)
        [HideInInspector] _EmissionMap("Emission Map", 2D) = "white" {}
        [HideInInspector] _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        [HideInInspector] _SpecGlossMap("Specular Map", 2D) = "white" {}
        [HideInInspector] _DetailNormalMap("Detail Normal", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("Clear Coat", Float) = 0
        [HideInInspector] _ClearCoatSmoothness("Clear Coat Smoothness", Float) = 0
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _SrcBlend("Src Blend", Float) = 1
        [HideInInspector] _DstBlend("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _AlphaToMask("Alpha To Mask", Float) = 0
        [HideInInspector] _BlendModePreserveSpecular("Preserve Specular", Float) = 1
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _SmoothnessTextureChannel("Smoothness Channel", Float) = 0
        [HideInInspector] _ReceiveShadows("Receive Shadows", Float) = 1
        [HideInInspector] _SpecularHighlights("Specular Highlights", Float) = 1
        [HideInInspector] _EnvironmentReflections("Environment Reflections", Float) = 1
        [HideInInspector] _QueueOffset("Queue Offset", Float) = 0
        [HideInInspector][NoScaleOffset] unity_Lightmaps("Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("Lightmaps Ind", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("Shadow Masks", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment IslandFragment
            #pragma shader_feature_local _ISLAND_TERRAIN
            #pragma shader_feature_local _ _ISLAND_WOOD_X _ISLAND_WOOD_Z
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            // Keep the native Lit constant buffer layout so all passes use the same SRP batcher layout.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            half SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 p, half3 w)
            {
                return SAMPLE_TEXTURE2D(tex, samp, p.zy).r * w.x
                     + SAMPLE_TEXTURE2D(tex, samp, p.xz).r * w.y
                     + SAMPLE_TEXTURE2D(tex, samp, p.xy).r * w.z;
            }

            half4 IslandFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv, surface);
                InputData lighting;
                InitializeInputData(input, surface.normalTS, lighting);

                // Object-relative projection follows moving props; scale keeps texture density in world meters.
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 scale = float3(length(objectToWorld._m00_m10_m20),
                                      length(objectToWorld._m01_m11_m21),
                                      length(objectToWorld._m02_m12_m22));
                float3 p = TransformWorldToObject(input.positionWS) * scale;
                half3 localNormal = normalize(mul(transpose((float3x3)objectToWorld), input.normalWS));
                half3 weights = pow(abs(localNormal), 4);
                weights /= max(dot(weights, half3(1,1,1)), 0.0001);
                #if defined(_ISLAND_WOOD_X)
                    p = p.yxz; weights = weights.yxz;
                #elif defined(_ISLAND_WOOD_Z)
                    p = p.xzy; weights = weights.xzy;
                #endif
                float3 detailPosition = p * _DetailAlbedoMap_ST.x;
                half detail = SampleTriplanar(TEXTURE2D_ARGS(_DetailAlbedoMap, sampler_DetailAlbedoMap), detailPosition, weights);
                #if defined(_ISLAND_TERRAIN)
                    half cliff = SampleTriplanar(TEXTURE2D_ARGS(_DetailMask, sampler_DetailMask), detailPosition * 0.72, weights);
                    half grass = smoothstep(0.025h, 0.085h, surface.albedo.g - max(surface.albedo.r, surface.albedo.b));
                    detail = lerp(cliff, detail, grass);
                #endif
                half variation = (detail - 0.5h) * _DetailAlbedoMapScale;
                surface.albedo *= clamp(1 + variation * 2, 0.55h, 1.45h);
                surface.smoothness = saturate(surface.smoothness - variation * 0.2h);

                // Height derivatives add shallow relief without changing the mesh or colliders.
                float3 dpdx = ddx(input.positionWS);
                float3 dpdy = ddy(input.positionWS);
                float3 r1 = cross(dpdy, lighting.normalWS);
                float3 r2 = cross(lighting.normalWS, dpdx);
                float determinant = dot(dpdx, r1);
                float3 gradient = sign(determinant) * (ddx(detail) * r1 + ddy(detail) * r2);
                lighting.normalWS = normalize(max(abs(determinant), 0.000001) * lighting.normalWS - gradient * _DetailNormalMapScale);
                InitializeBakedGIData(input, lighting);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

