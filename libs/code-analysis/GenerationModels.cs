using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace kajarlabs.Framework.CodeAnalysis;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> EMPTY = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> array;

    public EquatableArray(ImmutableArray<T> array)
    {
        this.array = array.IsDefault ? ImmutableArray<T>.Empty : array;
    }

    public ImmutableArray<T> Items => array.IsDefault ? ImmutableArray<T>.Empty : array;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;

            foreach (var item in Items)
                hash = (hash * 31) + item.GetHashCode();

            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}

internal readonly struct EquatableString : IEquatable<EquatableString>
{
    public EquatableString(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool Equals(EquatableString other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is EquatableString other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}

internal readonly struct LocationData : IEquatable<LocationData>
{
    public LocationData(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        TextSpan = textSpan;
        LineSpan = lineSpan;
    }

    public string FilePath { get; }

    public TextSpan TextSpan { get; }

    public LinePositionSpan LineSpan { get; }

    public static LocationData? FromLocation(Location? location)
    {
        if (location is null || !location.IsInSource)
            return null;

        return new LocationData(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public bool Equals(LocationData other)
        => string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
           && TextSpan.Equals(other.TextSpan)
           && LineSpan.Equals(other.LineSpan);

    public override bool Equals(object? obj) => obj is LocationData other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(FilePath);
            hash = (hash * 31) + TextSpan.GetHashCode();
            hash = (hash * 31) + LineSpan.GetHashCode();
            return hash;
        }
    }
}

internal readonly struct DiagnosticData : IEquatable<DiagnosticData>
{
    public DiagnosticData(DiagnosticRule rule, LocationData? location, EquatableArray<EquatableString> args)
    {
        Rule = rule;
        Location = location;
        Args = args;
    }

    public DiagnosticRule Rule { get; }

    public LocationData? Location { get; }

    public EquatableArray<EquatableString> Args { get; }

    public static DiagnosticData Create(DiagnosticRule rule, Location? location, params string[] args)
    {
        var builder = ImmutableArray.CreateBuilder<EquatableString>(args.Length);

        foreach (var arg in args)
            builder.Add(new EquatableString(arg));

        return new DiagnosticData(rule, LocationData.FromLocation(location), new EquatableArray<EquatableString>(builder.MoveToImmutable()));
    }

    public Diagnostic ToDiagnostic()
    {
        var messageArgs = new object[Args.Count];

        for (var i = 0; i < Args.Count; i++)
            messageArgs[i] = Args[i].Value;

        return Diagnostic.Create(
            DiagnosticDescriptors.GetDescriptor(Rule),
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            messageArgs);
    }

    public bool Equals(DiagnosticData other)
        => Rule == other.Rule
           && Nullable.Equals(Location, other.Location)
           && Args.Equals(other.Args);

    public override bool Equals(object? obj) => obj is DiagnosticData other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + (int)Rule;
            hash = (hash * 31) + (Location?.GetHashCode() ?? 0);
            hash = (hash * 31) + Args.GetHashCode();
            return hash;
        }
    }
}

internal readonly struct EnclosingTypeModel : IEquatable<EnclosingTypeModel>
{
    public EnclosingTypeModel(string keyword, string name, EquatableArray<EquatableString> typeParameters)
    {
        Keyword = keyword;
        Name = name;
        TypeParameters = typeParameters;
    }

    public string Keyword { get; }

    public string Name { get; }

    public EquatableArray<EquatableString> TypeParameters { get; }

    public bool Equals(EnclosingTypeModel other)
        => string.Equals(Keyword, other.Keyword, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && TypeParameters.Equals(other.TypeParameters);

    public override bool Equals(object? obj) => obj is EnclosingTypeModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Keyword);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Name);
            hash = (hash * 31) + TypeParameters.GetHashCode();
            return hash;
        }
    }
}

internal readonly struct CachedItemModel : IEquatable<CachedItemModel>
{
    public CachedItemModel(
        bool isSelf,
        string memberName,
        string keyTypeName,
        bool useCacheAs,
        bool memberIsNullableReference,
        bool memberIsNullableValueType,
        int order)
    {
        IsSelf = isSelf;
        MemberName = memberName;
        KeyTypeName = keyTypeName;
        UseCacheAs = useCacheAs;
        MemberIsNullableReference = memberIsNullableReference;
        MemberIsNullableValueType = memberIsNullableValueType;
        Order = order;
    }

    public bool IsSelf { get; }

    public string MemberName { get; }

    public string KeyTypeName { get; }

    public bool UseCacheAs { get; }

    public bool MemberIsNullableReference { get; }

    public bool MemberIsNullableValueType { get; }

    public int Order { get; }

    public bool Equals(CachedItemModel other)
        => IsSelf == other.IsSelf
           && string.Equals(MemberName, other.MemberName, StringComparison.Ordinal)
           && string.Equals(KeyTypeName, other.KeyTypeName, StringComparison.Ordinal)
           && UseCacheAs == other.UseCacheAs
           && MemberIsNullableReference == other.MemberIsNullableReference
           && MemberIsNullableValueType == other.MemberIsNullableValueType
           && Order == other.Order;

    public override bool Equals(object? obj) => obj is CachedItemModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + IsSelf.GetHashCode();
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(MemberName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(KeyTypeName);
            hash = (hash * 31) + UseCacheAs.GetHashCode();
            hash = (hash * 31) + MemberIsNullableReference.GetHashCode();
            hash = (hash * 31) + MemberIsNullableValueType.GetHashCode();
            hash = (hash * 31) + Order;
            return hash;
        }
    }
}

