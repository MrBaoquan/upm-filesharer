using System;

namespace AndX
{
    /// <summary>
    /// AndX SDK 配置。两种接入形态见 <see cref="AndXAccessMode"/>：**默认直连 AndX 云服务**（插件持本机节点凭据），
    /// 也可经本机 AndXEdge 接入（Edge 代持鉴权，插件不持密钥）。业务调用与接入形态无关。
    /// </summary>
    public sealed class AndXOptions
    {
        /// <summary>
        /// 接入方式（<see cref="AndXAccessMode"/>，默认 <see cref="AndXAccessMode.Direct"/>：**直连 AndX 云服务**）。
        /// 仅 <see cref="Config.InitAuto(AndXOptions)"/> 读取并按它解析基址与凭据；
        /// 显式 <see cref="Config.Init(AndXOptions)"/> 不做推断，此字段只作声明。
        /// </summary>
        public AndXAccessMode AccessMode { get; set; } = AndXAccessMode.Direct;

        /// <summary>
        /// 服务端 / 边缘网关基址（含协议，不含尾斜杠）。**含义随 <see cref="AccessMode"/> 变化**：
        /// 直连 = 后端 API 基址（缺省读环境变量 <c>ANDX_ENDPOINT</c>，再缺省用云服务基址 <c>https://andx.andcrane.com</c>）；
        /// Edge = 本机 AndXEdge 基址（缺省 <c>ANDX_EDGE_ENDPOINT</c> → <c>http://127.0.0.1:6699</c>）。
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// 边缘网关共享密钥（X-Edge-Key）；对 <c>/api/edge/*</c> 与 <c>/api/ai/*</c> 控制面生效。
        /// <b>仅在绕过 AndXEdge 直连后端时需要</b>——经本机 Edge 时留空，由 Edge 代持与注入。
        /// </summary>
        public string EdgeKey { get; set; }

        /// <summary>登录令牌（可选）：需登录接口（如资源下载授权）会以 <c>Authorization: Bearer</c> 注入。</summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// 登录令牌提供者（可选）：每次请求时取值，便于令牌刷新；返回 null/空 则不注入。
        /// 设置后优先于 <see cref="AccessToken"/>。
        /// </summary>
        public Func<string> AccessTokenProvider { get; set; }

        /// <summary>
        /// 全局默认展项 ID（可选）：作为 <c>AndX.AI</c> / <c>AndX.Share</c> / <c>AndX.Pay</c>
        /// 未显式传参时的兜底；调用处的显式值优先。
        /// 一个 Edge 服务多展项时建议在此配置。服务端会强制校验其归属（规范红线 2）。
        /// </summary>
        public string ExhibitId { get; set; }

        /// <summary>
        /// 节点身份凭据（可选，solution §16.2）：**直连后端**时使用——插件持本机节点密钥（HMAC），
        /// 服务端据此判定租户/展项并审计到展项。经 AndXEdge（Local/Relay/Hub）接入时留空。
        /// 与 <see cref="EdgeKey"/> 同时存在时**优先节点身份**（每节点一把、可单独吊销、审计到节点）。
        /// <see cref="Config.InitAuto(AndXOptions)"/> 在 <see cref="AndXAccessMode.Edge"/> 模式下会**清空**它：
        /// 经 Edge 时插件不下发节点签名，身份由 Edge 代持。
        /// </summary>
        public NodeCredential NodeCredential { get; set; }

        /// <summary>
        /// 首启注册令牌（可选）：只有「还没有本机节点密钥」时才需要，用它在 <see cref="Config.InitAutoAsync"/> 里换节点身份。
        /// **属于凭据**：只从环境变量 <c>ANDX_ENROLLMENT_TOKEN</c> 或部署配置注入，切勿写进代码或打进包体（规范 R9.2）。
        /// </summary>
        public string EnrollmentToken { get; set; }

        /// <summary>
        /// 首启注册的节点编号（可选）：展馆内唯一的硬件编号，缺省读环境变量 <c>ANDX_NODE_ID</c>。
        /// 只在 <see cref="Config.InitAutoAsync"/> 注册那一刻使用；注册完成后身份来自凭据文件。
        /// </summary>
        public string NodeId { get; set; }

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
            if (uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp && !uri.IsLoopback)
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
