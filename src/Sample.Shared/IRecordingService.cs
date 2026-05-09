using MagicOnion;
using Sample.Shared.Dto;

namespace Sample.Shared
{
    /// <summary>
    /// クライアントから見た録音サービスの契約 (MagicOnion v4 風シグネチャ)。
    /// <para>
    /// 注意: このサンプルでは <b>サーバー側にも同名の <c>IRecordingService</c> が
    /// <c>Sample.Server.Services</c> 名前空間に別途定義</c></b> されている。
    /// クライアント (v4) とサーバー (v7) でメソッドの戻り値型に微妙な差異 (例:
    /// <c>ClientStreamingResult&lt;,&gt;</c> の <c>[AsyncMethodBuilder]</c> 属性) があり、
    /// <c>Sample.Shared</c> を DLL としてサーバーから参照すると型解決が壊れるため、
    /// このファイルはクライアント側のみが利用する契約として置いている。
    /// gRPC のメソッドは「型名 + メソッド名」で解決されるので、名前空間が違っても疎通する。
    /// </para>
    /// </summary>
    public interface IRecordingService : IService<IRecordingService>
    {
        /// <summary>
        /// ClientStreaming で Ogg Opus のチャンクを連続送信し、最終的に保存結果を 1 つ受け取る。
        /// <para>
        /// MagicOnion v4 クライアントでは戻り値が <b>同期的に返る</b> 点に注意。
        /// <c>var stream = service.SaveStreaming();</c> のように代入で受けること。
        /// <c>await</c> すると <c>[AsyncMethodBuilder]</c> 経由で型が <c>RecordingResult</c> に
        /// すり替わってコンパイルが通らなくなる。
        /// </para>
        /// </summary>
        ClientStreamingResult<RecordingChunk, RecordingResult> SaveStreaming();

        /// <summary>
        /// Unary で Ogg Opus 全体を 1 メッセージにまとめて送信し、保存結果を受け取る。
        /// 短い録音や、ストリーミング不要なケース向け。
        /// </summary>
        UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

        /// <summary>
        /// サーバー側に保存された Ogg Opus ファイルをまるごとダウンロードする。
        /// <para>
        /// 引数 <paramref name="request"/> は現状未使用だが省略不可。
        /// MagicOnion v4 クライアントが「引数 0 個」のメソッドを送ると v7 サーバー側の
        /// MessagePack デシリアライズが失敗するため、ダミー DTO を必ず取らせている。
        /// 詳細は <see cref="DownloadRequest"/> 参照。
        /// </para>
        /// </summary>
        UnaryResult<DownloadResult> Download(DownloadRequest request);
    }
}
