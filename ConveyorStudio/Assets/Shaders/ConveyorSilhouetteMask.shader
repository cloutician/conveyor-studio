Shader "Conveyor Studio/Silhouette Mask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Combined silhouette"
            Cull Back
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _ConveyorOutlineColour;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float sceneDepth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                float surfaceDistance = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneDistance = LinearEyeDepth(sceneDepth, _ZBufferParams);
                clip(sceneDistance + 0.003 - surfaceDistance);
                return half4(_ConveyorOutlineColour.rgb, 1);
            }
            ENDHLSL
        }
    }
}
