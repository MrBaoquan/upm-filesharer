using UnityEngine;

namespace AndX.Unity
{
    /// <summary>向 core 注册 Unity 传输实现，使 <c>AndXHub.Create(options)</c> 可直接使用。</summary>
    internal static class AndXUnityBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AndXTransportProvider.Factory = () => new UnityWebRequestTransport();
        }
    }
}
