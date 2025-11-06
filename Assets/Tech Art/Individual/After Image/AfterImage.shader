Shader "Custom/FX/After Image Effect"
{
    Properties
    {
        _Color("Extra Color", Color) = (1,1,1,1)		
        _RimColor("Rim Color", Color) = (0,1,1,1)
        _MainTex("Main Texture", 2D) = "black" {}
        _RimPower("Rim Power", Range(1, 50)) = 20
        [PerRendererData]_Fade("Fade Amount", Range(0, 1)) = 1
        _Grow("Grow", Range(0, 1)) = 0.05
        _FadeSharpness("Fade Sharpness", Range(0.01, 0.5)) = 0.05
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalRenderPipeline" }
        Blend SrcAlpha One 
        ZWrite Off
        Cull Back
        
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
                float3 viewDir : TEXCOORD1;    // View direction for rim lighting
                float3 normalWS : TEXCOORD2;   // World space normal
                float fade : TEXCOORD3;        // Pre-calculated fade factor
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _RimColor;
            float _RimPower;
            float4 _MainTex_ST;
            float _Fade;
            float4 _Color;
            float _Grow;
            float _FadeSharpness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                // Pre-calculate fade factor to avoid redundant calculations
                float fadeFactor = saturate(1.0 - _Fade);
                
                // Apply vertex expansion - optimized calculation
                IN.positionOS.xyz += IN.normal * fadeFactor * _Grow;

                // Transform to clip space
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                
                // Calculate world position and normal
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normal);
                
                // Calculate view direction in world space
                OUT.viewDir = GetWorldSpaceNormalizeViewDir(worldPos);
                
                // Pass fade factor to fragment shader
                OUT.fade = fadeFactor;
                
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sample texture once
                float4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                // Calculate rim lighting
                float NdotV = saturate(dot(normalize(IN.normalWS), normalize(IN.viewDir)));
                float rim = pow(1.0 - NdotV, _RimPower);
                
                // Calculate base color with texture and tint
                float4 baseColor = texSample * _Color;
                
                // Add rim contribution
                float4 finalColor = baseColor + (rim * _RimColor);
                
                // Calculate alpha channel
                float textureLuminance = dot(texSample.rgb, float3(0.299, 0.587, 0.114)); // Luminance calculation
                finalColor.a = (baseColor.a + rim) * textureLuminance;
                
                // Apply fade with smoothstep - use pre-calculated fade factor
                finalColor.a = smoothstep(finalColor.a, finalColor.a + _FadeSharpness, 1.0 - IN.fade);
                
                // Clamp to valid range
                return saturate(finalColor);
            }
            ENDHLSL
        }
    }
}