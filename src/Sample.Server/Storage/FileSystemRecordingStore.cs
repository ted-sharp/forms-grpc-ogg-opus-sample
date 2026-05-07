namespace Sample.Server.Storage;

public class FileSystemRecordingStore : IRecordingStore
{
    private readonly string _filePath;

    public FileSystemRecordingStore(IConfiguration configuration, IHostEnvironment env)
    {
        var dir = configuration["Recording:Directory"]
            ?? Path.Combine(env.ContentRootPath, "recordings");
        Directory.CreateDirectory(dir);

        var fileName = configuration["Recording:FileName"] ?? "recording.opus";
        _filePath = Path.Combine(dir, fileName);
    }

    public string SavedPath => _filePath;

    public Stream OpenWrite()
    {
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
