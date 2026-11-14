cbuffer ProjectionUniform : register(b0, space1)
{
    row_major float4x4 Projection;
};

struct VertexInput
{
    float2 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float4 Color : TEXCOORD2;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;
};

VertexOutput main(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(float4(input.Position, 0.0f, 1.0f), Projection);
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    return output;
}
