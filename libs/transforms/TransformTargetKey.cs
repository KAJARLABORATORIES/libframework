using System.Runtime.CompilerServices;

namespace kajarlabs.Framework.Scene.Transforms;

internal readonly struct TransformTargetKey : IEquatable<TransformTargetKey>
{
    public BuiltInTransformMask BuiltInMask { get; }

    public string? CustomTargetName { get; }

    public object? CustomTargetObject { get; }

    private TransformTargetKey(BuiltInTransformMask builtInMask, string? customTargetName, object? customTargetObject)
    {
        BuiltInMask = builtInMask;
        CustomTargetName = customTargetName;
        CustomTargetObject = customTargetObject;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransformTargetKey FromBuiltIn(BuiltInTransformTarget target)
        => new(BuiltInTransformHelper.GetMask(target), null, null);

    public static TransformTargetKey FromCustomName(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return new TransformTargetKey(BuiltInTransformMask.None, targetName, null);
    }

    public static TransformTargetKey FromCustomObject(object targetObject)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        return new TransformTargetKey(BuiltInTransformMask.None, null, targetObject);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConflictsWith(in TransformTargetKey other)
    {
        if (BuiltInMask != BuiltInTransformMask.None || other.BuiltInMask != BuiltInTransformMask.None)
            return (BuiltInMask & other.BuiltInMask) != BuiltInTransformMask.None;

        if (CustomTargetObject is not null || other.CustomTargetObject is not null)
            return ReferenceEquals(CustomTargetObject, other.CustomTargetObject);

        if (CustomTargetName is not null && other.CustomTargetName is not null)
            return string.Equals(CustomTargetName, other.CustomTargetName, StringComparison.Ordinal);

        return false;
    }

    public bool Equals(TransformTargetKey other)
        => BuiltInMask == other.BuiltInMask
            && string.Equals(CustomTargetName, other.CustomTargetName, StringComparison.Ordinal)
            && ReferenceEquals(CustomTargetObject, other.CustomTargetObject);

    public override bool Equals(object? obj) => obj is TransformTargetKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            (int)BuiltInMask,
            CustomTargetName is not null ? StringComparer.Ordinal.GetHashCode(CustomTargetName) : 0,
            CustomTargetObject is not null ? RuntimeHelpers.GetHashCode(CustomTargetObject) : 0);

    public static bool operator ==(TransformTargetKey left, TransformTargetKey right) => left.Equals(right);

    public static bool operator !=(TransformTargetKey left, TransformTargetKey right) => !left.Equals(right);
}
