namespace Sample.Server.Storage;

/// <summary>
/// 録音ファイルをローカルファイルシステムに保存する <see cref="IRecordingStore"/> 実装。
/// 単一ファイル方式で、保存パスはコンストラクタで決定して以降固定。
/// </summary>
public class FileSystemRecordingStore : IRecordingStore
{
    private readonly string _filePath;

    public FileSystemRecordingStore(IConfiguration configuration, IHostEnvironment env)
    {
        // 保存先ディレクトリ: appsettings.json の "Recording:Directory" を最優先、無ければ ContentRootPath/recordings。
        // CWD は Program.cs 冒頭で AppContext.BaseDirectory に固定済みなので、
        // 相対パスで指定された場合は bin\Debug\net10.0\ を起点に解決される。
        var dir = configuration["Recording:Directory"]
            ?? Path.Combine(env.ContentRootPath, "recordings");
        Directory.CreateDirectory(dir);

        // ファイル名: appsettings.json の "Recording:FileName" を最優先、無ければ "recording.opus" 固定。
        var fileName = configuration["Recording:FileName"] ?? "recording.opus";
        _filePath = Path.Combine(dir, fileName);
    }

    public string SavedPath => _filePath;

    public Stream OpenWrite()
    {
        // FileMode.Create = 既存ファイルがあれば毎回上書き。
        // このサンプルは履歴を持たない設計 (常に最新の 1 ファイルのみ保存)。
        return new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public async Task WriteAllAsync(byte[] bytes)
    {
        using var fs = OpenWrite();
        await fs.WriteAsync(bytes, 0, bytes.Length);
    }

    public async Task<byte[]?> ReadAllAsync()
    {
        if (!File.Exists(_filePath)) return null;
        return await File.ReadAllBytesAsync(_filePath);
    }
}
