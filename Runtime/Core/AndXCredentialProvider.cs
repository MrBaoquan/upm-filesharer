using System;

namespace AndX
{
    /// <summary>
    /// 本机节点凭据存储的**平台注入点**（core 不引用任何平台 API，与 <see cref="AndXTransportProvider"/> 同构）。
    ///
    /// Unity 侧由 <c>AndX.Unity</c> 在启动时注册（默认落在
    /// <c>Application.persistentDataPath/andx/node-credential.json</c>）；单测或自定义平台可注入自己的
    /// <see cref="Core.NodeCredentialStore"/>（例如换成 DPAPI / Keystore 保护器，见 usage.md §15.2）。
    /// </summary>
    public static class AndXCredentialProvider
    {
        /// <summary>
        /// 凭据存储工厂；未注册时按「本机没有节点凭据」处理
        /// （<see cref="Config.InitAuto(AndXOptions)"/> 会给出可操作的错误提示）。
        /// </summary>
        public static Func<Core.NodeCredentialStore> StoreFactory;

        /// <summary>创建默认凭据存储；未注册平台存储时返回 null。</summary>
        public static Core.NodeCredentialStore CreateStore()
        {
            var factory = StoreFactory;
            return factory == null ? null : factory();
        }
    }
}