internal readonly struct ResolvedItemModel : IEquatable<ResolvedItemModel>
{
    public ResolvedItemModel(
        string memberName,
        string keyTypeName,
        bool isOptional,
        bool memberIsNullableValueType,
        bool memberIsAnnotatedNullable,
        int order)
    {
        MemberName = memberName;
        KeyTypeName = keyTypeName;
        IsOptional = isOptional;
        MemberIsNullableValueType = memberIsNullableValueType;
        MemberIsAnnotatedNullable = memberIsAnnotatedNullable;
        Order = order;
    }

    public string MemberName { get; }

    public string KeyTypeName { get; }

    public bool IsOptional { get; }

    public bool MemberIsNullableValueType { get; }

    public bool MemberIsAnnotatedNullable { get; }

    public int Order { get; }

    public bool Equals(ResolvedItemModel other)
        => string.Equals(MemberName, other.MemberName, StringComparison.Ordinal)
           && string.Equals(KeyTypeName, other.KeyTypeName, StringComparison.Ordinal)
           && IsOptional == other.IsOptional
           && MemberIsNullableValueType == other.MemberIsNullableValueType
           && MemberIsAnnotatedNullable == other.MemberIsAnnotatedNullable
           && Order == other.Order;

    public override bool Equals(object? obj) => obj is ResolvedItemModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(MemberName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(KeyTypeName);
            hash = (hash * 31) + IsOptional.GetHashCode();
            hash = (hash * 31) + MemberIsNullableValueType.GetHashCode();
            hash = (hash * 31) + MemberIsAnnotatedNullable.GetHashCode();
            hash = (hash * 31) + Order;
            return hash;
        }
    }
}

internal readonly struct TypeHeaderModel : IEquatable<TypeHeaderModel>
{
    public TypeHeaderModel(
        string hintName,
        string? ns,
        EquatableArray<EnclosingTypeModel> enclosingTypes,
        string typeKeyword,
        string typeName,
        EquatableArray<EquatableString> typeParameters,
        bool canGenerate,
        EquatableArray<DiagnosticData> typeDiagnostics)
    {
        HintName = hintName;
        Namespace = ns;
        EnclosingTypes = enclosingTypes;
        TypeKeyword = typeKeyword;
        TypeName = typeName;
        TypeParameters = typeParameters;
        CanGenerate = canGenerate;
        TypeDiagnostics = typeDiagnostics;
    }

    public string HintName { get; }

    public string? Namespace { get; }

    public EquatableArray<EnclosingTypeModel> EnclosingTypes { get; }

    public string TypeKeyword { get; }

    public string TypeName { get; }

    public EquatableArray<EquatableString> TypeParameters { get; }

    public bool CanGenerate { get; }

    public EquatableArray<DiagnosticData> TypeDiagnostics { get; }

    public bool Equals(TypeHeaderModel other)
        => string.Equals(HintName, other.HintName, StringComparison.Ordinal)
           && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
           && EnclosingTypes.Equals(other.EnclosingTypes)
           && string.Equals(TypeKeyword, other.TypeKeyword, StringComparison.Ordinal)
           && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
           && TypeParameters.Equals(other.TypeParameters)
           && CanGenerate == other.CanGenerate
           && TypeDiagnostics.Equals(other.TypeDiagnostics);

    public override bool Equals(object? obj) => obj is TypeHeaderModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(HintName);
            hash = (hash * 31) + (Namespace is null ? 0 : StringComparer.Ordinal.GetHashCode(Namespace));
            hash = (hash * 31) + EnclosingTypes.GetHashCode();
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeKeyword);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeName);
            hash = (hash * 31) + TypeParameters.GetHashCode();
            hash = (hash * 31) + CanGenerate.GetHashCode();
            hash = (hash * 31) + TypeDiagnostics.GetHashCode();
            return hash;
        }
    }
}

internal readonly struct TargetExtractionItem : IEquatable<TargetExtractionItem>
{
    public TargetExtractionItem(
        TypeHeaderModel header,
        EquatableArray<CachedItemModel> cachedItems,
        EquatableArray<ResolvedItemModel> resolvedItems,
        EquatableArray<DiagnosticData> memberDiagnostics)
    {
        Header = header;
        CachedItems = cachedItems;
        ResolvedItems = resolvedItems;
        MemberDiagnostics = memberDiagnostics;
    }

    public TypeHeaderModel Header { get; }

    public EquatableArray<CachedItemModel> CachedItems { get; }

    public EquatableArray<ResolvedItemModel> ResolvedItems { get; }

    public EquatableArray<DiagnosticData> MemberDiagnostics { get; }

    public bool Equals(TargetExtractionItem other)
        => Header.Equals(other.Header)
           && CachedItems.Equals(other.CachedItems)
           && ResolvedItems.Equals(other.ResolvedItems)
           && MemberDiagnostics.Equals(other.MemberDiagnostics);

    public override bool Equals(object? obj) => obj is TargetExtractionItem other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + Header.GetHashCode();
            hash = (hash * 31) + CachedItems.GetHashCode();
            hash = (hash * 31) + ResolvedItems.GetHashCode();
            hash = (hash * 31) + MemberDiagnostics.GetHashCode();
            return hash;
        }
    }
}
