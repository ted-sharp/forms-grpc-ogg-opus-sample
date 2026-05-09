using MagicOnion;
using Sample.Shared.Dto;

namespace Sample.Server.Services;

/// <summary>
/// サーバー側 (MagicOnion v7) の録音サービス契約。
/// <para>
/// クライアント側にも同名の <c>IRecordingService</c> が <see cref="Sample.Shared"/> 名前空間に
/// <b>別個に</b> 存在する。両者は同じ DTO を扱うが、戻り値型のシグネチャだけが微妙に異なる
/// (v4 と v7 で <c>ClientStreamingResult&lt;,&gt;</c> 周りの属性が変わったため)。
/// </para>
/// <para>
/// なぜ DLL を共有せずソースを分けているか:
/// <list type="bullet">
///   <item><c>MagicOnion.Abstractions</c> v4 と v7 を同居させると <c>[AsyncMethodBuilder]</c> 解決が壊れる。</item>
///   <item>gRPC のメソッド解決は「型名 (=<c>IRecordingService</c>) + メソッド名」で行われるため、
///         名前空間が違っていても通信は成立する。</item>
/// </list>
/// </para>
/// </summary>
public interface IRecordingService : IService<IRecordingService>
{
    /// <summary>
    /// ClientStreaming のサーバー側ハンドラ。
    /// <para>
    /// MagicOnion v7 の <c>ClientStreamingResult&lt;,&gt;</c> は task-like ではないため、
    /// <c>async ClientStreamingResult&lt;,&gt;</c> という書き方ができない。
    /// したがって戻り値は必ず <c>Task&lt;ClientStreamingResult&lt;,&gt;&gt;</c> で包む。
    /// </para>
    /// </summary>
    Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming();

    UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

    UnaryResult<DownloadResult> Download(DownloadRequest request);
}
