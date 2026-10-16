using System;

namespace AndX
{
    /// <summary>
    /// 环境变量接入面（规范 R9.1/R9.2：默认值 &lt; 环境变量 &lt; 部署配置；密钥不入代码、不入包体、不入日志）。
    ///
    /// **只有 <see cref="Config.InitAuto(AndXOptions)"/> 会读这里**：显式 <c>Init(options)</c> 完全按调用方给的字段走，
    /// 不受进程环境影响（便于测试与现场排查）。
    ///
    /// 接入方式（Direct / Edge）**刻意不提供环境变量**：它决定「展项身份从哪来」，必须由代码 / Inspector 显式选择，
    /// 避免一个残留的环境变量把现场选择悄悄改掉。
    /// </summary>
    public static class AndXEnvironment
    {
        /// <summary>直连后端基址（对应 <see cref="AndXOptions.Endpoint"/>）。</summary>
        public const string EndpointVariable = "ANDX_ENDPOINT";

        /// <summary>本机 Edge 基址（Edge 模式的 Endpoint；缺省 <c>http://127.0.0.1:6699</c>）。</summary>
        public const string EdgeEndpointVariable = "ANDX_EDGE_ENDPOINT";

        /// <summary>首启注册用的一次性令牌（对应 <see cref="AndXOptions.EnrollmentToken"/>；**不要写日志**）。</summary>
        public const string EnrollmentTokenVariable = "ANDX_ENROLLMENT_TOKEN";

        /// <summary>首启注册用的节点编号（对应 <see cref="AndXOptions.NodeId"/>）。</summary>
        public const string NodeIdVariable = "ANDX_NODE_ID";

        /// <summary>放行 http 明文基址（对应 <see cref="AndXOptions.AllowInsecureHttp"/>，仅联调）。</summary>
        public const string AllowInsecureHttpVariable = "ANDX_ALLOW_INSECURE_HTTP";

        /// <summary>直连后端基址；未设置返回 null。</summary>
        public static string Endpoint
        {
            get { return Read(EndpointVariable); }
        }

        /// <summary>本机 Edge 基址；未设置返回 null。</summary>
        public static string EdgeEndpoint
        {
            get { return Read(EdgeEndpointVariable); }
        }

        /// <summary>一次性注册令牌；未设置返回 null。</summary>
        public static string EnrollmentToken
        {
            get { return Read(EnrollmentTokenVariable); }
        }

        /// <summary>节点编号；未设置返回 null。</summary>
        public static string NodeId
        {
            get { return Read(NodeIdVariable); }
        }

        /// <summary>是否放行 http 明文基址。</summary>
        public static bool AllowInsecureHttp
        {
            get { return IsTruthy(Read(AllowInsecureHttpVariable)); }
        }

        /// <summary>读取环境变量并去掉首尾空白；空白归一为 null。</summary>
        public static string Read(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            var raw = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        /// <summary>常见真值写法（<c>1</c> / <c>true</c> / <c>yes</c> / <c>on</c>，大小写不敏感）。</summary>
        public static bool IsTruthy(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }
            var value = raw.Trim();
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
