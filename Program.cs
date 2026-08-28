using System.Net;
using System.Text;
using System.Text.Json.Serialization;

IPAddress? commandLineIp = null;
int? commandLinePort = null;
string? commandLineRoot = null;

if (args.Length is not 0 and not 3)
{
    Console.Error.WriteLine("用法: FileBrowser <IP> <端口> <目录>");
    Environment.ExitCode = 1;
    return;
}

if (args.Length == 3)
{
    if (!IPAddress.TryParse(args[0], out commandLineIp))
    {
        Console.Error.WriteLine($"IP 地址无效: {args[0]}");
        Environment.ExitCode = 1;
        return;
    }

    if (!int.TryParse(args[1], out var port) || port is < 1 or > 65535)
    {
        Console.Error.WriteLine($"端口号无效: {args[1]}（有效范围为 1-65535）");
        Environment.ExitCode = 1;
        return;
    }

    commandLinePort = port;
    commandLineRoot = args[2];
}

// Positional parameters are handled above so ASP.NET Core does not interpret them
// as configuration switches.
var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
if (commandLineIp is not null && commandLinePort is not null)
{
    var host = commandLineIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"[{commandLineIp}]"
        : commandLineIp.ToString();
    builder.Configuration["Kestrel:Endpoints:Http:Url"] = $"http://{host}:{commandLinePort}";
}
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
var app = builder.Build();

var root = Path.GetFullPath(commandLineRoot
    ?? Environment.GetEnvironmentVariable("FILE_BROWSER_ROOT")
    ?? builder.Configuration["FileBrowser:Root"]
    ?? Directory.GetCurrentDirectory());

if (!Directory.Exists(root))
    throw new DirectoryNotFoundException($"浏览目录不存在: {root}");

const long maxPreviewBytes = 2 * 1024 * 1024;
var previewExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".cs", ".csproj", ".sln", ".json", ".js", ".jsx", ".ts", ".tsx",
    ".html", ".htm", ".css", ".scss", ".md", ".txt", ".xml", ".yml",
    ".yaml", ".sql", ".sh", ".razor", ".cshtml", ".vue", ".env", ".gitignore"
};

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/files", (string? path) =>
{
    var resolved = Resolve(path);
    if (resolved is null) return Results.BadRequest(new ErrorResponse("路径无效。"));
    if (!Directory.Exists(resolved)) return Results.NotFound(new ErrorResponse("目录不存在。"));

    try
    {
        var directory = new DirectoryInfo(resolved);
        var items = directory.EnumerateFileSystemInfos()
            .Where(x => (x.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(x => new FileItem(
                x.Name,
                x is DirectoryInfo,
                x.LastWriteTimeUtc,
                x is FileInfo file ? file.Length : null,
                x is FileInfo f && IsPreviewable(f)))
            .OrderByDescending(x => x.LastModifiedUtc)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new DirectoryResult(NormalizeRelative(resolved), items));
    }
    catch (UnauthorizedAccessException)
    {
        return TypedResults.Json(new ErrorResponse("没有权限读取此目录。"),
            AppJsonContext.Default.ErrorResponse, statusCode: 403);
    }
});

app.MapGet("/api/preview", async (string? path) =>
{
    var resolved = Resolve(path);
    if (resolved is null) return Results.BadRequest(new ErrorResponse("路径无效。"));
    if (!File.Exists(resolved)) return Results.NotFound(new ErrorResponse("文件不存在。"));

    var file = new FileInfo(resolved);
    if (!IsPreviewable(file))
        return Results.BadRequest(new ErrorResponse($"暂不支持预览此文件，最大支持 {maxPreviewBytes / 1024 / 1024} MB 的文本文件。"));

    try
    {
        await using var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync();
        return Results.Ok(new PreviewResult(file.Name, NormalizeRelative(resolved), file.LastWriteTimeUtc, content));
    }
    catch (DecoderFallbackException)
    {
        return Results.BadRequest(new ErrorResponse("文件不是可识别的 UTF-8 文本。"));
    }
    catch (UnauthorizedAccessException)
    {
        return TypedResults.Json(new ErrorResponse("没有权限读取此文件。"),
            AppJsonContext.Default.ErrorResponse, statusCode: 403);
    }
});

app.MapFallbackToFile("index.html");
app.Run();

string? Resolve(string? relativePath)
{
    relativePath = (relativePath ?? "").Replace('/', Path.DirectorySeparatorChar);
    var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
    return candidate == root || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        ? candidate : null;
}

string NormalizeRelative(string fullPath) => Path.GetRelativePath(root, fullPath)
    .Replace(Path.DirectorySeparatorChar, '/') is "." ? "" : Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

bool IsPreviewable(FileInfo file) => file.Length <= maxPreviewBytes &&
    (previewExtensions.Contains(file.Extension) || previewExtensions.Contains(file.Name));

record FileItem(string Name, bool IsDirectory, DateTime LastModifiedUtc, long? Size, bool Previewable);
record DirectoryResult(string Path, FileItem[] Items);
record PreviewResult(string Name, string Path, DateTime LastModifiedUtc, string Content);
record ErrorResponse(string Error);

[JsonSerializable(typeof(FileItem))]
[JsonSerializable(typeof(FileItem[]))]
[JsonSerializable(typeof(DirectoryResult))]
[JsonSerializable(typeof(PreviewResult))]
[JsonSerializable(typeof(ErrorResponse))]
partial class AppJsonContext : JsonSerializerContext;
