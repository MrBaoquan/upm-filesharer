using System;
using System.Collections;
using System.Threading;
using AndX.Core;
using AndX.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AndX.Tests
{
    /// <summary>PlayMode 冒烟：验证 <c>AndX.Unity</c> 的启动注册与 UnityWebRequest 传输映射。</summary>
    public class RuntimeSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_registers_default_transport()
        {
            Config.Reset();
            Config.Init(new AndXOptions { Endpoint = "https://edge.test" });
            Assert.IsTrue(Config.IsConfigured, "AndXUnityBootstrap 应在启动时注册 UnityWebRequestTransport");
            Config.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnityWebRequestTransport_maps_unreachable_host_to_network_error()
        {
            var transport = new UnityWebRequestTransport();
            var request = new TransportRequest
            {
                Method = "GET",
                Url = "http://127.0.0.1:1/andx-nope",
                Timeout = TimeSpan.FromSeconds(3),
            };

            var task = transport.SendAsync(request, CancellationToken.None);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsFalse(task.IsFaulted, "传输实现不得抛异常，须以 TransportResponse 返回");
            Assert.IsTrue(task.Result.NetworkError, "连接被拒应映射为 NetworkError");
            Assert.IsFalse(task.Result.IsSuccess);
        }

        [Test]
        public void QrTextureLoader_returns_null_for_empty_url()
        {
            Assert.IsNull(QrTextureLoader.LoadAsync(null).GetAwaiter().GetResult());
            Assert.IsNull(QrTextureLoader.LoadAsync(string.Empty).GetAwaiter().GetResult());
        }
    }
}
