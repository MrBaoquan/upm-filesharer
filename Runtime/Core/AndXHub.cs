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
    /// AndX SDK 门面：<c>AndXHub.Create(options).Use&lt;ShareCapability&gt;().Use&lt;PayCapability&gt;()</c>。
    /// 能力按需注册，访问未注册能力抛出配置错误。
    /// </summary>
    public sealed class AndXHub
    {
        private readonly AndXOptions _options;
        private readonly AndXApiClient _api;
        private Share.ShareCapability _share;
        private Pay.PayCapability _pay;

        private AndXHub(AndXOptions options, IAndXTransport transport)
        {
            _options = options;
            _api = new AndXApiClient(transport, options.Endpoint, options.EdgeKey, options.Timeout, options.MaxRetries);
        }

        /// <summary>创建门面（使用平台适配层注册的默认传输）。</summary>
        public static AndXHub Create(AndXOptions options)
        {
            return Create(options, null);
        }

        /// <summary>创建门面（显式注入传输实现，便于测试与自定义实现）。</summary>
        public static AndXHub Create(AndXOptions options, IAndXTransport transport)
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
            return new AndXHub(options, resolved);
        }

        /// <summary>注册能力域。</summary>
        public AndXHub Use<T>() where T : class, IAndXCapability
        {
            var type = typeof(T);
            if (type == typeof(Share.ShareCapability))
            {
                _share = new Share.ShareCapability(_api, _options.ChunkSize);
            }
            else if (type == typeof(Pay.PayCapability))
            {
                _pay = new Pay.PayCapability(_api);
            }
            else
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "未知能力域: " + type.Name);
            }
            return this;
        }

        /// <summary>资源分享能力域。</summary>
        public Share.ShareCapability Share()
        {
            return _share ?? throw NotRegistered("ShareCapability");
        }

        /// <summary>扫码付费能力域。</summary>
        public Pay.PayCapability Pay()
        {
            return _pay ?? throw NotRegistered("PayCapability");
        }

        /// <summary>当前配置（只读用途）。</summary>
        public AndXOptions Options
        {
            get { return _options; }
        }

        private static IAndXTransport ResolveDefaultTransport()
        {
            var factory = AndXTransportProvider.Factory;
            return factory == null ? null : factory();
        }

        private static AndXException NotRegistered(string name)
        {
            return new AndXException(AndXContract.SdkErrorCodes.Configuration, "能力未注册: " + name + "，请先调用 Use<" + name + ">()");
        }
    }
}
