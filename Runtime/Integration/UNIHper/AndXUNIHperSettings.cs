using System;
using System.Xml.Serialization;
using Parful.AndX;
using Parful.AndX.Core;
using UNIHper;

namespace Parful.AndX.Integration.UNIHper
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [SerializedAt(AppPath.PersistentDir, "Configs", "AndX.xml", Priority = 0)]
#else
    [SerializedAt(AppPath.Auto, "Configs", "AndX.xml", Priority = 0)]
#endif
    [SerializeWith(ConfigDriver.XML)]
    public sealed class AndXUNIHperSettings : UConfig
    {
        [XmlAttribute]
        public int SchemaVersion = 1;

        [XmlAttribute]
        public bool AutoInitialize = true;

        [XmlAttribute]
        public string AccessMode = "Auto";

        [XmlAttribute]
        public string Endpoint = string.Empty;

        [XmlAttribute]
        public string ExhibitId = string.Empty;

        [XmlAttribute]
        public string NodeId = string.Empty;

        /// <summary>
        /// 开发期可直接写入的节点 HMAC 密钥；必须与 NodeId 配套。
        /// 生产环境建议改由环境变量或部署凭据注入，避免配置文件随包分发扩散。
        /// </summary>
        [XmlAttribute]
        public string NodeSecret = string.Empty;

        /// <summary>AndXEdge 共享密钥；仅开发/内网直连调试时直接写入，生产由 Edge 代持。</summary>
        [XmlAttribute]
        public string EdgeKey = string.Empty;

        /// <summary>登录令牌；开发期可写入，生产建议使用运行时 AccessTokenProvider 刷新。</summary>
        [XmlAttribute]
        public string AccessToken = string.Empty;

        /// <summary>一次性注册令牌；仅显式调用 InitializeAsync 时有效，普通自动启动不走异步注册。</summary>
        [XmlAttribute]
        public string EnrollmentToken = string.Empty;

        [XmlAttribute]
        public float TimeoutSeconds = 30f;

        [XmlAttribute]
        public int MaxRetries = 2;

        [XmlAttribute]
        public long ChunkSize = 8L * 1024 * 1024;

        [XmlAttribute]
        public bool AllowInsecureHttp = false;

        public AndXOptions CreateOptions()
        {
            Normalize();
            var nodeId = NullIfEmpty(NodeId);
            var nodeSecret = NullIfEmpty(NodeSecret);
            if (nodeSecret != null && nodeId == null)
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "AndX.xml 已设置 NodeSecret，但缺少配套的 NodeId"
                );
            }

            return new AndXOptions
            {
                AccessMode = ParseAccessMode(AccessMode),
                Endpoint = NullIfEmpty(Endpoint),
                ExhibitId = NullIfEmpty(ExhibitId),
                NodeId = nodeId,
                NodeCredential = nodeSecret == null ? null : new NodeCredential(nodeId, nodeSecret),
                EdgeKey = NullIfEmpty(EdgeKey),
                AccessToken = NullIfEmpty(AccessToken),
                EnrollmentToken = NullIfEmpty(EnrollmentToken),
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
                MaxRetries = MaxRetries,
                ChunkSize = ChunkSize,
                AllowInsecureHttp = AllowInsecureHttp,
                Transport = TransportMode.Auto,
            };
        }

        internal void Normalize()
        {
            if (SchemaVersion <= 0)
                SchemaVersion = 1;
            if (TimeoutSeconds <= 0f)
                TimeoutSeconds = 30f;
            if (MaxRetries < 0)
                MaxRetries = 0;
            if (ChunkSize <= 0L)
                ChunkSize = 8L * 1024 * 1024;

            AccessMode = NormalizeText(AccessMode, "Auto");
            Endpoint = NormalizeText(Endpoint, string.Empty);
            ExhibitId = NormalizeText(ExhibitId, string.Empty);
            NodeId = NormalizeText(NodeId, string.Empty);
            NodeSecret = NormalizeText(NodeSecret, string.Empty);
            EdgeKey = NormalizeText(EdgeKey, string.Empty);
            AccessToken = NormalizeText(AccessToken, string.Empty);
            EnrollmentToken = NormalizeText(EnrollmentToken, string.Empty);
        }

        protected override void OnDeserialized()
        {
            Normalize();
        }

        protected override void OnLoaded()
        {
            Normalize();
            AndXUNIHperBridge.ApplySettings(this, false);
        }

        protected override void OnSerializing()
        {
            Normalize();
        }

        protected override string Comment()
        {
            return "AndX UNIHper 接入配置。AutoInitialize 控制是否随 UNIHper 自动初始化；AccessMode 支持 Auto、Direct、Edge。开发期可在本文件直接填写 NodeId/NodeSecret、EdgeKey、AccessToken 或 EnrollmentToken；NodeSecret 必须与 NodeId 配套，生产环境建议改用环境变量或部署凭据。";
        }

        private static AndXAccessMode ParseAccessMode(string value)
        {
            var normalized = NormalizeText(value, "Auto");
            if (string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
                return AndXAccessMode.Auto;
            if (string.Equals(normalized, "Direct", StringComparison.OrdinalIgnoreCase))
                return AndXAccessMode.Direct;
            if (string.Equals(normalized, "Edge", StringComparison.OrdinalIgnoreCase))
                return AndXAccessMode.Edge;

            throw new AndXException(
                AndXContract.SdkErrorCodes.Configuration,
                "AndX UNIHper 配置 AccessMode 无效: " + normalized
            );
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}