using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AndX.Core
{
    /// <summary>
    /// 传输抽象：core 只依赖此接口，平台适配（UnityWebRequest / 后续 native）在各自实现中完成。
    /// 实现须保证：不抛网络异常，统一以 <see cref="TransportResponse"/> 返回。
    /// </summary>
    public interface IAndXTransport
    {
        Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
    }

    /// <summary>一次传输请求。</summary>
    public sealed class TransportRequest
    {
        public string Method { get; set; } = "GET";
        public string Url { get; set; }

        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        /// <summary>请求体（JSON 或二进制分片）。</summary>
        public byte[] Body { get; set; }

        public string ContentType { get; set; }

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>true 时只取原始字节（用于二维码 PNG）。</summary>
        public bool RawResponse { get; set; }

        /// <summary>上传进度（本请求已发送字节）。</summary>
        public IProgress<long> UploadProgress { get; set; }
    }

    /// <summary>一次传输响应（网络层结果，不做业务解析）。</summary>
    public sealed class TransportResponse
    {
        public int StatusCode { get; set; }
        public string Text { get; set; }
        public byte[] Bytes { get; set; }
        public string ContentType { get; set; }

        /// <summary>连接失败 / 传输中断等网络层错误。</summary>
        public bool NetworkError { get; set; }

        /// <summary>超时（属网络层错误的一种，便于映射为 SDK TIMEOUT）。</summary>
        public bool TimedOut { get; set; }

        public string NetworkMessage { get; set; }

        public bool IsSuccess
        {
            get { return !NetworkError && StatusCode >= 200 && StatusCode < 300; }
        }
    }
}
