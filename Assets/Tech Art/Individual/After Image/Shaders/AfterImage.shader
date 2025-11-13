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
        [Toggle]_EnableRim("Enable Rim Effect", Float) = 1
        [Toggle]_AlphaTest("Alpha Test", Float) = 0
        _AlphaThreshold("Alpha Threshold", Range(0, 0.1)) = 0.01
    }
    SubShader
    {
        Tags { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent" 
            "RenderPipeline" = "UniversalRenderPipeline"
            "IgnoreProjector" = "True"
        }
        
        Blend SrcAlpha One 
        ZWrite Off
        ZTest LEqual
        Cull Back
        
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature _ENABLERIM_ON
            #pragma shader_feature _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
                #if _ENABLERIM_ON
                    half3 viewDir : TEXCOORD1;
                    half3 normalWS : TEXCOORD2;
                #endif
                half fade : TEXCOORD3;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    half fogFactor : TEXCOORD4;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _RimColor;
            half _RimPower;
            float4 _MainTex_ST;
            half4 _Color;
            half _Fade;
            half _Grow;
            half _FadeSharpness;
            #if _ALPHATEST_ON
                half _AlphaThreshold;
            #endif
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                half fadeFactor = saturate(1.0h - _Fade);
                half growAmount = fadeFactor * _Grow;
                
                float3 positionOS = IN.positionOS.xyz + IN.normal * growAmount;
                OUT.positionHCS = TransformObjectToHClip(positionOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.fade = fadeFactor;
                
                #if _ENABLERIM_ON
                    OUT.normalWS = TransformObjectToWorldNormal(IN.normal);
                    float3 worldPos = TransformObjectToWorld(positionOS);
                    OUT.viewDir = GetWorldSpaceNormalizeViewDir(worldPos);
                #endif
                
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                #endif
                
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                #if _ALPHATEST_ON
                    clip(texSample.a - _AlphaThreshold);
                #endif
                
                half rim = 0.0h;
                #if _ENABLERIM_ON
                    half3 normalWS = normalize(IN.normalWS);
                    half3 viewDir = normalize(IN.viewDir);
                    half NdotV = saturate(dot(normalWS, viewDir));
                    rim = exp2(_RimPower * log(1.0h - NdotV)) * _RimColor.a;
                #endif
                
                half4 baseColor = texSample * _Color;
                half4 finalColor = baseColor;
                
                #if _ENABLERIM_ON
                    finalColor.rgb += rim * _RimColor.rgb;
                #endif
                
                half textureLuminance = dot(texSample.rgb, half3(0.299h, 0.587h, 0.114h));
                finalColor.a = (baseColor.a + rim) * textureLuminance;
                
                half fadeEdge = 1.0h - IN.fade;
                half alphaMin = finalColor.a;
                half alphaMax = finalColor.a + _FadeSharpness;
                
                half t = saturate((fadeEdge - alphaMin) / (alphaMax - alphaMin));
                finalColor.a = t * t * (3.0h - 2.0h * t);
                
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    finalColor.rgb = MixFog(finalColor.rgb, IN.fogFactor);
                #endif
                
                return saturate(finalColor);
            }
            ENDHLSL
        }
        
    }
    
    SubShader
    {
        Tags { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent"
        }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_simple
            #pragma fragment frag_simple
            #include "UnityCG.cginc"
            
            struct appdata_simple {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f_simple {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                half fade : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Fade;
            half _Grow;
            half4 _Color;
            
            v2f_simple vert_simple(appdata_simple v) {
                v2f_simple o;
                half fadeFactor = saturate(1.0h - _Fade);
                v.vertex.xyz += v.vertex.xyz * fadeFactor * _Grow * 0.01h;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fade = fadeFactor;
                return o;
            }
            
            half4 frag_simple(v2f_simple i) : SV_Target {
                half4 texSample = tex2D(_MainTex, i.uv);
                half4 col = texSample * _Color;
                col.a *= (1.0h - i.fade);
                return col;
            }
            ENDCG
        }
    }
}
