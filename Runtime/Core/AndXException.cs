using System;

namespace AndX
{
    /// <summary>
    /// AndX 统一异常：携带稳定错误码（业务错误码见 <see cref="Core.AndXContract.ErrorCodes"/>，
    /// SDK 侧见 <see cref="Core.AndXContract.SdkErrorCodes"/>），不向调用方暴露裸 HTTP 状态码语义。
    /// </summary>
    public sealed class AndXException : Exception
    {
        public AndXException(string code, string message, int? httpStatus = null, string traceId = null)
            : base(message)
        {
            Code = code;
            HttpStatus = httpStatus;
            TraceId = traceId;
        }

        /// <summary>稳定错误码。</summary>
        public string Code { get; }

        /// <summary>HTTP 状态码（网络层错误时为 null）。</summary>
        public int? HttpStatus { get; }

        /// <summary>服务端追踪标识（服务端暂未统一回传时为 null）。</summary>
        public string TraceId { get; }

        public bool Is(string code)
        {
            return string.Equals(Code, code, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return HttpStatus.HasValue
                ? "[" + Code + "] " + Message + " (HTTP " + HttpStatus.Value + ")"
                : "[" + Code + "] " + Message;
        }
    }
}
