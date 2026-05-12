using System.IO;

namespace Sample.Client.Streaming.Audio;

/// <summary>
/// 書き込まれたバイト列を内部バッファに溜め、しきい値を超えたら同期的に送信デリゲートを呼ぶ Stream。
/// <para>
/// 背景: <c>OpusOggWriteStream.WriteSamples</c> は最終的に <see cref="Stream.Write(Byte[], Int32, Int32)"/> を
/// 同期的に呼んでくるが、gRPC の <c>RequestStream.WriteAsync</c> は非同期 API しかない。両者を繋ぐため
/// 「同期 Write → デリゲートで非同期 WriteAsync を <c>GetAwaiter().GetResult()</c> で同期待ち」する。
/// 録音停止時の Finish の中で Flush が起きるため、シーケンシャルに送信完了するこの形が安全。
/// </para>
/// <para>
/// バックグラウンド・ポンプ・タスク方式 (キュー + 別タスクで非同期送信) は、停止時の
/// Finish → CompleteAsync → ResponseAsync の順序がポンプの await 中のキャンセル例外と噛み合って
/// 「The client reset the request stream」になることがあったため避けている。
/// </para>
/// </summary>
public sealed class ChunkForwardStream : Stream
{
    private readonly Func<byte[], Task> _sendAsync;
    private readonly int _flushThreshold;
    private readonly MemoryStream _buffer = new();
    private readonly Lock _lock = new();
    private volatile bool _closed;

    public ChunkForwardStream(Func<byte[], Task> sendAsync, int flushThreshold = 32 * 1024)
    {
        this._sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        this._flushThreshold = flushThreshold;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (this._closed) return;
        byte[]? toSend = null;
        lock (this._lock)
        {
            if (this._closed) return;
            this._buffer.Write(buffer, offset, count);
            if (this._buffer.Length >= this._flushThreshold)
            {
                toSend = this._buffer.ToArray();
                this._buffer.SetLength(0);
            }
        }
        if (toSend != null)
        {
            this._sendAsync(toSend).GetAwaiter().GetResult();
        }
    }

    public override void Flush()
    {
        if (this._closed) return;
        byte[]? toSend = null;
        lock (this._lock)
        {
            if (this._closed) return;
            if (this._buffer.Length > 0)
            {
                toSend = this._buffer.ToArray();
                this._buffer.SetLength(0);
            }
        }
        if (toSend != null)
        {
            this._sendAsync(toSend).GetAwaiter().GetResult();
        }
    }

    /// <summary>残バッファをまとめて 1 回だけ送信する。Stream の Close ではないので
    /// 呼び出し後も再使用しないこと。</summary>
    public Task CompleteAsync()
    {
        this.Flush();
        return Task.CompletedTask;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 注意: Concentus.Oggfile の OpusOggWriteStream.Finish() が
            // 内部で _outputStream.Close() を呼んでくるため、ここを通る経路がある。
            // _buffer をその時点で Dispose してしまうと、後続のコード (StreamingRecorder の
            // OnRecordingStopped) でこの Stream を触ったときに ObjectDisposedException が出て
            // gRPC 呼び出しが中断され、サーバーで「client reset the request stream」と観測される。
            // クローズ済みフラグだけ立てて、_buffer の解放は GC に任せる。
            this._closed = true;
        }
        base.Dispose(disposing);
    }
}
