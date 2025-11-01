#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

//------------------------------------------------------------------------------------------------------
// Includes
//------------------------------------------------------------------------------------------------------

//------------------------------------------------------------------------------------------------------
// Keyword Pragmas
//------------------------------------------------------------------------------------------------------

#ifndef SHADERGRAPH_PREVIEW
	#if SHADERPASS != SHADERPASS_FORWARD && SHADERPASS != SHADERPASS_GBUFFER
		// #if to avoid "duplicate keyword" warnings if this is included in a Lit Graph

    	#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
    	#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
		#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
		#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

		// Left some keywords (e.g. light layers, cookies) in subgraphs to help avoid unnecessary shader variants
		// But means if those subgraphs are nested in another, you'll need to copy the keywords from blackboard

	#endif
#endif

//------------------------------------------------------------------------------------------------------
// Main Light
//------------------------------------------------------------------------------------------------------

/*
- Obtains the Direction, Color and DistanceAtten for the Main Directional Light
- (DistanceAtten is either 1 or 0, depending if the object is in the light's Culling Mask or not)
- For shadows, see MainLightShadows_float
- For DistanceAtten output to work in the Forward+ path "_CLUSTER_LIGHT_LOOP" keyword is required
*/
void MainLight_float(out float3 Direction, out float3 Color, out float DistanceAtten)
{
    #ifdef SHADERGRAPH_PREVIEW
        Direction = float3(0.650945, 0.650945, -0.390567); 
        Color = 1;
        DistanceAtten = 1;
    #else
        Light mainLight = GetMainLight();
        Direction = mainLight.direction;
        Color = mainLight.color;
        DistanceAtten = mainLight.distanceAttenuation;
    #endif
}

/*
- Tests whether the Main Light Layer Mask appears in the Rendering Layers from renderer
- (Used to support Light Layers, pass your shading from Main Light into this)
- To work in an Unlit Graph, requires keywords :
	- Boolean Keyword, Global Multi-Compile "_LIGHT_LAYERS"
*/
void MainLightLayer_float(float3 Shading, out float3 Out)
{
    #ifdef SHADERGRAPH_PREVIEW
        Out = Shading;
    #else
        #ifdef _LIGHT_LAYERS
            if (IsMatchingLightLayer(GetMainLight().layerMask, GetMeshRenderingLayer()))
        #endif
            Out = Shading;
        #ifndef _LIGHT_LAYERS
            Out = Shading;
        #endif
    #endif
}

/*
- Obtains the Light Cookie assigned to the Main Light
- (For usage, You'd want to Multiply the result with your Light Colour)
- To work in an Unlit Graph, requires keywords :
	- Boolean Keyword, Global Multi-Compile "_LIGHT_COOKIES"
*/
void MainLightCookie_float(float3 WorldPos, out float3 Cookie)
{
    #if defined(_LIGHT_COOKIES)
        Cookie = SampleMainLightCookie(WorldPos);
    #else
        Cookie = 1;
    #endif
}

//------------------------------------------------------------------------------------------------------
// Main Light Shadows
//------------------------------------------------------------------------------------------------------

/*
- Samples the Shadowmap for the Main Light, based on the World Position passed in. (Position node)
*/
void MainLightShadows_float(float3 WorldPos, half4 Shadowmask, out float ShadowAtten)
{
    #ifdef SHADERGRAPH_PREVIEW
        ShadowAtten = 1;
    #else
        float4 shadowCoord;
        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
            shadowCoord = ComputeScreenPos(TransformWorldToHClip(WorldPos));
        #else
            shadowCoord = TransformWorldToShadowCoord(WorldPos);
        #endif
        ShadowAtten = MainLightShadow(shadowCoord, WorldPos, Shadowmask, _MainLightOcclusionProbes);
    #endif
}

void MainLightShadows_float(float3 WorldPos, out float ShadowAtten)
{
    #ifdef SHADERGRAPH_PREVIEW
        ShadowAtten = 1;
    #else
        MainLightShadows_float(WorldPos, half4(1,1,1,1), ShadowAtten);
    #endif
}

//------------------------------------------------------------------------------------------------------
// Baked GI
//------------------------------------------------------------------------------------------------------

