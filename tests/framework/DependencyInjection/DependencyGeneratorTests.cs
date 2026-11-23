using System.Collections.Immutable;
using System.Globalization;
using kajarlabs.Framework.CodeAnalysis;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Scene;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace kajarlabs.Framework.Tests.DependencyInjection;

public sealed class DependencyGeneratorTests
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> base_references = new(createBaseReferences);

    [Fact]
    public void CachedAttributeOnClassAndMembersGeneratesCacheDependenciesAndCompilesCleanly()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace SampleApp;

            public interface IAudioService
            {
            }

            public interface IMetricsService
            {
            }

            public sealed class AudioService : IAudioService, IMetricsService
            {
            }

            public sealed class ConfigData
            {
            }

            [Cached]
            [Cached(typeof(IMetricsService))]
            public partial class ServiceHostDrawable : Drawable, IMetricsService
            {
                [Cached]
                private readonly ConfigData config = new();

                [Cached(typeof(IAudioService))]
                private AudioService Audio { get; } = new();

                [Cached]
                private ConfigData? NullableConfig => config;
            }
            """;

        var result = runGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);

        var (_, generatedText) = Assert.Single(result.GeneratedSources);
        Assert.Contains("dependencies.Cache<global::SampleApp.ServiceHostDrawable>(this);", generatedText);
        Assert.Contains("dependencies.Cache<global::SampleApp.IMetricsService>(this);", generatedText);
        Assert.Contains("dependencies.Cache<global::SampleApp.ConfigData>(this.config);", generatedText);
        Assert.Contains("dependencies.Cache<global::SampleApp.IAudioService>(this.Audio);", generatedText);
        Assert.Contains("dependencies.Cache<global::SampleApp.ConfigData>(this.NullableConfig!);", generatedText);
    }

    [Fact]
    public void ResolvedAttributeHonorsNullabilityAndCanBeNullOverridesWithoutWarnings()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace SampleApp;

            public interface IRendererService
            {
            }

            public interface IAudioService
            {
            }

            public partial class ConsumerDrawable : Drawable
            {
                [Resolved]
                private IRendererService renderer = null!;

                [Resolved]
                private IAudioService? optionalAudio;

                [Resolved(CanBeNull = false)]
                private IAudioService? requiredAudio;

                [Resolved(CanBeNull = true)]
                private IRendererService explicitOptionalRenderer = null!;

                [Resolved]
                private int? optionalCount;

                [Resolved(CanBeNull = false)]
                private int? requiredCount;

                public (IRendererService, IAudioService?, IAudioService?, IRendererService, int?, int?) ReadAll()
                    => (renderer, optionalAudio, requiredAudio, explicitOptionalRenderer, optionalCount, requiredCount);
            }
            """;

        var result = runGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);

        var (_, generatedText) = Assert.Single(result.GeneratedSources);
        Assert.Contains("this.renderer = dependencies.Get<global::SampleApp.IRendererService>();", generatedText);
        Assert.Contains("this.optionalAudio = dependencies.TryGet<global::SampleApp.IAudioService>(out var __kajarResolved0) ? __kajarResolved0 : null;", generatedText);
        Assert.Contains("this.requiredAudio = dependencies.Get<global::SampleApp.IAudioService>();", generatedText);
        Assert.Contains("this.explicitOptionalRenderer = dependencies.TryGet<global::SampleApp.IRendererService>(out var __kajarResolved1) ? __kajarResolved1 : default!;", generatedText);
        Assert.Contains("this.optionalCount = dependencies.TryGet<int>(out var __kajarResolved2) ? (int?)__kajarResolved2 : null;", generatedText);
        Assert.Contains("this.requiredCount = dependencies.Get<int>();", generatedText);
    }

    [Fact]
    public void InheritanceChainAcrossSeparateAssembliesCompilesAndChainsBaseHooks()
    {
        const string base_assembly_source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace BaseLib;

            public sealed class BaseToken
            {
            }

            public interface ISharedService
            {
            }

            public partial class BasePanel : Container
            {
                [Cached]
                private readonly BaseToken token = new();

                [Resolved]
                private ISharedService shared = null!;

                public ISharedService Shared => shared;
            }
            """;

        var baseResult = runGenerator(base_assembly_source, assemblyName: "BaseLib");
        Assert.Empty(baseResult.GeneratorDiagnostics);
        Assert.Empty(baseResult.CompilationDiagnostics);

        const string derived_assembly_source = """
            using BaseLib;
            using kajarlabs.Framework.DependencyInjection;

            namespace AppLib;

            public sealed class DerivedToken
            {
            }

            public partial class DerivedPanel : BasePanel
            {
                [Cached]
                private readonly DerivedToken derivedToken = new();

                [Resolved]
                private BaseToken resolvedBaseToken = null!;

                public BaseToken ResolvedBaseToken => resolvedBaseToken;
            }
            """;

        var derivedResult = runGenerator(
            derived_assembly_source,
            assemblyName: "AppLib",
            extraReferences: [baseResult.OutputCompilation.ToMetadataReference()]);

        Assert.Empty(derivedResult.GeneratorDiagnostics);
        Assert.Empty(derivedResult.CompilationDiagnostics);

        var (_, generatedText) = Assert.Single(derivedResult.GeneratedSources);
        Assert.Contains("base.CacheDependencies(dependencies);", generatedText);
        Assert.Contains("base.ResolveDependencies(dependencies);", generatedText);
        Assert.Contains("dependencies.Cache<global::AppLib.DerivedToken>(this.derivedToken);", generatedText);
        Assert.Contains("this.resolvedBaseToken = dependencies.Get<global::BaseLib.BaseToken>();", generatedText);
    }

    [Fact]
    public void NestedAndGenericPartialTypesGenerateValidNestedDeclarations()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace NestedApp;

            public partial class OuterHost<TOuter>
            {
                public partial class InnerDrawable<TInner> : Drawable
                {
                    [Cached]
                    private TInner cachedItem;

                    [Resolved]
                    private TOuter? resolvedOuter;

                    public InnerDrawable(TInner cachedItem)
                    {
                        this.cachedItem = cachedItem;
                    }

                    public TOuter? ResolvedOuter => resolvedOuter;
                }
            }
            """;

        var result = runGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);

        var (_, generatedText) = Assert.Single(result.GeneratedSources);
        Assert.Contains("partial class OuterHost<TOuter>", generatedText);
        Assert.Contains("partial class InnerDrawable<TInner>", generatedText);
        Assert.Contains("dependencies.CacheAs(typeof(TInner), this.cachedItem!);", generatedText);
        Assert.Contains("this.resolvedOuter = dependencies.TryGet<TOuter>(out var __kajarResolved0) ? __kajarResolved0 : default!;", generatedText);
    }

    [Fact]
    public void MissingPartialOnTargetOrEnclosingTypeEmitsKjdi001()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace InvalidPartial;

            public class NonPartialDrawable : Drawable
            {
                [Resolved]
                private string? label;
            }

            public class NonPartialOuter
            {
                public partial class PartialInner : Drawable
                {
                    [Cached]
                    private readonly string token = "x";
                }
            }
            """;

        var result = runGenerator(source);

        Assert.Equal(2, result.GeneratorDiagnostics.Count(d => d.Id == "KJDI001"));
        Assert.All(result.GeneratorDiagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void InvalidResolvedTargetsEmitKjdi002()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace InvalidResolved;

            public partial class BadResolvedDrawable : Drawable
            {
                [Resolved]
                private readonly string readonlyField = "";

                [Resolved]
                private static string? staticField;

                [Resolved]
                private string GetOnlyProp { get; } = "";

                [Resolved]
                private string InitOnlyProp { get; init; } = "";

                [Resolved]
                private static string? StaticProp { get; set; }
            }
            """;

        var result = runGenerator(source);

        Assert.Equal(5, result.GeneratorDiagnostics.Count(d => d.Id == "KJDI002"));
        Assert.All(result.GeneratorDiagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void InvalidCachedTargetsEmitKjdi003()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace InvalidCached;

            public partial class BadCachedDrawable : Drawable
            {
                [Cached]
                private static readonly string staticField = "s";

                [Cached]
                private static string StaticProp => "s";

                [Cached]
                private string WriteOnlyProp { set { } }
            }
            """;

        var result = runGenerator(source);

        Assert.Equal(3, result.GeneratorDiagnostics.Count(d => d.Id == "KJDI003"));
        Assert.All(result.GeneratorDiagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void InvalidCachedAsTypeEmitsKjdi004()
    {
        const string source = """
            using System.Collections.Generic;
            using kajarlabs.Framework.DependencyInjection;
            using kajarlabs.Framework.Scene;

            namespace InvalidAsType;

            public interface IService
            {
            }

            public sealed class Unrelated
            {
            }

            [Cached(typeof(IService))]
            public partial class BadAsTypeDrawable : Drawable
            {
                [Cached(typeof(IService))]
                private readonly Unrelated unrelated = new();

                [Cached(typeof(IList<>))]
                private readonly List<int> list = new();

                [Cached(typeof(int?))]
                private readonly int number = 1;
            }
            """;

        var result = runGenerator(source);

        var kjdi004 = result.GeneratorDiagnostics.Where(d => d.Id == "KJDI004").Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(4, kjdi004.Length);
        Assert.Contains(kjdi004, static m => m.Contains("'BadAsTypeDrawable'", StringComparison.Ordinal));
        Assert.Contains(kjdi004, static m => m.Contains("'unrelated'", StringComparison.Ordinal));
        Assert.Contains(kjdi004, static m => m.Contains("'list'", StringComparison.Ordinal));
        Assert.Contains(kjdi004, static m => m.Contains("'number'", StringComparison.Ordinal));
        Assert.All(result.GeneratorDiagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void NonDrawableNonGameTypeEmitsKjdi005()
    {
        const string source = """
            using kajarlabs.Framework.DependencyInjection;

            namespace InvalidBase;

            [Cached]
            public partial class PlainService
            {
                [Resolved]
                private string? value;
            }
            """;

        var result = runGenerator(source);

        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("KJDI005", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Empty(result.GeneratedSources);
    }

    private static GeneratorTestResult runGenerator(
        string source,
        string assemblyName = "GeneratorTestAssembly",
        MetadataReference[]? extraReferences = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = extraReferences is { Length: > 0 }
            ? base_references.Value.AddRange(extraReferences)
            : base_references.Value;

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));

        var generator = new DependencyInjectionGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        var generatedSources = runResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .Select(static s => (s.HintName, s.SourceText.ToString()))
            .ToImmutableArray();

        return new GeneratorTestResult(outputCompilation, generatorDiagnostics, outputCompilation.GetDiagnostics(), generatedSources);
    }

    private static ImmutableArray<MetadataReference> createBaseReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(path))
                    builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        var frameworkLocation = typeof(Drawable).Assembly.Location;
        if (!string.IsNullOrEmpty(frameworkLocation) && seen.Add(frameworkLocation))
            builder.Add(MetadataReference.CreateFromFile(frameworkLocation));

        return builder.ToImmutable();
    }

    private sealed record GeneratorTestResult(
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationDiagnostics,
        ImmutableArray<(string HintName, string Text)> GeneratedSources);
}
