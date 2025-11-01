#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

void GetCubemap_float(float3 ViewDirWS, float3 PositionWS, float3 NormalWS, float Roughness, out float3 Cubemap)
{
    #ifdef SHADERGRAPH_PREVIEW
    Cubemap = float3(0, 0, 0);
    #else

    float3 normalizedViewDir = normalize(ViewDirWS);
    float3 normalizedNormal = normalize(NormalWS);
    
    float3 reflectionVector = reflect(-normalizedViewDir, normalizedNormal);
    
    Cubemap = GlossyEnvironmentReflection(
        reflectionVector,
        PositionWS,
        Roughness,
        1.0h,  
        float2(0, 0)
    );
    #endif
}

void GetCubemap_half(half3 ViewDirWS, half3 PositionWS, half3 NormalWS, half Roughness, out half3 Cubemap)
{
    #ifdef SHADERGRAPH_PREVIEW
    Cubemap = half3(0, 0, 0);
    #else
    half3 reflectionVector = reflect(-normalize(ViewDirWS), normalize(NormalWS));
    Cubemap = GlossyEnvironmentReflection(reflectionVector, PositionWS, Roughness, 1.0h, float2(0, 0));
    #endif
}