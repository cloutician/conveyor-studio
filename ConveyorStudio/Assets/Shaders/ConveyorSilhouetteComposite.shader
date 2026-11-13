Shader "Conveyor Studio/Silhouette Border"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outside border only"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _ConveyorOutlineParameters; // texel width, texel height, border width
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 centre = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                // Never paint the object itself. All model parts were united in the mask,
                // so mesh boundaries, bolts and belt seams cannot produce internal lines.
                if (centre.a > 0.5h) return half4(0, 0, 0, 0);
                half4 nearest = 0;
                float closest = 100;
                [unroll] for (int y = -4; y <= 4; y++)
                [unroll] for (int x = -4; x <= 4; x++)
                {
                    float distance = length(float2(x, y));
                    if (distance > _ConveyorOutlineParameters.z + 0.5 || distance >= closest) continue;
                    half4 sample = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + float2(x, y) * _ConveyorOutlineParameters.xy);
                    if (sample.a > 0.5h) { nearest = sample; closest = distance; }
                }
                nearest.a *= saturate(_ConveyorOutlineParameters.z + 0.5 - closest);
                return nearest;
            }
            ENDHLSL
        }
    }
}
