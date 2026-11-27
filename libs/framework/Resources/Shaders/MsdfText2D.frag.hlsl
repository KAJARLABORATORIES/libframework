Texture2D<float4> SpriteTexture : register(t0, space2);
SamplerState SpriteSampler : register(s0, space2);

cbuffer MsdfParams : register(b0, space3)
{
    float2 UnitRange;
    float DistanceBias;
    float Reserved;
};

struct FragmentInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;
};

float median(float r, float g, float b)
{
    return max(min(r, g), min(max(r, g), b));
}

float4 main(FragmentInput input) : SV_Target0
{
    float4 texel = SpriteTexture.Sample(SpriteSampler, input.TexCoord);
    float sd = median(texel.r, texel.g, texel.b);
    float2 screenTexSize = 1.0f / max(fwidth(input.TexCoord), float2(1e-6f, 1e-6f));
    float screenPxRange = max(0.5f * dot(UnitRange, screenTexSize), 1.0f);
    float screenPxDistance = screenPxRange * (sd - 0.5f + DistanceBias);
    float opacity = clamp(screenPxDistance + 0.5f, 0.0f, 1.0f);
    return float4(input.Color.rgb, input.Color.a * opacity);
}
