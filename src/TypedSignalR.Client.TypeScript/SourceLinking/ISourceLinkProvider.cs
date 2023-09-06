using Tapper;

namespace TypedSignalR.Client.TypeScript.SourceLinking;

///-------------------------------------------------------------------------------------------------
/// <summary>
///     Interface for a provider which can help source linking (imports) from different sources.
///     Allows using external tools like Swagger Gen.
/// </summary>
///-------------------------------------------------------------------------------------------------
public interface ISourceLinkProvider
{
    ///-------------------------------------------------------------------------------------------------
    /// <summary>   Gets the source link (file name where the DTO type is defined in).  </summary>
    ///
    /// <param name="typeName">     Name of the type.   </param>
    /// <param name="relativePath"> .   </param>
    ///
    /// <returns>   The source link.    </returns>
    ///-------------------------------------------------------------------------------------------------
    string? GetSourceLink(string typeName, string relativePath);
}
