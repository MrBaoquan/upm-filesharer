using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
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

        [UnityTest]
        public IEnumerator UnityWebRequestTransport_can_be_called_from_background_thread()
        {
            // 回归：core 使用 ConfigureAwait(false)，分片上传会让后续请求落在后台线程。
            // 传输层须把 Unity API 调用编组回主线程，否则报 "Create can only be called from the main thread"。
            var transport = new UnityWebRequestTransport();
            var request = new TransportRequest
            {
                Method = "GET",
                Url = "http://127.0.0.1:1/andx-nope",
                Timeout = TimeSpan.FromSeconds(3),
            };

            Task<TransportResponse> task = null;
            using (var started = new ManualResetEventSlim(false))
            {
                var worker = new Thread(() =>
                {
                    task = transport.SendAsync(request, CancellationToken.None);
                    started.Set();
                }) { IsBackground = true };
                worker.Start();

                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!started.IsSet && DateTime.UtcNow < deadline)
                {
                    yield return null;
                }
                Assert.IsTrue(started.IsSet, "后台线程应已发起请求");

                while ((task == null || !task.IsCompleted) && DateTime.UtcNow < deadline)
                {
                    yield return null;
                }
            }

            Assert.IsTrue(task != null && task.IsCompleted, "跨线程请求应在超时前完成");
            Assert.IsFalse(task.IsFaulted,
                "跨线程调用不得抛异常（含 Unity 主线程限制）：" + (task.Exception?.GetBaseException().Message ?? ""));
            Assert.IsTrue(task.Result.NetworkError, "连接被拒应映射为 NetworkError");
            Assert.That(task.Result.NetworkMessage ?? string.Empty, Does.Not.Contain("main thread"),
                "不得出现 Unity 主线程限制错误");
        }

        [Test]
        public void QrTextureLoader_returns_null_for_empty_url()
        {
            Assert.IsNull(QrTextureLoader.LoadAsync(null).GetAwaiter().GetResult());
            Assert.IsNull(QrTextureLoader.LoadAsync(string.Empty).GetAwaiter().GetResult());
        }
    }
}
