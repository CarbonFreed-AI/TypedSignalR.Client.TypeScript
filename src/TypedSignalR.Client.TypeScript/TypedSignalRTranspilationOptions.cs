using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Tapper;
using TypedSignalR.Client.TypeScript.SourceLinking;

namespace TypedSignalR.Client.TypeScript;

public class TypedSignalRTranspilationOptions : TranspilationOptions, ITypedSignalRTranspilationOptions
{
    public MethodStyle MethodStyle { get; }
    public string OutputPath { get; }
    public ISourceLinkProvider SourceLinkProvider { get; }

    private TypedSignalRTranspilationOptions(
        string outputPath,
        Compilation compilation,
        ISourceLinkProvider sourceLinkProvider,
        ITypeMapperProvider typeMapperProvider,
        SerializerOption serializerOption,
        NamingStyle namingStyle,
        EnumStyle enumStyle,
        MethodStyle methodStyle,
        NewLineOption newLineOption,
        int indent,
        bool referencedAssembliesTranspilation,
        bool enableAttributeReference) : base(compilation,
            typeMapperProvider,
            serializerOption,
            namingStyle,
            enumStyle,
            newLineOption,
            indent,
            referencedAssembliesTranspilation,
            enableAttributeReference)
    {
        MethodStyle = methodStyle;
        OutputPath = outputPath;
        SourceLinkProvider = sourceLinkProvider;
    }

    public static async Task<TypedSignalRTranspilationOptions> Make(
        string outputPath,
        Compilation compilation,
        string? dtoSource,
        ITypeMapperProvider typeMapperProvider,
        SerializerOption serializerOption,
        NamingStyle externalDtoNamingStyle,
        NamingStyle namingStyle,
        EnumStyle enumStyle,
        MethodStyle methodStyle,
        NewLineOption newLineOption,
        int indent,
        bool referencedAssembliesTranspilation,
        bool enableAttributeReference, CancellationToken cancellationToken)
    {
        return new(outputPath, compilation, await SourceLinking.SourceLinkProvider.Resolve(dtoSource, externalDtoNamingStyle, cancellationToken), typeMapperProvider, serializerOption, namingStyle, enumStyle,
            methodStyle, newLineOption, indent, referencedAssembliesTranspilation, enableAttributeReference);
    }
}
