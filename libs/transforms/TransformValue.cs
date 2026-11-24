using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct TransformValue : IEquatable<TransformValue>
{
    public readonly float A;
    public readonly float B;
    public readonly float C;
    public readonly float D;

    public TransformValue(float a, float b = 0f, float c = 0f, float d = 0f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformValue FromSingle(float value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformValue FromVector2(Vector2 value) => new(value.X, value.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformValue FromColor4(Color4 value) => new(value.R, value.G, value.B, value.A);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformValue FromMarginPadding(MarginPadding value) => new(value.Left, value.Top, value.Right, value.Bottom);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float AsSingle() => A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 AsVector2() => new(A, B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color4 AsColor4() => new(A, B, C, D);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MarginPadding AsMarginPadding() => new(A, B, C, D);

    public bool Equals(TransformValue other)
        => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) && D.Equals(other.D);

    public override bool Equals(object? obj) => obj is TransformValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, B, C, D);

    public static bool operator ==(TransformValue left, TransformValue right) => left.Equals(right);

    public static bool operator !=(TransformValue left, TransformValue right) => !left.Equals(right);
}
