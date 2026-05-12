using MagicOnion;
using Sample.Shared.Dto;

namespace Sample.Shared;

/// <summary>
/// クライアントとサーバー双方が共有する録音サービス契約。
/// MagicOnion v7 のシグネチャに統一しているため、両端を 1 つの DLL で参照できる。
/// </summary>
public interface IRecordingService : IService<IRecordingService>
{
    /// <summary>
    /// ClientStreaming で Ogg Opus のチャンクを連続送信し、最終的に保存結果を 1 つ受け取る。
    /// </summary>
    /// <remarks>
    /// 戻り値は <see cref="Task{TResult}"/> で <see cref="ClientStreamingResult{TRequest, TResponse}"/> を包む。
    /// クライアントでは <c>var ctx = await client.SaveStreaming();</c> で取得し、
    /// <c>ctx.RequestStream.WriteAsync</c> でチャンク送信、最後に <c>ctx.RequestStream.CompleteAsync</c> +
    /// <c>await ctx.ResponseAsync</c> でレスポンスを待つ。
    /// </remarks>
    Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming();

    /// <summary>
    /// Unary で Ogg Opus 全体を 1 メッセージにまとめて送信し、保存結果を受け取る。
    /// 短い録音や、ストリーミング不要なケース向け。
    /// </summary>
    UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

    /// <summary>
    /// サーバー側に保存された Ogg Opus ファイルをまるごとダウンロードする。
    /// <para>
    /// 引数 <paramref name="request"/> は現状未使用だが省略不可。
    /// MagicOnion / MessagePack の都合で「引数 0 個の Unary」は地味に踏みやすい地雷なので、
    /// 拡張余地も兼ねてダミー DTO を取らせている。詳細は <see cref="DownloadRequest"/> 参照。
    /// </para>
    /// </summary>
    UnaryResult<DownloadResult> Download(DownloadRequest request);
}
