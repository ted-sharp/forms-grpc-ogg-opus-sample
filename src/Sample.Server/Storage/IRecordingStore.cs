namespace Sample.Server.Storage;

public interface IRecordingStore
{
    string SavedPath { get; }

    Stream OpenWrite();

    Task WriteAllAsync(byte[] bytes);

    Task<byte[]?> ReadAllAsync();
}
