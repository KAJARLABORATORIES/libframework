Texture2D<float4> SpriteTexture : register(t0, space2);
SamplerState SpriteSampler : register(s0, space2);

struct FragmentInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;
};

float4 main(FragmentInput input) : SV_Target0
{
    return SpriteTexture.Sample(SpriteSampler, input.TexCoord) * input.Color;
}
