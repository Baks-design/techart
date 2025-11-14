Shader "Custom/Code/AfterImageEffect"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)		
        _RimColor("Rim Color", Color) = (0,1,1,1)
        _MainTex("Main Texture", 2D) = "white" {}
        _RimPower("Rim Power", Range(1, 50)) = 20
        [PerRendererData]_Fade("Fade Amount", Range(0, 1)) = 1
        _Grow("Grow", Range(0, 1)) = 0.05
        _FadeSharpness("Fade Sharpness", Range(0.01, 0.5)) = 0.05
        [Toggle]_EnableRim("Enable Rim Effect", Float) = 1
        [Toggle]_AlphaTest("Alpha Test", Float) = 0
        _AlphaThreshold("Alpha Threshold", Range(0, 0.1)) = 0.01
    }

    SubShader
    {
        Tags { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Back
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ENABLERIM_ON
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                #ifdef _ENABLERIM_ON
                    float3 normalWS : TEXCOORD1;
                    float3 viewDirWS : TEXCOORD2;
                #endif
                float fade : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _Fade;
                half _Grow;
                half _FadeSharpness;
                #ifdef _ALPHATEST_ON
                    half _AlphaThreshold;
                #endif
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Grow effect - expands as it fades out
                half fadeFactor = _Fade;
                half growAmount = (1.0 - fadeFactor) * _Grow;
                
                float3 positionOS = input.positionOS.xyz + input.normalOS * growAmount;
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fade = _Fade;

                #ifdef _ENABLERIM_ON
                    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                    float3 positionWS = TransformObjectToWorld(positionOS);
                    output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                #endif

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                #ifdef _ALPHATEST_ON
                    clip(texSample.a - _AlphaThreshold);
                #endif

                half4 finalColor = texSample * _Color;

                #ifdef _ENABLERIM_ON
                    half3 normalWS = normalize(input.normalWS);
                    half3 viewDirWS = normalize(input.viewDirWS);
                    half NdotV = 1.0 - saturate(dot(normalWS, viewDirWS));
                    half rim = pow(NdotV, _RimPower) * _RimColor.a;
                    
                    // Add rim lighting
                    finalColor.rgb += rim * _RimColor.rgb;
                    finalColor.a = max(finalColor.a, rim * 0.5);
                #endif

                // Smooth fade with sharpness control
                half smoothFade = smoothstep(0.0, _FadeSharpness, input.fade);
                finalColor.a *= smoothFade;
                
                return finalColor;
            }
            ENDHLSL
        }   

        // Only if want Shadows in After Images
        // Shadow pass for URP compatibility
        // Pass
        // {
        //     Name "ShadowCaster"
        //     Tags{"LightMode" = "ShadowCaster"}

        //     ZWrite On
        //     ZTest LEqual
        //     ColorMask 0
        //     Cull Back

        //     HLSLPROGRAM
        //     #pragma vertex ShadowPassVertex
        //     #pragma fragment ShadowPassFragment
        //     #pragma multi_compile_instancing

        //     #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
        //     #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
        //     ENDHLSL
        // }
    }
}