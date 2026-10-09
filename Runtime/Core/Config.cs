using System;
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
    /// 全局配置入口：调用一次 <see cref="Init(AndXOptions)"/> 后，
    /// 即可直接使用 <c>AndX.Share</c> / <c>AndX.Pay</c>。
    /// </summary>
    public static class Config
    {
        private static AndXApiClient _api;
        private static long _chunkSize = 8L * 1024 * 1024;

        /// <summary>是否已完成配置。</summary>
        public static bool IsConfigured
        {
            get { return _api != null; }
        }

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
                        "AndX 未配置：请先调用 AndX.Config.Init(new AndXOptions { Endpoint = ... })");
                }
                return api;
            }
        }

        /// <summary>上传分片阈值（字节）。</summary>
        internal static long ChunkSize
        {
            get { return _chunkSize; }
        }

        /// <summary>使用平台适配层注册的默认传输进行配置。</summary>
        public static void Init(AndXOptions options)
        {
            Init(options, null);
        }

        /// <summary>
        /// 零参数接入：指向本机 AndXEdge 默认基址（<see cref="AndXContract.Defaults.LocalEdgeEndpoint"/>）。
        /// 鉴权、展项标识与公网地址均由 Edge 代持，调用方无需提供任何密钥。
        /// </summary>
        public static void InitLocal()
        {
            Init(new AndXOptions { Endpoint = AndXContract.Defaults.LocalEdgeEndpoint });
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
            _api = new AndXApiClient(resolved, options.Endpoint, options.EdgeKey, options.Timeout, options.MaxRetries, BuildTokenProvider(options));
            _chunkSize = options.ChunkSize > 0 ? options.ChunkSize : 8L * 1024 * 1024;
        }

        /// <summary>清空配置（测试与场景切换用）。</summary>
        public static void Reset()
        {
            _api = null;
            _chunkSize = 8L * 1024 * 1024;
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