/*
- Used to support "Shadowmask" Baked GI mode in Lighting window.
- Ideally sample once in graph, then input into the Main Light Shadows and/or Additional Light subgraphs/functions.
- To work in an Unlit Graph, likely requires keywords :
	- Boolean Keyword, Global Multi-Compile "SHADOWS_SHADOWMASK" 
	- Boolean Keyword, Global Multi-Compile "LIGHTMAP_SHADOW_MIXING"
	- (also LIGHTMAP_ON, but I believe Shader Graph is already defining this one)
*/
void Shadowmask_half(float2 lightmapUV, out half4 Shadowmask)
{
    #ifdef SHADERGRAPH_PREVIEW
        Shadowmask = 1;
    #else
        OUTPUT_LIGHTMAP_UV(lightmapUV, unity_LightmapST, lightmapUV);
        Shadowmask = SAMPLE_SHADOWMASK(lightmapUV);
    #endif
}

/*
- Used to support "Subtractive" Baked GI mode in Lighting window
- Inputs should be ShadowAtten from Main Light Shadows subgraph, Normal Vector (World space) and Baked GI nodes
- To work in an Unlit Graph, likely requires keywords :
	- Boolean Keyword, Global Multi-Compile "LIGHTMAP_SHADOW_MIXING"
	- (also LIGHTMAP_ON, but I believe Shader Graph is already defining this one)
*/
void SubtractiveGI_float(float ShadowAtten, float3 NormalWS, float3 BakedGI, out half3 result)
{
    #ifdef SHADERGRAPH_PREVIEW
        result = 1;
    #else
        Light mainLight = GetMainLight();
        mainLight.shadowAttenuation = ShadowAtten;
        MixRealtimeAndBakedGI(mainLight, NormalWS, BakedGI);
        result = BakedGI;
    #endif
}

//------------------------------------------------------------------------------------------------------
// Default Additional Lights
//------------------------------------------------------------------------------------------------------

#ifndef SHADERGRAPH_PREVIEW
void CalculateLightContribution(
    Light light, float3 worldNormal, float3 worldView, float3 specColor, 
    float smoothness, uint meshRenderingLayers, inout float3 diffuse, inout float3 specular)
{
    #ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        return;
    #endif
            
    float3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
    diffuse += LightingLambert(attenuatedLightColor, light.direction, worldNormal);
    specular += LightingSpecular(attenuatedLightColor, light.direction, worldNormal, worldView, float4(specColor, 0), smoothness);
}
#endif

/*
- Handles additional lights (e.g. additional directional, point, spotlights)
- For custom lighting, you may want to duplicate this and swap the LightingLambert / LightingSpecular functions out. See Toon Example below!
- Requires keywords "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS" & "_CLUSTER_LIGHT_LOOP"
*/
void AdditionalLights_float(
    float3 SpecColor, float Smoothness, float3 WorldPosition, float3 WorldNormal, float3 WorldView, 
    half4 Shadowmask, out float3 Diffuse, out float3 Specular) 
{
    float3 diffuseColor = 0;
    float3 specularColor = 0;

    #ifndef SHADERGRAPH_PREVIEW
        Smoothness = exp2(10 * Smoothness + 1);
        uint pixelLightCount = GetAdditionalLightsCount();
        uint meshRenderingLayers = GetMeshRenderingLayer();

        #if USE_CLUSTER_LIGHT_LOOP
            for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++) 
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
                CalculateLightContribution(light, WorldNormal, WorldView, SpecColor, Smoothness, meshRenderingLayers, diffuseColor, specularColor);
            }
        #endif

        // For Forward+ the LIGHT_LOOP_BEGIN macro will use inputData.normalizedScreenSpaceUV, inputData.positionWS
        InputData inputData = (InputData)0;
        float4 screenPos = ComputeScreenPos(TransformWorldToHClip(WorldPosition));
        inputData.normalizedScreenSpaceUV = screenPos.xy / screenPos.w;
        inputData.positionWS = WorldPosition;

        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
            CalculateLightContribution(light, WorldNormal, WorldView, SpecColor, Smoothness, meshRenderingLayers, diffuseColor, specularColor);
        LIGHT_LOOP_END
    #endif

    Diffuse = diffuseColor;
    Specular = specularColor;
}

