using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>传输实现工厂（由平台适配层注册；core 不引用任何平台 API）。</summary>
    public static class AndXTransportProvider
    {
        /// <summary>默认传输工厂。Unity 侧由 <c>AndX.Unity</c> 在启动时注册。</summary>
        public static Func<IAndXTransport> Factory;
    }

    /// <summary>
    /// 全局配置入口：调用一次 <see cref="Init(AndXOptions)"/> 或 <see cref="InitAuto(AndXOptions)"/> 后，
    /// 即可直接使用 <c>AndX.Share</c> / <c>AndX.Pay</c> / <c>AndX.AI</c>。
    ///
    /// > 两种接入形态（<see cref="AndXAccessMode"/>）**业务调用完全一致**，差异只在「凭据归谁 / 展项身份从哪来」：
    /// > 直连后端由插件持本机节点凭据；经 AndXEdge 则由 Edge 代持。
    /// </summary>
    public static class Config
    {
        private static AndXApiClient _api;
        private static long _chunkSize = 8L * 1024 * 1024;
        private static string _exhibitId;
        private static NodeCredential _nodeCredential;
        private static bool _hasEdgeKey;

        /// <summary>是否已完成配置。</summary>
        public static bool IsConfigured
        {
            get { return _api != null; }
        }

        /// <summary>
        /// 生效的接入方式：<see cref="InitAuto(AndXOptions)"/> 解析后写回；显式 <see cref="Init(AndXOptions)"/> 时取
        /// <see cref="AndXOptions.AccessMode"/>（调用方声明值，未做推断）。
        /// </summary>
        public static AndXAccessMode ActiveMode { get; private set; } = AndXAccessMode.Direct;

        /// <summary>生效的本机节点身份（直连模式）；经 Edge 或未持节点凭据时为 null。**密钥不外露**。</summary>
        public static string ActiveNodeId
        {
            get { return _nodeCredential == null ? null : _nodeCredential.NodeId; }
        }

        /// <summary>本次配置的选择理由（可读，不含密钥）；示例与现场排障可直接打印。</summary>
        public static string ResolutionReason { get; private set; }

        /// <summary>已配置的控制面客户端；未配置时抛出配置错误。</summary>
        internal static AndXApiClient Api
        {
            get
            {
                var api = _api;
                if (api == null)
                {
                    throw new AndXException(
                        AndXContract.SdkErrorCodes.Configuration,
                        "AndX 未配置：请先调用 AndX.Config.InitAuto(new AndXOptions { AccessMode = ... })（默认直连后端）或 AndX.Config.InitLocal()");
                }
                return api;
            }
        }

        /// <summary>上传分片阈值（字节）。</summary>
        internal static long ChunkSize
        {
            get { return _chunkSize; }
        }

        /// <summary>全局默认展项 ID（<see cref="AndXOptions.ExhibitId"/>）；空表示由 Edge / 服务端兜底。</summary>
        internal static string ExhibitId
        {
            get { return _exhibitId; }
        }

        /// <summary>解析展项 ID：显式值优先，其次全局默认；均空返回 null。</summary>
        internal static string ResolveExhibitId(string explicitId)
        {
            return !string.IsNullOrEmpty(explicitId) ? explicitId : _exhibitId;
        }

        /// <summary>一行接入摘要（**不含任何密钥**）：模式 / 基址 / 身份来源 / 展项兜底 / 选择理由。</summary>
        public static string Describe()
        {
            var api = _api;
            if (api == null)
            {
                return "未配置（请先调用 AndX.Config.InitAuto 或 InitLocal）";
            }

            var parts = new List<string>
            {
                "mode=" + ActiveMode,
                "endpoint=" + api.BaseUrl,
            };
            if (_nodeCredential != null)
            {
                parts.Add("identity=节点身份 node:" + _nodeCredential.NodeId + "（服务端审计到「节点 + 展项」）");
            }
            else if (_hasEdgeKey)
            {
                parts.Add("identity=共享密钥 x-edge-key（旧轨，直连建议改节点身份）");
            }
            else
            {
                parts.Add("identity=由对端代持（本机 Edge）");
            }
            if (!string.IsNullOrEmpty(_exhibitId))
            {
                parts.Add("exhibitId=" + _exhibitId);
            }
            if (!string.IsNullOrEmpty(ResolutionReason))
            {
                parts.Add("说明=" + ResolutionReason);
            }
            return string.Join(" | ", parts);
        }

        /// <summary>使用平台适配层注册的默认传输进行配置。</summary>
        public static void Init(AndXOptions options)
        {
            Init(options, null);
        }

        /// <summary>
        /// 零参数接入本机 AndXEdge（<see cref="AndXContract.Defaults.LocalEdgeEndpoint"/>）：
        /// 鉴权、展项标识与公网地址均由 Edge 代持，调用方无需提供任何密钥。
        /// 等价于 <c>InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Edge })</c>。
        /// </summary>
        public static void InitLocal()
        {
            InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Edge });
        }

        /// <summary>显式注入传输实现进行配置（便于测试与自定义实现）。</summary>
        public static void Init(AndXOptions options, IAndXTransport transport)
        {
            if (options == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AndXOptions 不能为空");
            }
            options.Validate();
            var resolved = transport ?? ResolveDefaultTransport();
            if (resolved == null)
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "未注册传输实现：Unity 侧请确保 AndX.Unity 已初始化，或显式传入 IAndXTransport");
            }
            _api = new AndXApiClient(
                resolved,
                options.Endpoint,
                options.EdgeKey,
                options.Timeout,
                options.MaxRetries,
                BuildTokenProvider(options),
                options.NodeCredential);
            _chunkSize = options.ChunkSize > 0 ? options.ChunkSize : 8L * 1024 * 1024;
            _exhibitId = string.IsNullOrEmpty(options.ExhibitId) ? null : options.ExhibitId;
            _nodeCredential = options.NodeCredential;
            _hasEdgeKey = !string.IsNullOrEmpty(options.EdgeKey);
            ActiveMode = options.AccessMode;
            ResolutionReason = "显式 Init：按给定的 Endpoint（" + options.Endpoint + "）接入，未做模式推断";
        }

        /// <summary>
        /// 按 <see cref="AndXOptions.AccessMode"/> 自动接入（**默认 <see cref="AndXAccessMode.Direct"/>：直连 AndX 云服务**）：
        ///
        /// - <c>Direct</c>：基址按「<see cref="AndXOptions.Endpoint"/> → 环境变量 <c>ANDX_ENDPOINT</c> → 默认云服务基址（<see cref="AndXContract.Defaults.CloudEndpoint"/>）」回退；
        ///   节点身份取 <see cref="AndXOptions.NodeCredential"/>，缺省从本机凭据存储（<see cref="AndXCredentialProvider"/>）加载。
        /// - <c>Edge</c>：基址取 <see cref="AndXOptions.Endpoint"/>，缺省 <c>ANDX_EDGE_ENDPOINT</c> → 环回默认值；
        ///   不下发节点签名（由 Edge 代持）。
        /// - <c>Auto</c>：本机已有节点凭据（或提供了注册令牌）→ <c>Direct</c>，否则 <c>Edge</c>。
        ///
        /// 结果写回 <see cref="ActiveMode"/> / <see cref="ResolutionReason"/> / <see cref="ActiveNodeId"/>，示例与排障可直接打印
        /// <see cref="Describe"/>。需要「首启注册」（只有一次性令牌、尚无节点密钥）时用 <see cref="InitAutoAsync"/>。
        /// </summary>
        public static void InitAuto(AndXOptions options = null)
        {
            var opts = options ?? new AndXOptions();
            var plan = Plan(opts, allowEnroll: false);
            ApplyResolved(opts, plan);
            Init(opts, null);
            ActiveMode = plan.Mode;
            ResolutionReason = plan.Reason;
        }

        /// <summary>
        /// 同 <see cref="InitAuto(AndXOptions)"/>，但允许用一次性注册令牌完成**首启注册**
        /// （<see cref="Node.EnrollAsync"/> 是异步动作）：注册成功后凭据落盘，后续启动走同步的 <see cref="InitAuto"/>。
        /// </summary>
        public static async Task InitAutoAsync(AndXOptions options = null, CancellationToken cancellationToken = default)
        {
            var opts = options ?? new AndXOptions();
            var plan = Plan(opts, allowEnroll: true);
            if (plan.NeedsEnroll)
            {
                plan.Credential = await Node
                    .EnrollAsync(
                        plan.Endpoint,
                        FirstNonEmpty(opts.EnrollmentToken, AndXEnvironment.EnrollmentToken),
                        FirstNonEmpty(opts.NodeId, AndXEnvironment.NodeId),
                        name: null,
                        store: AndXCredentialProvider.CreateStore(),
                        transport: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                plan.Reason = "直连模式：已用一次性注册令牌完成首启注册（node=" + plan.Credential.NodeId + "，凭据已落盘）";
            }
            ApplyResolved(opts, plan);
            Init(opts, null);
            ActiveMode = plan.Mode;
            ResolutionReason = plan.Reason;
        }

        /// <summary>清空配置（测试与场景切换用）。</summary>
        public static void Reset()
        {
            _api = null;
            _chunkSize = 8L * 1024 * 1024;
            _exhibitId = null;
            _nodeCredential = null;
            _hasEdgeKey = false;
            ActiveMode = AndXAccessMode.Direct;
            ResolutionReason = null;
        }

        /// <summary>接入解析结果（模式 / 基址 / 凭据 / 是否还需首启注册 / 理由）。</summary>
        private sealed class AccessPlan
        {
            public AndXAccessMode Mode;
            public string Endpoint;
            public NodeCredential Credential;
            public bool NeedsEnroll;
            public string Reason;
        }

        /// <summary>
        /// 解析接入方式：模式取 <see cref="AndXOptions.AccessMode"/>（<c>Auto</c> 时按「有无节点身份」落定），
        /// 基址与凭据按「显式字段 &gt; 环境变量 &gt; 默认」顺序回退。
        /// 解析不出来（缺少必要输入）时抛 <c>CONFIGURATION</c>，消息里直接给出该怎么补。
        /// </summary>
        private static AccessPlan Plan(AndXOptions options, bool allowEnroll)
        {
            var declared = options.AccessMode;
            var credential = options.NodeCredential ?? LoadStoredCredential();
            var enrollmentToken = FirstNonEmpty(options.EnrollmentToken, AndXEnvironment.EnrollmentToken);

            var mode = declared;
            if (mode == AndXAccessMode.Auto)
            {
                mode = credential != null || enrollmentToken != null ? AndXAccessMode.Direct : AndXAccessMode.Edge;
            }

            if (mode == AndXAccessMode.Edge)
            {
                var edgeEndpoint = FirstNonEmpty(
                    options.Endpoint,
                    AndXEnvironment.EdgeEndpoint,
                    AndXContract.Defaults.LocalEdgeEndpoint);
                return new AccessPlan
                {
                    Mode = AndXAccessMode.Edge,
                    Endpoint = edgeEndpoint,
                    Credential = null,
                    Reason = "Edge 模式：经本机 AndXEdge（" + edgeEndpoint + "）；鉴权 / 展项标识 / 公网地址由 Edge 代持"
                        + (declared == AndXAccessMode.Auto ? "，Auto 因本机无节点凭据回退到 Edge" : string.Empty),
                };
            }

            // 基址三级回退：显式 Endpoint > 环境变量 ANDX_ENDPOINT > 云服务默认基址
            var explicitEndpoint = FirstNonEmpty(options.Endpoint, AndXEnvironment.Endpoint);
            var endpoint = explicitEndpoint ?? AndXContract.Defaults.CloudEndpoint;
            var endpointSource = explicitEndpoint != null
                ? (string.IsNullOrWhiteSpace(options.Endpoint) ? "环境变量 " + AndXEnvironment.EndpointVariable : "AndXOptions.Endpoint")
                : "默认云服务基址（可用 AndXOptions.Endpoint 或 " + AndXEnvironment.EndpointVariable + " 覆盖）";

            if (credential != null)
            {
                return new AccessPlan
                {
                    Mode = AndXAccessMode.Direct,
                    Endpoint = endpoint,
                    Credential = credential,
                    Reason = "直连模式：" + endpointSource + " → " + endpoint
                        + "；使用本机节点身份 node=" + credential.NodeId + "（服务端据此判定租户/展项并审计到「节点 + 展项」）",
                };
            }

            if (enrollmentToken != null)
            {
                if (!allowEnroll)
                {
                    throw new AndXException(
                        AndXContract.SdkErrorCodes.Configuration,
                        "本机还没有节点凭据，但检测到一次性注册令牌（" + AndXEnvironment.EnrollmentTokenVariable
                        + "）：首启注册是异步动作，请改用 await AndX.Config.InitAutoAsync(...)，或先用 AndX.Node.EnrollAsync(...) 注册一次");
                }
                return new AccessPlan
                {
                    Mode = AndXAccessMode.Direct,
                    Endpoint = endpoint,
                    Credential = null,
                    NeedsEnroll = true,
                    Reason = "直连模式：" + endpointSource + " → " + endpoint + "；用一次性注册令牌完成首启注册",
                };
            }

            throw new AndXException(
                AndXContract.SdkErrorCodes.Configuration,
                "直连模式需要本机节点身份：请先用 AndX.Node.EnrollAsync(endpoint, enrollmentToken, nodeId) 完成首启注册"
                + "（凭据默认落在 persistentDataPath/andx/node-credential.json）；或把 AccessMode 设为 Edge（经本机 AndXEdge）"
                + "/ Auto（有凭据则直连，否则走 Edge）");
        }

        /// <summary>把解析结果写回 options（再走统一的 <see cref="Init(AndXOptions, IAndXTransport)"/> 建客户端）。</summary>
        private static void ApplyResolved(AndXOptions options, AccessPlan plan)
        {
            options.Endpoint = plan.Endpoint;
            options.NodeCredential = plan.Credential;
            options.AccessMode = plan.Mode;
            if (!options.AllowInsecureHttp && AndXEnvironment.AllowInsecureHttp)
            {
                options.AllowInsecureHttp = true;
            }
        }

        /// <summary>加载本机节点凭据（平台未注册存储 / 文件不存在 / 内容损坏时返回 null）。</summary>
        private static NodeCredential LoadStoredCredential()
        {
            var store = AndXCredentialProvider.CreateStore();
            return store == null ? null : store.Load();
        }

        /// <summary>取第一个非空白值（去首尾空白）；全为空返回 null。</summary>
        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }

        private static Func<string> BuildTokenProvider(AndXOptions options)
        {
            if (options.AccessTokenProvider != null)
            {
                return options.AccessTokenProvider;
            }
            var token = options.AccessToken;
            return () => token;
        }

        private static IAndXTransport ResolveDefaultTransport()
        {
            var factory = AndXTransportProvider.Factory;
            return factory == null ? null : factory();
        }
    }
}