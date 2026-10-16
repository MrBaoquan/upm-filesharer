using System.Threading;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>向 core 注册 Unity 传输实现，使 <c>AndX.Config.Init(options)</c> 可直接使用。</summary>
    internal static class AndXUnityBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // 登记主线程上下文：core 的 ConfigureAwait(false) 会让后续请求落到后台线程，
            // 传输层据此把 Unity API 调用编组回主线程。
            UnityWebRequestTransport.InstallMainThread(SynchronizationContext.Current);
            AndXTransportProvider.Factory = () => new UnityWebRequestTransport();

            // 登记本机节点凭据存储：使 core 的 AndX.Config.InitAuto（直连模式）无需引用 UnityEngine
            // 即可找到 persistentDataPath/andx/node-credential.json（保护器见 usage.md §15.2）。
            AndXCredentialProvider.StoreFactory = () => NodeCredentialStorage.Create();
        }
    }
}