//------------------------------------------------------------------------------------------------------
// Additional Lights Toon Example
//------------------------------------------------------------------------------------------------------

/*
- Calculates light attenuation values to produce multiple bands for a toon effect. See AdditionalLightsToon function below
*/
#ifndef SHADERGRAPH_PREVIEW
float ToonAttenuation(int lightIndex, float3 positionWS, float pointBands, float spotBands)
{
    #if !USE_CLUSTER_LIGHT_LOOP
        lightIndex = GetPerObjectLightIndex(lightIndex);
    #endif

    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
        half4 spotDirection = _AdditionalLightsBuffer[lightIndex].spotDirection;
        half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
    #else
        float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
        half4 spotDirection = _AdditionalLightsSpotDir[lightIndex];
        half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[lightIndex];
    #endif

    // Point light calculations
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    float range = rsqrt(distanceAndSpotAttenuation.x);
    float dist = sqrt(distanceSqr) * range; 

    // Spot light calculations
    half3 lightDirection = lightVector * rsqrt(distanceSqr); 
    half SdotL = dot(spotDirection.xyz, lightDirection);
    half spotAtten = saturate(SdotL * distanceAndSpotAttenuation.z + distanceAndSpotAttenuation.w);
    spotAtten *= spotAtten;
    float maskSpotToRange = step(dist, 1);

    // Attenuation
    bool isSpot = (distanceAndSpotAttenuation.z > 0);
    return isSpot ? 
        (floor(spotAtten * spotBands) / spotBands) * maskSpotToRange :
        saturate(1.0 - floor(dist * pointBands) / pointBands);
}

void CalculateToonLightContribution(
    Light light, int lightIndex, float3 worldPos, float pointBands, float spotBands, 
    uint meshRenderingLayers, inout float3 diffuseColor)
{
    #ifdef _LIGHT_LAYERS
        if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            return;
    #endif
    
    if (pointBands <= 1 && spotBands <= 1)
    {
        // Solid colour lights
        diffuseColor += light.color * step(0.0001, light.distanceAttenuation * light.shadowAttenuation);
    }
    else
    {
        // Multiple bands
        diffuseColor += light.color * light.shadowAttenuation * ToonAttenuation(lightIndex, worldPos, pointBands, spotBands);
    }
}
#endif

/*
- Handles additional lights (e.g. point, spotlights) with banded toon effect
- Requires keywords "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS" & "_CLUSTER_LIGHT_LOOP"
*/
void AdditionalLightsToon_float(
    float3 SpecColor, float Smoothness, float3 WorldPosition, float3 WorldNormal, float3 WorldView, half4 Shadowmask,
    float PointLightBands, float SpotLightBands, out float3 Diffuse, out float3 Specular)
{
    float3 diffuseColor = 0;
    float3 specularColor = 0;

    #ifndef SHADERGRAPH_PREVIEW
        Smoothness = exp2(10 * Smoothness + 1);
        uint meshRenderingLayers = GetMeshRenderingLayer();

        #if USE_CLUSTER_LIGHT_LOOP
            for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++) 
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
                CalculateToonLightContribution(light, lightIndex, WorldPosition, PointLightBands, SpotLightBands, meshRenderingLayers, diffuseColor);
            }
        #endif

        // For Forward+ the LIGHT_LOOP_BEGIN macro will use inputData.normalizedScreenSpaceUV, inputData.positionWS
        InputData inputData = (InputData)0;
        float4 screenPos = ComputeScreenPos(TransformWorldToHClip(WorldPosition));
        inputData.normalizedScreenSpaceUV = screenPos.xy / screenPos.w;
        inputData.positionWS = WorldPosition;

        LIGHT_LOOP_BEGIN((uint)GetAdditionalLightsCount())
            Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
            CalculateToonLightContribution(light, lightIndex, WorldPosition, PointLightBands, SpotLightBands, meshRenderingLayers, diffuseColor);
        LIGHT_LOOP_END
    #endif

    Diffuse = diffuseColor;
    Specular = specularColor; // Direct assignment since specular is disabled for toon shader
}

#endif 