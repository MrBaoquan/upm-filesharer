using System;

namespace AndX
{
    /// <summary>AndX SDK 配置。密钥一律不入包：边缘共享密钥经 <see cref="EdgeKey.FromEnvironment"/> 注入。</summary>
    public sealed class AndXOptions
    {
        /// <summary>服务端 / 边缘网关基址（含协议，不含尾斜杠），如 https://edge.museum-a.com。</summary>
        public string Endpoint { get; set; }

        /// <summary>边缘网关共享密钥（X-Edge-Key）；仅对 /api/edge/* 生效。</summary>
        public string EdgeKey { get; set; }

        /// <summary>登录令牌（可选）：需登录接口（如资源下载授权）会以 <c>Authorization: Bearer</c> 注入。</summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// 登录令牌提供者（可选）：每次请求时取值，便于令牌刷新；返回 null/空 则不注入。
        /// 设置后优先于 <see cref="AccessToken"/>。
        /// </summary>
        public Func<string> AccessTokenProvider { get; set; }

        /// <summary>共享核实现选择。</summary>
        public TransportMode Transport { get; set; } = TransportMode.Auto;

        /// <summary>单次请求超时。</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>可重试请求（网络错误 / 5xx）的最大重试次数。</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>分片大小（字节）。服务端有该值下限（S3 multipart 除末片外 ≥ 5 MiB）。</summary>
        public long ChunkSize { get; set; } = 8L * 1024 * 1024;

        /// <summary>
        /// 允许 http 明文基址（默认 false）。WebGL 平台强制 https；
        /// 内网联调可显式开启，切勿用于生产。
        /// </summary>
        public bool AllowInsecureHttp { get; set; }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "AndXOptions.Endpoint 不能为空");
            }
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "AndXOptions.Endpoint 不是合法的绝对地址: " + Endpoint);
            }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "AndXOptions.Endpoint 仅支持 http/https: " + Endpoint);
            }
            if (uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "Endpoint 使用 http 明文；如确需内网联调请显式设置 AllowInsecureHttp=true");
            }
            if (ChunkSize <= 0)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "AndXOptions.ChunkSize 必须为正数");
            }
            if (MaxRetries < 0)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "AndXOptions.MaxRetries 不能为负");
            }
        }
    }

    /// <summary>边缘网关密钥加载（密钥不入包、不入库、不入日志）。</summary>
    public static class EdgeKey
    {
        public const string EnvironmentVariable = "ANDX_EDGE_KEY";

        /// <summary>从环境变量读取边缘密钥；缺失时返回 null（由调用方决定是否报错）。</summary>
        public static string FromEnvironment()
        {
            return Environment.GetEnvironmentVariable(EnvironmentVariable);
        }
    }
}
