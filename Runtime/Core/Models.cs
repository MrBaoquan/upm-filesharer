using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AndX
{
    /// <summary>素材类型。</summary>
    public enum MediaType
    {
        Image = 0,
        Video = 1,
    }

    /// <summary>
    /// 可上传载荷。实现需保证：长度可控、可重复读取（分片续传会从偏移处重新读取）。
    /// </summary>
    public interface IAndXPayload
    {
        /// <summary>文件名（含扩展名，用于服务端推断 content-type）。</summary>
        string FileName { get; }

        /// <summary>素材类型。</summary>
        MediaType MediaType { get; }

        /// <summary>总字节数（必须已知且 &gt;= 0）。</summary>
        long Length { get; }

        /// <summary>打开只读流，从头开始；调用方负责释放。</summary>
        Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>上传选项。定价不由端侧决定（由服务端/后台配置）。</summary>
    public sealed class UploadOptions
    {
        /// <summary>
        /// 展项 ID（可选）：经本机 AndXEdge 接入时留空，由 Edge / 服务端边缘配置提供；
        /// 直连后端或一个 Edge 服务多展项时显式传入。
        /// </summary>
        public string ExhibitId { get; set; }

        /// <summary>素材标题（可选）。</summary>
        public string Title { get; set; }

        /// <summary>覆盖载荷自带的素材类型（可选）。</summary>
        public MediaType? MediaType { get; set; }
    }

    /// <summary>上传进度（全局累计）。</summary>
    public sealed class UploadProgress
    {
        public UploadProgress(long sent, long total)
        {
            Sent = sent;
            Total = total;
        }

        /// <summary>已发送字节（含已完成分片与当前分片进度）。</summary>
        public long Sent { get; }

        /// <summary>总字节。</summary>
        public long Total { get; }

        /// <summary>完成比例 0..1。</summary>
        public double Percent
        {
            get { return Total > 0 ? (double)Sent / Total : 0d; }
        }
    }

    /// <summary>票据签发结果（Edge 控制面 / 设备端）。</summary>
    public sealed class IssuedTicket
    {
        public string Token { get; set; }
        public string Purpose { get; set; }

        /// <summary>普通链接二维码内容（未配置公网基址时为 null）。</summary>
        public string ShareUrl { get; set; }

        /// <summary>定价（分），0=免费。</summary>
        public long Amount { get; set; }

        /// <summary>过期时间（ISO8601），null=长期。</summary>
        public System.DateTimeOffset? ExpireAt { get; set; }

        /// <summary>二维码 PNG 绝对地址（本端按 Endpoint 拼好）。</summary>
        public string QrImageUrl { get; set; }
    }

    /// <summary>完成一次资源上传的结果。</summary>
    public sealed class ShareResult
    {
        public string ResourceId { get; set; }
        public string Token { get; set; }
        public string Purpose { get; set; }

        /// <summary>普通链接二维码内容（未配置公网基址时为 null）。</summary>
        public string ShareUrl { get; set; }

        /// <summary>定价（分），0=免费。</summary>
        public long Amount { get; set; }

        /// <summary>已接收字节。</summary>
        public long SizeBytes { get; set; }

        /// <summary>二维码 PNG 绝对地址。</summary>
        public string QrImageUrl { get; set; }
    }

    /// <summary>扫码解析结果（公开接口）。</summary>
    public sealed class ScanResolveResult
    {
        public string Purpose { get; set; }
        public string Token { get; set; }
        public string ShareUrl { get; set; }
        public ExhibitBrief Exhibit { get; set; }
        public MediaBrief Media { get; set; }

        /// <summary>预览地址（付费未购为低清/水印；无预览时 null）。</summary>
        public string PreviewUrl { get; set; }

        /// <summary>当前用户是否已具备下载权益（免费恒为 true）。</summary>
        public bool Entitled { get; set; }

        public sealed class ExhibitBrief
        {
            public string Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public string Subtitle { get; set; }
            public long Amount { get; set; }
        }

        public sealed class MediaBrief
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string Title { get; set; }
            public long? Duration { get; set; }
            public string SizeMb { get; set; }
            public long Amount { get; set; }
        }
    }

    /// <summary>下载授权结果。</summary>
    public sealed class DownloadResult
    {
        /// <summary>presigned 下载地址（时效见 <see cref="ExpiresIn"/>）。</summary>
        public string Url { get; set; }

        /// <summary>有效期（秒）。</summary>
        public long ExpiresIn { get; set; }

        public string Type { get; set; }
        public string Title { get; set; }
    }

    /// <summary>展项付费票据签发选项。</summary>
    public sealed class PayTicketOptions
    {
        /// <summary>
        /// 展项 ID（可选）：经本机 AndXEdge 接入时留空，由 Edge / 服务端边缘配置提供；
        /// 直连后端或一个 Edge 服务多展项时显式传入。
        /// </summary>
        public string ExhibitId { get; set; }
    }

    /// <summary>
    /// 展项付费票据。<see cref="OrderNo"/> 在签发阶段通常为 null——
    /// /p/ 展项付费的下单链路尚未打通（见 implementation.md §4.4 待后续增量）。
    /// </summary>
    public sealed class PayTicket
    {
        public string Token { get; set; }
        public string Purpose { get; set; }
        public string ShareUrl { get; set; }
        public long Amount { get; set; }
        public System.DateTimeOffset? ExpireAt { get; set; }
        public string QrImageUrl { get; set; }

        /// <summary>订单号（下单后可得；签发阶段为 null）。</summary>
        public string OrderNo { get; set; }
    }

    /// <summary>订单状态。</summary>
    public sealed class OrderStatusInfo
    {
        public string OrderNo { get; set; }
        public string Status { get; set; }
        public long Amount { get; set; }
        public System.DateTimeOffset? PayTime { get; set; }
    }

    /// <summary>AI 能力可用性（单项）。</summary>
    public sealed class AiCapabilityInfo
    {
        /// <summary>能力标识，如 ai.image.generate。</summary>
        public string Capability { get; set; }

        /// <summary>该能力当前映射的聚合网关模型名。</summary>
        public string Model { get; set; }

        /// <summary>是否可用（聚合网关未配置时为 false）。</summary>
        public bool Available { get; set; }
    }

    /// <summary>AI 能力清单与整体可用性。</summary>
    public sealed class AiCapabilitiesResult
    {
        /// <summary>AI 能力整体是否可用（聚合网关已配置）。</summary>
        public bool Available { get; set; }

        /// <summary>不可用时的原因（可用时为 null）。</summary>
        public string Reason { get; set; }

        /// <summary>各能力明细。</summary>
        public System.Collections.Generic.List<AiCapabilityInfo> Capabilities { get; set; }
    }

    /// <summary>生图任务提交参数（不含密钥、不含定价）。</summary>
    public sealed class AiImageRequest
    {
        /// <summary>提示词（必填）。</summary>
        public string Prompt { get; set; }

        /// <summary>展项 ID（可选）：缺省用 <see cref="AndXOptions.ExhibitId"/>，都为空则由 Edge / 服务端兜底。</summary>
        public string ExhibitId { get; set; }

        /// <summary>尺寸（可选），如 1024x1024。</summary>
        public string Size { get; set; }

        /// <summary>生成数量（1~4，可选；缺省由服务端/模型决定）。</summary>
        public int? N { get; set; }

        /// <summary>扩展入参（可选，如参考图 mediaId）。</summary>
        public System.Collections.Generic.IDictionary<string, object> Input { get; set; }

        /// <summary>同参幂等键（可选）：命中成功的同参任务时直接复用结果。</summary>
        public string IdemKey { get; set; }
    }

    /// <summary>AI 生成任务。</summary>
    public sealed class AiJob
    {
        public string JobNo { get; set; }

        /// <summary>状态：见 <see cref="AndX.Core.AndXContract.AiJobStatuses"/>。</summary>
        public string Status { get; set; }

        public string Capability { get; set; }

        /// <summary>聚合网关解析后的供应商/网关标识。</summary>
        public string Provider { get; set; }

        public string Model { get; set; }

        /// <summary>是否命中同参幂等缓存（仅提交接口返回；查询任务时恒为 false）。</summary>
        public bool Cached { get; set; }

        /// <summary>成功后登记的首个 media 主键（可直接用于 <c>AndX.Share.ResolveAsync</c> / 出码）。</summary>
        public string MediaId { get; set; }

        /// <summary>全部结果素材主键。</summary>
        public System.Collections.Generic.List<string> MediaIds { get; set; }

        /// <summary>结果可下载地址（presigned，仅 SUCCEEDED 时非空）。</summary>
        public System.Collections.Generic.List<string> Urls { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public System.DateTimeOffset? CreatedAt { get; set; }

        public System.DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>是否终态（SUCCEEDED / FAILED / CANCELED）。</summary>
        public bool IsTerminal
        {
            get { return AndX.Core.AndXContract.AiJobStatuses.IsTerminal(Status); }
        }
    }

    /// <summary>轮询等待选项（用于 <c>AndX.Ai.WaitForJobAsync</c> / <c>GenerateImageAsync</c>）。</summary>
    public sealed class AiWaitOptions
    {
        /// <summary>最长等待时间（默认 120 秒）。</summary>
        public System.TimeSpan Timeout { get; set; } = System.TimeSpan.FromSeconds(120);

        /// <summary>轮询间隔（默认 2 秒）。</summary>
        public System.TimeSpan Interval { get; set; } = System.TimeSpan.FromSeconds(2);
    }
}
