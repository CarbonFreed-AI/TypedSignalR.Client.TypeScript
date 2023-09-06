using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tapper;

namespace TypedSignalR.Client.TypeScript.SourceLinking;

///-------------------------------------------------------------------------------------------------
/// <summary>
///     A source link provider which analyzes externally generated source files and links them
///     into the generated files. This class cannot be inherited.
/// </summary>
///-------------------------------------------------------------------------------------------------
internal sealed class SourceLinkProvider : ISourceLinkProvider
{
    private static readonly Regex LooseModelRegex = new(@"export (?:interface|enum) (\w+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex IndexModelRegex = new(@"export { ((?:(?:\w|\d)+(?:, )?)+) }", RegexOptions.Compiled | RegexOptions.Multiline);
    private readonly Dictionary<string, string[]> _sourceLinkMap;
    private readonly string _sourcePath;
    private readonly NamingStyle _dtoNamingStyle;

    public SourceLinkProvider()
    {
        _sourceLinkMap = new();
        _sourcePath = string.Empty;
    }

    private SourceLinkProvider(Dictionary<string, string[]> map, string sourcePath, NamingStyle dtoNamingStyle)
    {
        _sourceLinkMap = map;
        _sourcePath = sourcePath;
        _dtoNamingStyle = dtoNamingStyle;
    }

    ///-------------------------------------------------------------------------------------------------
    /// <summary>   Resolves the provided source path and generates a link map. </summary>
    ///
    /// <param name="sourcePath">           Full pathname of the source file.   </param>
    /// <param name="dtoNamingStyle">       The data transfer object naming style.  </param>
    /// <param name="cancellationToken">    A token that allows processing to be cancelled. </param>
    ///
    /// <returns>   A SourceLinkProvider.   </returns>
    ///-------------------------------------------------------------------------------------------------
    public static async Task<SourceLinkProvider> Resolve(string? sourcePath, NamingStyle dtoNamingStyle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sourcePath)) return new();

        Dictionary<string, string[]> map = new();

        var files = Directory.GetFiles(sourcePath, "*.ts", SearchOption.TopDirectoryOnly);
        string? indexFile;
        if ((indexFile = files.FirstOrDefault(f => f.EndsWith("index.ts", StringComparison.InvariantCultureIgnoreCase))) != null)
        {
            // Read index.ts instead of all model files
            var content = await File.ReadAllTextAsync(indexFile, cancellationToken);

            var matches = IndexModelRegex.Matches(content);
            var key = Path.GetDirectoryName(indexFile)!;
            map[key] = matches.SelectMany(x => x.Groups[1].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
        }
        else
        {
            // Loose model classes get read separately
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var content = await File.ReadAllTextAsync(file, cancellationToken);

                var matches = LooseModelRegex.Matches(content);
                var key = Path.GetDirectoryName(file)!;
                var selector = matches.Select(x => x.Groups[1].Value);

                if (map.ContainsKey(key))
                    map[key] = map[key].Concat(selector).ToArray();
                else map[key] = selector.ToArray();
            }
        }

        return new(map, sourcePath, dtoNamingStyle);
    }

    ///-------------------------------------------------------------------------------------------------
    /// <summary>   Gets the source link (file name where the DTO type is defined in).  </summary>
    ///
    /// <param name="typeName">     Name of the type.   </param>
    /// <param name="relativePath"> .   </param>
    ///
    /// <returns>   The source link.    </returns>
    ///-------------------------------------------------------------------------------------------------
    public string? GetSourceLink(string typeName, string relativePath)
    {
        var transformedName = _dtoNamingStyle.Transform(typeName);
        var match = (from pair in _sourceLinkMap where pair.Value.Any(t => t.Equals(transformedName)) select pair.Key).FirstOrDefault();
        if (match == null) return null;

        return MakeRelative(Path.Combine(_sourcePath, match), relativePath).Replace("\\", "/");
    }

    public bool HasSourceLink(string typeName)
    {
        var transformedName = _dtoNamingStyle.Transform(typeName);
        return _sourceLinkMap.Any(pair => pair.Value.Any(t => t.Equals(transformedName)));
    }

    public static string MakeRelative(string fullPath, string baseDir)
    {
        string pathSep = "\\";
        string itemPath = Path.GetFullPath(fullPath);
        string baseDirPath = Path.GetFullPath(baseDir);

        string[] p1 = Regex.Split(itemPath, "[\\\\/]").Where(x => x.Length != 0).ToArray();
        string[] p2 = Regex.Split(baseDirPath, "[\\\\/]").Where(x => x.Length != 0).ToArray();
        int i = 0;

        for (; i < p1.Length && i < p2.Length; i++)
            if (string.Compare(p1[i], p2[i], StringComparison.OrdinalIgnoreCase) != 0) // Case insensitive match
                break;

        if (i == 0) // Cannot make relative path, for example if resides on different drive
            return itemPath;

        string r = string.Join(pathSep, Enumerable.Repeat("..", p2.Length - i).Concat(p1.Skip(i).Take(p1.Length - i)));
        return r;
    }
}
