using Tapper;
using TypedSignalR.Client.TypeScript.SourceLinking;

namespace TypedSignalR.Client.TypeScript;

public interface ITypedSignalRTranspilationOptions : ITranspilationOptions
{
    MethodStyle MethodStyle { get; }

    string OutputPath { get; }

    ISourceLinkProvider SourceLinkProvider { get; }
}
