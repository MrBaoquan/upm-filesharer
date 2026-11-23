using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Parful.AndX;
using Parful.AndX.Core;
using UniRx;
using UnityEngine.TestTools;

namespace Parful.AndX.Tests
{
    /// <summary>UNIHper 可选集成层的 PlayMode 验证：冷流、主线程回调、Dispose 取消。</summary>
    public sealed class AndXUNIHperRxExtensionsTests
    {
        [UnityTest]
        public IEnumerator Observable_is_cold_and_completes_on_main_thread()
        {
            global::AndX.Config.Reset();
            var transport = new ImmediateTransport(
                "{\"code\":0,\"message\":\"ok\",\"data\":{\"available\":true,\"capabilities\":[]}}"
            );
            global::AndX.Config.Init(new AndXOptions { Endpoint = "https://edge.test" }, transport);
            var subscription = Disposable.Empty;
            try
            {
                var stream = global::AndX.AI.GetCapabilitiesAsObservable();
                Assert.AreEqual(0, transport.Calls, "Observable 必须是冷流，订阅前不得发起请求");

                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                AICapabilitiesResult result = null;
                Exception error = null;
                var callbackThreadId = 0;
                var completed = false;

                subscription = stream.Subscribe(
                    value =>
                    {
                        result = value;
                        callbackThreadId = Thread.CurrentThread.ManagedThreadId;
                    },
                    exception =>
                    {
                        error = exception;
                        completed = true;
                    },
                    () => completed = true
                );

                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!completed && DateTime.UtcNow < deadline)
                {
                    yield return null;
                }

                Assert.IsNull(error, error == null ? string.Empty : error.ToString());
                Assert.IsNotNull(result);
                Assert.IsTrue(result.Available);
                Assert.AreEqual(mainThreadId, callbackThreadId, "Rx 终态回调必须回到 Unity 主线程");
                Assert.AreEqual(1, transport.Calls);
            }
            finally
            {
                subscription.Dispose();
                global::AndX.Config.Reset();
            }
        }

        [UnityTest]
        public IEnumerator Disposing_subscription_cancels_underlying_task()
        {
            global::AndX.Config.Reset();
            var transport = new BlockingTransport();
            global::AndX.Config.Init(new AndXOptions { Endpoint = "https://edge.test" }, transport);
            var subscription = Disposable.Empty;
            try
            {
                subscription = global::AndX.AI
                    .GetCapabilitiesAsObservable()
                    .Subscribe(_ => { }, _ => { }, () => { });

                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!transport.Started.Task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    yield return null;
                }
                Assert.IsTrue(transport.Started.Task.IsCompleted, "订阅后应启动底层请求");

                subscription.Dispose();

                deadline = DateTime.UtcNow.AddSeconds(10);
                while (!transport.Canceled.Task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    yield return null;
                }
                Assert.IsTrue(transport.Canceled.Task.IsCompleted, "Dispose 必须取消底层 CancellationToken");
            }
            finally
            {
                subscription.Dispose();
                global::AndX.Config.Reset();
            }
        }

        private sealed class ImmediateTransport : IAndXTransport
        {
            private readonly string _body;

            public ImmediateTransport(string body)
            {
                _body = body;
            }

            public int Calls { get; private set; }

            public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(
                    new TransportResponse
                    {
                        StatusCode = 200,
                        Text = _body,
                        ContentType = "application/json",
                    }
                );
            }
        }

        private sealed class BlockingTransport : IAndXTransport
        {
            public TaskCompletionSource<bool> Started { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Canceled { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<TransportResponse> SendAsync(
                TransportRequest request,
                CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                try
                {
                    await Task.Delay(-1, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Canceled.TrySetResult(true);
                    throw;
                }

                return new TransportResponse { StatusCode = 200, Text = "{\"code\":0,\"data\":null}" };
            }
        }
    }
}
