using System.Runtime.CompilerServices;

namespace kajarlabs.Framework.Scene.Transforms;

internal enum BuiltInTransformTarget : byte
{
    None = 0,
    Alpha = 1,
    Color = 2,
    Position = 3,
    X = 4,
    Y = 5,
    Size = 6,
    Width = 7,
    Height = 8,
    Scale = 9,
    Rotation = 10,
    Shear = 11,
    Margin = 12,
    Padding = 13,
}

[Flags]
internal enum BuiltInTransformMask : ushort
{
    None = 0,
    Alpha = 1 << 0,
    Color = 1 << 1,
    PositionX = 1 << 2,
    PositionY = 1 << 3,
    Position = PositionX | PositionY,
    SizeX = 1 << 4,
    SizeY = 1 << 5,
    Size = SizeX | SizeY,
    Scale = 1 << 6,
    Rotation = 1 << 7,
    Shear = 1 << 8,
    Margin = 1 << 9,
    Padding = 1 << 10,
}

internal static class BuiltInTransformHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BuiltInTransformMask GetMask(BuiltInTransformTarget target)
    {
        switch (target)
        {
            case BuiltInTransformTarget.None:
                return BuiltInTransformMask.None;

            case BuiltInTransformTarget.Alpha:
                return BuiltInTransformMask.Alpha;

            case BuiltInTransformTarget.Color:
                return BuiltInTransformMask.Color;

            case BuiltInTransformTarget.Position:
                return BuiltInTransformMask.Position;

            case BuiltInTransformTarget.X:
                return BuiltInTransformMask.PositionX;

            case BuiltInTransformTarget.Y:
                return BuiltInTransformMask.PositionY;

            case BuiltInTransformTarget.Size:
                return BuiltInTransformMask.Size;

            case BuiltInTransformTarget.Width:
                return BuiltInTransformMask.SizeX;

            case BuiltInTransformTarget.Height:
                return BuiltInTransformMask.SizeY;

            case BuiltInTransformTarget.Scale:
                return BuiltInTransformMask.Scale;

            case BuiltInTransformTarget.Rotation:
                return BuiltInTransformMask.Rotation;

            case BuiltInTransformTarget.Shear:
                return BuiltInTransformMask.Shear;

            case BuiltInTransformTarget.Margin:
                return BuiltInTransformMask.Margin;

            case BuiltInTransformTarget.Padding:
                return BuiltInTransformMask.Padding;

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported built-in transform target.");
        }
    }

    public static void ValidateTargetForDrawable(Drawable target, BuiltInTransformTarget property)
    {
        _ = GetMask(property);

        if (property == BuiltInTransformTarget.Padding && target is not CompositeDrawable)
            throw new ArgumentException("Padding transforms can only target CompositeDrawable instances.", nameof(target));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformValue ReadValue(Drawable target, BuiltInTransformTarget property)
    {
        switch (property)
        {
            case BuiltInTransformTarget.None:
                return default;

            case BuiltInTransformTarget.Alpha:
                return TransformValue.FromSingle(target.Alpha);

            case BuiltInTransformTarget.Color:
                return TransformValue.FromColor4(target.Color);

            case BuiltInTransformTarget.Position:
                return TransformValue.FromVector2(target.Position);

            case BuiltInTransformTarget.X:
                return TransformValue.FromSingle(target.X);

            case BuiltInTransformTarget.Y:
                return TransformValue.FromSingle(target.Y);

            case BuiltInTransformTarget.Size:
                return TransformValue.FromVector2(target.Size);

            case BuiltInTransformTarget.Width:
                return TransformValue.FromSingle(target.Width);

            case BuiltInTransformTarget.Height:
                return TransformValue.FromSingle(target.Height);

            case BuiltInTransformTarget.Scale:
                return TransformValue.FromVector2(target.Scale);

            case BuiltInTransformTarget.Rotation:
                return TransformValue.FromSingle(target.Rotation);

            case BuiltInTransformTarget.Shear:
                return TransformValue.FromVector2(target.Shear);

            case BuiltInTransformTarget.Margin:
                return TransformValue.FromMarginPadding(target.Margin);

            case BuiltInTransformTarget.Padding:
                return TransformValue.FromMarginPadding(((CompositeDrawable)target).Padding);

            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported built-in transform target.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyValue(Drawable target, BuiltInTransformTarget property, in TransformValue value)
    {
        switch (property)
        {
            case BuiltInTransformTarget.None:
                break;

            case BuiltInTransformTarget.Alpha:
                target.Alpha = value.AsSingle();
                break;

            case BuiltInTransformTarget.Color:
                target.Color = value.AsColor4();
                break;

            case BuiltInTransformTarget.Position:
                target.Position = value.AsVector2();
                break;

            case BuiltInTransformTarget.X:
                target.X = value.AsSingle();
                break;

            case BuiltInTransformTarget.Y:
                target.Y = value.AsSingle();
                break;

            case BuiltInTransformTarget.Size:
                target.Size = value.AsVector2();
                break;

            case BuiltInTransformTarget.Width:
                target.Width = value.AsSingle();
                break;

            case BuiltInTransformTarget.Height:
                target.Height = value.AsSingle();
                break;

            case BuiltInTransformTarget.Scale:
                target.Scale = value.AsVector2();
                break;

            case BuiltInTransformTarget.Rotation:
                target.Rotation = value.AsSingle();
                break;

            case BuiltInTransformTarget.Shear:
                target.Shear = value.AsVector2();
                break;

            case BuiltInTransformTarget.Margin:
                target.Margin = value.AsMarginPadding();
                break;

            case BuiltInTransformTarget.Padding:
                ((CompositeDrawable)target).Padding = value.AsMarginPadding();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported built-in transform target.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyInterpolated(
        Drawable target,
        BuiltInTransformTarget property,
        in TransformValue startValue,
        in TransformValue endValue,
        double time,
        double startTime,
        double endTime,
        Easing easing)
        => ApplyInterpolated(target, property, in startValue, in endValue, time, startTime, endTime, new DefaultEasingFunction(easing));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyInterpolated<TEasing>(
        Drawable target,
        BuiltInTransformTarget property,
        in TransformValue startValue,
        in TransformValue endValue,
        double time,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        switch (property)
        {
            case BuiltInTransformTarget.None:
                break;

            case BuiltInTransformTarget.Alpha:
                target.Alpha = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Color:
                target.Color = Interpolation.ValueAt(time, startValue.AsColor4(), endValue.AsColor4(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Position:
                target.Position = Interpolation.ValueAt(time, startValue.AsVector2(), endValue.AsVector2(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.X:
                target.X = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Y:
                target.Y = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Size:
                target.Size = Interpolation.ValueAt(time, startValue.AsVector2(), endValue.AsVector2(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Width:
                target.Width = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Height:
                target.Height = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Scale:
                target.Scale = Interpolation.ValueAt(time, startValue.AsVector2(), endValue.AsVector2(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Rotation:
                target.Rotation = Interpolation.ValueAt(time, startValue.AsSingle(), endValue.AsSingle(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Shear:
                target.Shear = Interpolation.ValueAt(time, startValue.AsVector2(), endValue.AsVector2(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Margin:
                target.Margin = Interpolation.ValueAt(time, startValue.AsMarginPadding(), endValue.AsMarginPadding(), startTime, endTime, easing);
                break;

            case BuiltInTransformTarget.Padding:
                ((CompositeDrawable)target).Padding = Interpolation.ValueAt(time, startValue.AsMarginPadding(), endValue.AsMarginPadding(), startTime, endTime, easing);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported built-in transform target.");
        }
    }
}
