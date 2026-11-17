using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace kajarlabs.Framework.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class DependencyInjectionGenerator : IIncrementalGenerator
{
    private const string cached_attribute_metadata_name = "kajarlabs.Framework.DependencyInjection.CachedAttribute";
    private const string resolved_attribute_metadata_name = "kajarlabs.Framework.DependencyInjection.ResolvedAttribute";
    private const string drawable_metadata_name = "kajarlabs.Framework.Scene.Drawable";
    private const string game_metadata_name = "kajarlabs.Framework.Game";

    private static readonly SymbolDisplayFormat fully_qualified_format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                              | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                              | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cachedItems = context.SyntaxProvider.ForAttributeWithMetadataName(
            cached_attribute_metadata_name,
            predicate: static (node, _) => isCandidateSyntaxNode(node),
            transform: static (ctx, ct) => extractCached(ctx, ct))
            .Where(static item => item.HasValue)
            .Select(static (item, _) => item!.Value);

        var resolvedItems = context.SyntaxProvider.ForAttributeWithMetadataName(
            resolved_attribute_metadata_name,
            predicate: static (node, _) => isCandidateSyntaxNode(node),
            transform: static (ctx, ct) => extractResolved(ctx, ct))
            .Where(static item => item.HasValue)
            .Select(static (item, _) => item!.Value);

        var combined = cachedItems.Collect().Combine(resolvedItems.Collect());

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            emitAll(spc, pair.Left, pair.Right);
        });
    }

    private static bool isCandidateSyntaxNode(SyntaxNode node)
        => node is TypeDeclarationSyntax or VariableDeclaratorSyntax or PropertyDeclarationSyntax;

    private static TargetExtractionItem? extractCached(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is INamedTypeSymbol typeSymbol)
            return extractCachedOnType(typeSymbol, context, cancellationToken);

        if (context.TargetSymbol is IFieldSymbol or IPropertySymbol)
            return extractCachedOnMember(context.TargetSymbol, context, cancellationToken);

        return null;
    }

    private static TargetExtractionItem? extractCachedOnType(
        INamedTypeSymbol typeSymbol,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var header = buildTypeHeader(typeSymbol, context.SemanticModel.Compilation, cancellationToken);
        var cachedBuilder = ImmutableArray.CreateBuilder<CachedItemModel>();
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<DiagnosticData>();

        if (typeSymbol.IsStatic)
        {
            diagnosticsBuilder.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidCachedTarget,
                getTargetLocation(context.TargetNode, typeSymbol),
                typeSymbol.Name,
                "type must not be static"));
        }
        else if (header.CanGenerate)
        {
            var order = 0;

            foreach (var attr in context.Attributes)
            {
                if (tryResolveCachedKeyType(
                    typeSymbol,
                    typeSymbol,
                    attr,
                    context.SemanticModel.Compilation,
                    getAttributeOrTargetLocation(attr, context.TargetNode, typeSymbol),
                    diagnosticsBuilder,
                    out var keyType))
                {
                    cachedBuilder.Add(new CachedItemModel(
                        isSelf: true,
                        memberName: string.Empty,
                        keyTypeName: formatType(keyType),
                        useCacheAs: isPotentiallyNullableTypeParameter(keyType),
                        memberIsNullableReference: false,
                        memberIsNullableValueType: false,
                        order: order++));
                }
            }
        }

        return new TargetExtractionItem(
            header,
            new EquatableArray<CachedItemModel>(cachedBuilder.ToImmutable()),
            EquatableArray<ResolvedItemModel>.EMPTY,
            new EquatableArray<DiagnosticData>(diagnosticsBuilder.ToImmutable()));
    }

    private static TargetExtractionItem? extractCachedOnMember(
        ISymbol memberSymbol,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var containingType = memberSymbol.ContainingType;

        if (containingType is null)
            return null;

        var header = buildTypeHeader(containingType, context.SemanticModel.Compilation, cancellationToken);
        var cachedBuilder = ImmutableArray.CreateBuilder<CachedItemModel>();
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<DiagnosticData>();

        if (!validateCachedMember(memberSymbol, context.TargetNode, diagnosticsBuilder, out var memberType))
        {
            return new TargetExtractionItem(
                header,
                EquatableArray<CachedItemModel>.EMPTY,
                EquatableArray<ResolvedItemModel>.EMPTY,
                new EquatableArray<DiagnosticData>(diagnosticsBuilder.ToImmutable()));
        }

        if (header.CanGenerate)
        {
            var isNullableValueType = unwrapNullableValueType(memberType, out var unwrappedType);
            var effectiveMemberType = isNullableValueType ? unwrappedType : stripTopLevelNullable(memberType);
            var isNullableReference = !isNullableValueType
                                      && !memberType.IsValueType
                                      && (memberType.NullableAnnotation == NullableAnnotation.Annotated
                                          || memberType is ITypeParameterSymbol);

            var baseOrder = context.TargetNode.SpanStart + 1_000_000;
            var attrIndex = 0;

            foreach (var attr in context.Attributes)
            {
                if (tryResolveCachedKeyType(
                    memberSymbol,
                    effectiveMemberType,
                    attr,
                    context.SemanticModel.Compilation,
                    getAttributeOrTargetLocation(attr, context.TargetNode, memberSymbol),
                    diagnosticsBuilder,
                    out var keyType))
                {
                    cachedBuilder.Add(new CachedItemModel(
                        isSelf: false,
                        memberName: memberSymbol.Name,
                        keyTypeName: formatType(keyType),
                        useCacheAs: isPotentiallyNullableTypeParameter(keyType),
                        memberIsNullableReference: isNullableReference,
                        memberIsNullableValueType: isNullableValueType,
                        order: baseOrder + attrIndex));
                }

                attrIndex++;
            }
        }

        return new TargetExtractionItem(
            header,
            new EquatableArray<CachedItemModel>(cachedBuilder.ToImmutable()),
            EquatableArray<ResolvedItemModel>.EMPTY,
            new EquatableArray<DiagnosticData>(diagnosticsBuilder.ToImmutable()));
    }

    private static TargetExtractionItem? extractResolved(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not (IFieldSymbol or IPropertySymbol))
            return null;

        var memberSymbol = context.TargetSymbol;
        var containingType = memberSymbol.ContainingType;

        if (containingType is null)
            return null;

        var header = buildTypeHeader(containingType, context.SemanticModel.Compilation, cancellationToken);
        var resolvedBuilder = ImmutableArray.CreateBuilder<ResolvedItemModel>();
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<DiagnosticData>();

        if (!validateResolvedMember(memberSymbol, context.TargetNode, diagnosticsBuilder, out var memberType))
        {
            return new TargetExtractionItem(
                header,
                EquatableArray<CachedItemModel>.EMPTY,
                EquatableArray<ResolvedItemModel>.EMPTY,
                new EquatableArray<DiagnosticData>(diagnosticsBuilder.ToImmutable()));
        }

        if (header.CanGenerate)
        {
            var attr = context.Attributes[0];
            bool? explicitCanBeNull = null;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "CanBeNull" && namedArg.Value.Value is bool value)
                    explicitCanBeNull = value;
            }

            var isNullableValueType = unwrapNullableValueType(memberType, out var unwrappedType);
            var isUnconstrainedTypeParameter = memberType is ITypeParameterSymbol tp && !tp.IsReferenceType;
            var isAnnotatedNullable = isNullableValueType || memberType.NullableAnnotation == NullableAnnotation.Annotated;
            var keyType = isNullableValueType ? unwrappedType : stripTopLevelNullable(memberType);
            var isOptional = explicitCanBeNull ?? isAnnotatedNullable;

            resolvedBuilder.Add(new ResolvedItemModel(
                memberName: memberSymbol.Name,
                keyTypeName: formatType(keyType),
                isOptional: isOptional,
                memberIsNullableValueType: isNullableValueType,
                memberIsAnnotatedNullable: isAnnotatedNullable && !isUnconstrainedTypeParameter,
                order: context.TargetNode.SpanStart));
        }

        return new TargetExtractionItem(
            header,
            EquatableArray<CachedItemModel>.EMPTY,
            new EquatableArray<ResolvedItemModel>(resolvedBuilder.ToImmutable()),
            new EquatableArray<DiagnosticData>(diagnosticsBuilder.ToImmutable()));
    }

    private static TypeHeaderModel buildTypeHeader(
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var typeDiagnostics = ImmutableArray.CreateBuilder<DiagnosticData>();

        var derivesFromFrameworkBase = derivesFromDrawableOrGame(typeSymbol, compilation);

        if (!derivesFromFrameworkBase)
        {
            typeDiagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.UnsupportedContainingType,
                getTypeIdentifierLocation(typeSymbol, cancellationToken),
                typeSymbol.Name));
        }

        var missingPartial = false;

        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            foreach (var syntaxRef in current.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax(cancellationToken) is TypeDeclarationSyntax typeDecl
                    && !typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    missingPartial = true;
                    typeDiagnostics.Add(DiagnosticData.Create(
                        DiagnosticRule.MissingPartial,
                        typeDecl.Identifier.GetLocation(),
                        current.Name));
                }
            }
        }

        var enclosingBuilder = ImmutableArray.CreateBuilder<EnclosingTypeModel>();
        var stack = new Stack<INamedTypeSymbol>();

        for (var parent = typeSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
            stack.Push(parent);

        while (stack.Count > 0)
        {
            var parent = stack.Pop();
            enclosingBuilder.Add(new EnclosingTypeModel(
                getTypeKeyword(parent),
                parent.Name,
                extractTypeParameters(parent)));
        }

        var ns = typeSymbol.ContainingNamespace is { IsGlobalNamespace: false }
            ? typeSymbol.ContainingNamespace.ToDisplayString()
            : null;

        var canGenerate = derivesFromFrameworkBase && !missingPartial && !typeSymbol.IsStatic && typeSymbol.TypeKind == TypeKind.Class;

        return new TypeHeaderModel(
            hintName: buildHintName(typeSymbol),
            ns: ns,
            enclosingTypes: new EquatableArray<EnclosingTypeModel>(enclosingBuilder.ToImmutable()),
            typeKeyword: getTypeKeyword(typeSymbol),
            typeName: typeSymbol.Name,
            typeParameters: extractTypeParameters(typeSymbol),
            canGenerate: canGenerate,
            typeDiagnostics: new EquatableArray<DiagnosticData>(typeDiagnostics.ToImmutable()));
    }

    private static bool derivesFromDrawableOrGame(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsRecord)
            return false;

        var drawableType = compilation.GetTypeByMetadataName(drawable_metadata_name);
        var gameType = compilation.GetTypeByMetadataName(game_metadata_name);

        for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (drawableType is not null && SymbolEqualityComparer.Default.Equals(baseType, drawableType))
                return true;

            if (gameType is not null && SymbolEqualityComparer.Default.Equals(baseType, gameType))
                return true;
        }

        return false;
    }

    private static bool validateCachedMember(
        ISymbol memberSymbol,
        SyntaxNode targetNode,
        ImmutableArray<DiagnosticData>.Builder diagnostics,
        out ITypeSymbol memberType)
    {
        var location = getTargetLocation(targetNode, memberSymbol);

        if (memberSymbol.IsStatic)
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidCachedTarget,
                location,
                memberSymbol.Name,
                "member must not be static"));
            memberType = null!;
            return false;
        }

        if (memberSymbol is IFieldSymbol field)
        {
            if (field.RefKind != RefKind.None)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidCachedTarget,
                    location,
                    memberSymbol.Name,
                    "ref fields are not supported"));
                memberType = null!;
                return false;
            }

            memberType = field.Type;
        }
        else if (memberSymbol is IPropertySymbol prop)
        {
            if (prop.IsIndexer)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidCachedTarget,
                    location,
                    memberSymbol.Name,
                    "indexers are not supported"));
                memberType = null!;
                return false;
            }

            if (prop.RefKind != RefKind.None)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidCachedTarget,
                    location,
                    memberSymbol.Name,
                    "ref properties are not supported"));
                memberType = null!;
                return false;
            }

            if (prop.GetMethod is null)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidCachedTarget,
                    location,
                    memberSymbol.Name,
                    "property must have a getter"));
                memberType = null!;
                return false;
            }

            memberType = prop.Type;
        }
        else
        {
            memberType = null!;
            return false;
        }

        if (memberType.TypeKind == TypeKind.Error)
            return false;

        if (!isSupportedDependencyType(memberType))
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidCachedTarget,
                location,
                memberSymbol.Name,
                $"member type '{memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not a valid dependency type"));
            return false;
        }

        return true;
    }

    private static bool validateResolvedMember(
        ISymbol memberSymbol,
        SyntaxNode targetNode,
        ImmutableArray<DiagnosticData>.Builder diagnostics,
        out ITypeSymbol memberType)
    {
        var location = getTargetLocation(targetNode, memberSymbol);

        if (memberSymbol.IsStatic)
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidResolvedTarget,
                location,
                memberSymbol.Name,
                "member must not be static"));
            memberType = null!;
            return false;
        }

        if (memberSymbol is IFieldSymbol field)
        {
            if (field.IsReadOnly)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidResolvedTarget,
                    location,
                    memberSymbol.Name,
                    "field must not be readonly"));
                memberType = null!;
                return false;
            }

            if (field.RefKind != RefKind.None)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidResolvedTarget,
                    location,
                    memberSymbol.Name,
                    "ref fields are not supported"));
                memberType = null!;
                return false;
            }

            memberType = field.Type;
        }
        else if (memberSymbol is IPropertySymbol prop)
        {
            if (prop.IsIndexer)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidResolvedTarget,
                    location,
                    memberSymbol.Name,
                    "indexers are not supported"));
                memberType = null!;
                return false;
            }

            if (prop.RefKind != RefKind.None)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidResolvedTarget,
                    location,
                    memberSymbol.Name,
                    "ref properties are not supported"));
                memberType = null!;
                return false;
            }

            if (prop.SetMethod is null || prop.SetMethod.IsInitOnly)
            {
                diagnostics.Add(DiagnosticData.Create(
                    DiagnosticRule.InvalidResolvedTarget,
                    location,
                    memberSymbol.Name,
                    "property must have a mutable setter (not init-only or read-only)"));
                memberType = null!;
                return false;
            }

            memberType = prop.Type;
        }
        else
        {
            memberType = null!;
            return false;
        }

        if (memberType.TypeKind == TypeKind.Error)
            return false;

        if (!isSupportedDependencyType(memberType))
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidResolvedTarget,
                location,
                memberSymbol.Name,
                $"member type '{memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not a valid dependency type"));
            return false;
        }

        return true;
    }

    private static bool tryResolveCachedKeyType(
        ISymbol targetSymbol,
        ITypeSymbol sourceType,
        AttributeData attribute,
        Compilation compilation,
        Location? diagnosticLocation,
        ImmutableArray<DiagnosticData>.Builder diagnostics,
        out ITypeSymbol keyType)
    {
        if (attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].IsNull)
        {
            keyType = sourceType;
            return true;
        }

        if (attribute.ConstructorArguments[0].Value is not ITypeSymbol asType)
        {
            keyType = sourceType;
            return false;
        }

        if (asType.TypeKind == TypeKind.Error)
        {
            keyType = sourceType;
            return false;
        }

        if (asType is INamedTypeSymbol { IsUnboundGenericType: true }
            || !isSupportedDependencyType(asType)
            || unwrapNullableValueType(asType, out _))
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidCachedAsType,
                diagnosticLocation,
                targetSymbol.Name,
                asType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                $"'{asType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not a valid dependency key type"));
            keyType = sourceType;
            return false;
        }

        var normalizedAsType = stripTopLevelNullable(asType);
        var normalizedSourceType = stripTopLevelNullable(sourceType);
        var conversion = compilation.ClassifyConversion(normalizedSourceType, normalizedAsType);

        if (!conversion.Exists || !conversion.IsImplicit || (!conversion.IsIdentity && !conversion.IsReference && !conversion.IsBoxing))
        {
            diagnostics.Add(DiagnosticData.Create(
                DiagnosticRule.InvalidCachedAsType,
                diagnosticLocation,
                targetSymbol.Name,
                asType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                $"'{normalizedSourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not assignable to '{asType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"));
            keyType = sourceType;
            return false;
        }

        keyType = normalizedAsType;
        return true;
    }

    private static bool isSupportedDependencyType(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            return false;

        if (type.IsRefLikeType)
            return false;

        return type.SpecialType != SpecialType.System_Void;
    }

    private static bool unwrapNullableValueType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullableNamed)
        {
            underlyingType = nullableNamed.TypeArguments[0];
            return true;
        }

        underlyingType = type;
        return false;
    }

    private static ITypeSymbol stripTopLevelNullable(ITypeSymbol type)
        => type.NullableAnnotation == NullableAnnotation.Annotated
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;

    private static bool isPotentiallyNullableTypeParameter(ITypeSymbol type)
    {
        if (type is not ITypeParameterSymbol tp)
            return false;

        if (tp.IsValueType || tp.HasNotNullConstraint)
            return false;

        return !(tp.HasReferenceTypeConstraint && tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.NotAnnotated);
    }

    private static string formatType(ITypeSymbol type)
        => type.ToDisplayString(fully_qualified_format);

    private static EquatableArray<EquatableString> extractTypeParameters(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return EquatableArray<EquatableString>.EMPTY;

        var builder = ImmutableArray.CreateBuilder<EquatableString>(typeSymbol.TypeParameters.Length);

        foreach (var tp in typeSymbol.TypeParameters)
            builder.Add(new EquatableString(tp.Name));

        return new EquatableArray<EquatableString>(builder.MoveToImmutable());
    }

    private static string getTypeKeyword(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.IsRecord)
            return typeSymbol.TypeKind == TypeKind.Struct ? "record struct" : "record";

        return typeSymbol.TypeKind switch
        {
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class",
        };
    }

    private static string buildHintName(INamedTypeSymbol typeSymbol)
    {
        var parts = new Stack<string>();

        for (var current = typeSymbol; current is not null; current = current.ContainingType)
            parts.Push(current.MetadataName);

        var sb = new StringBuilder();

        if (typeSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            sb.Append(ns.ToDisplayString());
            sb.Append('.');
        }

        while (parts.Count > 0)
        {
            sb.Append(parts.Pop());

            if (parts.Count > 0)
                sb.Append('.');
        }

        sb.Replace('`', '_');
        sb.Replace('<', '_');
        sb.Replace('>', '_');
        sb.Append(".DependencyInjection.g.cs");
        return sb.ToString();
    }

    private static Location? getTypeIdentifierLocation(INamedTypeSymbol typeSymbol, CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(cancellationToken) is TypeDeclarationSyntax typeDecl)
                return typeDecl.Identifier.GetLocation();
        }

        return typeSymbol.Locations.FirstOrDefault();
    }

    private static Location? getTargetLocation(SyntaxNode targetNode, ISymbol symbol)
    {
        return targetNode switch
        {
            TypeDeclarationSyntax typeDecl => typeDecl.Identifier.GetLocation(),
            VariableDeclaratorSyntax declarator => declarator.Identifier.GetLocation(),
            PropertyDeclarationSyntax propertyDecl => propertyDecl.Identifier.GetLocation(),
            _ => symbol.Locations.FirstOrDefault(),
        };
    }

    private static Location? getAttributeOrTargetLocation(AttributeData attr, SyntaxNode targetNode, ISymbol symbol)
        => attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? getTargetLocation(targetNode, symbol);

    private static void emitAll(
        SourceProductionContext spc,
        ImmutableArray<TargetExtractionItem> cachedItems,
        ImmutableArray<TargetExtractionItem> resolvedItems)
    {
        if (cachedItems.IsDefaultOrEmpty && resolvedItems.IsDefaultOrEmpty)
            return;

        var grouped = new SortedDictionary<string, TypeAggregate>(StringComparer.Ordinal);

        void addItems(ImmutableArray<TargetExtractionItem> items)
        {
            if (items.IsDefaultOrEmpty)
                return;

            foreach (var item in items)
            {
                if (!grouped.TryGetValue(item.Header.HintName, out var aggregate))
                {
                    aggregate = new TypeAggregate(item.Header);
                    grouped.Add(item.Header.HintName, aggregate);
                }

                aggregate.Cached.AddRange(item.CachedItems);
                aggregate.Resolved.AddRange(item.ResolvedItems);
                aggregate.MemberDiagnostics.AddRange(item.MemberDiagnostics);
            }
        }

        addItems(cachedItems);
        addItems(resolvedItems);

        foreach (var aggregate in grouped.Values)
        {
            foreach (var diag in aggregate.Header.TypeDiagnostics)
                spc.ReportDiagnostic(diag.ToDiagnostic());

            foreach (var diag in aggregate.MemberDiagnostics)
                spc.ReportDiagnostic(diag.ToDiagnostic());

            if (!aggregate.Header.CanGenerate)
                continue;

            if (aggregate.Cached.Count == 0 && aggregate.Resolved.Count == 0)
                continue;

            var source = renderSource(aggregate);
            spc.AddSource(aggregate.Header.HintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string renderSource(TypeAggregate aggregate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indent = 0;

        if (aggregate.Header.Namespace is not null)
        {
            sb.Append("namespace ").AppendLine(aggregate.Header.Namespace);
            sb.AppendLine("{");
            indent++;
        }

        foreach (var enclosing in aggregate.Header.EnclosingTypes)
        {
            appendIndent(sb, indent);
            sb.Append("partial ").Append(enclosing.Keyword).Append(' ').Append(enclosing.Name);
            appendTypeParameters(sb, enclosing.TypeParameters);
            sb.AppendLine();
            appendIndent(sb, indent);
            sb.AppendLine("{");
            indent++;
        }

        appendIndent(sb, indent);
        sb.Append("partial ").Append(aggregate.Header.TypeKeyword).Append(' ').Append(aggregate.Header.TypeName);
        appendTypeParameters(sb, aggregate.Header.TypeParameters);
        sb.AppendLine();
        appendIndent(sb, indent);
        sb.AppendLine("{");
        indent++;

        if (aggregate.Cached.Count > 0)
        {
            aggregate.Cached.Sort(static (a, b) =>
            {
                var c = a.Order.CompareTo(b.Order);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.MemberName, b.MemberName);
                return c != 0 ? c : string.CompareOrdinal(a.KeyTypeName, b.KeyTypeName);
            });

            appendIndent(sb, indent);
            sb.AppendLine("protected override void CacheDependencies(global::kajarlabs.Framework.DependencyInjection.DependencyCollection dependencies)");
            appendIndent(sb, indent);
            sb.AppendLine("{");
            indent++;

            appendIndent(sb, indent);
            sb.AppendLine("base.CacheDependencies(dependencies);");

            foreach (var cached in aggregate.Cached)
            {
                if (cached.IsSelf)
                {
                    appendIndent(sb, indent);
                    sb.Append("dependencies.Cache<").Append(cached.KeyTypeName).AppendLine(">(this);");
                }
                else if (cached.MemberIsNullableValueType)
                {
                    appendIndent(sb, indent);
                    if (cached.UseCacheAs)
                    {
                        sb.Append("dependencies.CacheAs(typeof(")
                          .Append(cached.KeyTypeName)
                          .Append("), this.")
                          .Append(cached.MemberName)
                          .Append(" ?? throw new global::System.ArgumentNullException(nameof(this.")
                          .Append(cached.MemberName)
                          .AppendLine(")));");
                    }
                    else
                    {
                        sb.Append("dependencies.Cache<")
                          .Append(cached.KeyTypeName)
                          .Append(">(this.")
                          .Append(cached.MemberName)
                          .Append(" ?? throw new global::System.ArgumentNullException(nameof(this.")
                          .Append(cached.MemberName)
                          .AppendLine(")));");
                    }
                }
                else
                {
                    var memberAccess = cached.MemberIsNullableReference
                        ? $"this.{cached.MemberName}!"
                        : $"this.{cached.MemberName}";

                    appendIndent(sb, indent);
                    if (cached.UseCacheAs)
                    {
                        sb.Append("dependencies.CacheAs(typeof(")
                          .Append(cached.KeyTypeName)
                          .Append("), ")
                          .Append(memberAccess)
                          .AppendLine(");");
                    }
                    else
                    {
                        sb.Append("dependencies.Cache<")
                          .Append(cached.KeyTypeName)
                          .Append(">(")
                          .Append(memberAccess)
                          .AppendLine(");");
                    }
                }
            }

            indent--;
            appendIndent(sb, indent);
            sb.AppendLine("}");
        }

        if (aggregate.Cached.Count > 0 && aggregate.Resolved.Count > 0)
            sb.AppendLine();

        if (aggregate.Resolved.Count > 0)
        {
            aggregate.Resolved.Sort(static (a, b) =>
            {
                var c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.MemberName, b.MemberName);
            });

            appendIndent(sb, indent);
            sb.AppendLine("protected override void ResolveDependencies(global::kajarlabs.Framework.DependencyInjection.IReadOnlyDependencyCollection dependencies)");
            appendIndent(sb, indent);
            sb.AppendLine("{");
            indent++;

            appendIndent(sb, indent);
            sb.AppendLine("base.ResolveDependencies(dependencies);");

            var localIndex = 0;

            foreach (var resolved in aggregate.Resolved)
            {
                appendIndent(sb, indent);

                if (!resolved.IsOptional)
                {
                    sb.Append("this.")
                      .Append(resolved.MemberName)
                      .Append(" = dependencies.Get<")
                      .Append(resolved.KeyTypeName)
                      .AppendLine(">();");
                }
                else
                {
                    var localVar = $"__kajarResolved{localIndex++}";

                    if (resolved.MemberIsNullableValueType)
                    {
                        sb.Append("this.")
                          .Append(resolved.MemberName)
                          .Append(" = dependencies.TryGet<")
                          .Append(resolved.KeyTypeName)
                          .Append(">(out var ")
                          .Append(localVar)
                          .Append(") ? (")
                          .Append(resolved.KeyTypeName)
                          .Append("?)")
                          .Append(localVar)
                          .AppendLine(" : null;");
                    }
                    else if (resolved.MemberIsAnnotatedNullable)
                    {
                        sb.Append("this.")
                          .Append(resolved.MemberName)
                          .Append(" = dependencies.TryGet<")
                          .Append(resolved.KeyTypeName)
                          .Append(">(out var ")
                          .Append(localVar)
                          .Append(") ? ")
                          .Append(localVar)
                          .AppendLine(" : null;");
                    }
                    else
                    {
                        sb.Append("this.")
                          .Append(resolved.MemberName)
                          .Append(" = dependencies.TryGet<")
                          .Append(resolved.KeyTypeName)
                          .Append(">(out var ")
                          .Append(localVar)
                          .Append(") ? ")
                          .Append(localVar)
                          .AppendLine(" : default!;");
                    }
                }
            }

            indent--;
            appendIndent(sb, indent);
            sb.AppendLine("}");
        }

        while (indent > 0)
        {
            indent--;
            appendIndent(sb, indent);
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void appendTypeParameters(StringBuilder sb, EquatableArray<EquatableString> typeParameters)
    {
        if (typeParameters.Count == 0)
            return;

        sb.Append('<');

        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            sb.Append(typeParameters[i].Value);
        }

        sb.Append('>');
    }

    private static void appendIndent(StringBuilder sb, int indent)
    {
        for (var i = 0; i < indent; i++)
            sb.Append("    ");
    }

    private sealed class TypeAggregate
    {
        public TypeAggregate(TypeHeaderModel header)
        {
            Header = header;
        }

        public TypeHeaderModel Header { get; }

        public List<CachedItemModel> Cached { get; } = new();

        public List<ResolvedItemModel> Resolved { get; } = new();

        public List<DiagnosticData> MemberDiagnostics { get; } = new();
    }
}
