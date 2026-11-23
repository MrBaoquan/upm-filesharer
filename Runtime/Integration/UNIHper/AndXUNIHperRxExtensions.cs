using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using UniRx;

namespace Parful.AndX
{
    /// <summary>
    /// AndX 业务服务的 UniRx 适配。仅由 UNIHper 集成程序集提供；
    /// 每个 Observable 都是冷流：订阅时启动异步调用，Dispose 时取消底层请求，终态回调统一切回主线程。
    /// </summary>
    public static class AndXUNIHperRxExtensions
    {
        // Share

        public static IObservable<IssuedTicket> IssueResourceTicketAsObservable(
            this Share share,
            string mediaId,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.IssueResourceTicketAsync(mediaId, token), cancellationToken);
        }

        public static IObservable<ShareResult> UploadAsObservable(
            this Share share,
            IAndXPayload payload,
            UploadOptions options = null,
            IProgress<UploadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.UploadAsync(payload, options, progress, token), cancellationToken);
        }

        public static IObservable<Unit> AbortAsObservable(
            this Share share,
            string uploadId,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.AbortAsync(uploadId, token), cancellationToken);
        }

        public static IObservable<ScanResolveResult> ResolveAsObservable(
            this Share share,
            string ticket,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.ResolveAsync(ticket, token), cancellationToken);
        }

        public static IObservable<DownloadResult> GetDownloadAsObservable(
            this Share share,
            string ticket,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.GetDownloadAsync(ticket, token), cancellationToken);
        }

        public static IObservable<byte[]> GetQrPngAsObservable(
            this Share share,
            string ticket,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => share.GetQrPngAsync(ticket, token), cancellationToken);
        }

        // Pay

        public static IObservable<PayTicket> CreateTicketAsObservable(
            this Pay pay,
            PayTicketOptions options = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => pay.CreateTicketAsync(options, token), cancellationToken);
        }

        public static IObservable<OrderStatusInfo> QueryOrderAsObservable(
            this Pay pay,
            string orderNo,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => pay.QueryOrderAsync(orderNo, token), cancellationToken);
        }

        public static IObservable<byte[]> GetQrPngAsObservable(
            this Pay pay,
            string ticket,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => pay.GetQrPngAsync(ticket, token), cancellationToken);
        }

        // AI

        public static IObservable<AICapabilitiesResult> GetCapabilitiesAsObservable(
            this AI ai,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GetCapabilitiesAsync(token), cancellationToken);
        }

        public static IObservable<AIJob> CreateImageJobAsObservable(
            this AI ai,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.CreateImageJobAsync(prompt, token), cancellationToken);
        }

        public static IObservable<AIJob> CreateImageJobAsObservable(
            this AI ai,
            AIImageRequest request,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.CreateImageJobAsync(request, token), cancellationToken);
        }

        public static IObservable<AITextResult> GenerateTextAsObservable(
            this AI ai,
            AITextRequest request,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GenerateTextAsync(request, token), cancellationToken);
        }

        public static IObservable<AITextResult> GenerateTextAsObservable(
            this AI ai,
            string scenario,
            IDictionary<string, object> parameters = null,
            string prompt = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(
                token => ai.GenerateTextAsync(scenario, parameters, prompt, exhibitId, token),
                cancellationToken
            );
        }

        public static IObservable<AIJob> GetJobAsObservable(
            this AI ai,
            string jobNo,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GetJobAsync(jobNo, token), cancellationToken);
        }

        public static IObservable<AIJob> CancelJobAsObservable(
            this AI ai,
            string jobNo,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.CancelJobAsync(jobNo, token), cancellationToken);
        }

        public static IObservable<AIJob> WaitForJobAsObservable(
            this AI ai,
            string jobNo,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.WaitForJobAsync(jobNo, waitOptions, progress, token), cancellationToken);
        }

        public static IObservable<AIJob> GenerateScenarioAsObservable(
            this AI ai,
            string scenario,
            IDictionary<string, object> inputs = null,
            IDictionary<string, object> parameters = null,
            string exhibitId = null,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(
                token => ai.GenerateScenarioAsync(
                    scenario,
                    inputs,
                    parameters,
                    exhibitId,
                    waitOptions,
                    progress,
                    token
                ),
                cancellationToken
            );
        }

        public static IObservable<AIJob> GenerateImageAsObservable(
            this AI ai,
            string prompt,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GenerateImageAsync(prompt, waitOptions, progress, token), cancellationToken);
        }

        public static IObservable<AIJob> GenerateImageAsObservable(
            this AI ai,
            AIImageRequest request,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GenerateImageAsync(request, waitOptions, progress, token), cancellationToken);
        }

        public static IObservable<byte[]> GetResultBytesAsObservable(
            this AI ai,
            AIJob job,
            int index = 0,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GetResultBytesAsync(job, index, token), cancellationToken);
        }

        public static IObservable<byte[]> GetResultBytesAsObservable(
            this AI ai,
            string jobNo,
            int index = 0,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.GetResultBytesAsync(jobNo, index, token), cancellationToken);
        }

        public static IObservable<string> SaveResultAsObservable(
            this AI ai,
            AIJob job,
            int index,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.SaveResultAsync(job, index, filePath, token), cancellationToken);
        }

        public static IObservable<string> SaveResultAsObservable(
            this AI ai,
            string jobNo,
            int index,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => ai.SaveResultAsync(jobNo, index, filePath, token), cancellationToken);
        }

        // Node

        public static IObservable<NodeCredential> EnrollAsObservable(
            this Node node,
            string endpoint,
            string enrollmentToken,
            string nodeId,
            string name = null,
            NodeCredentialStore store = null,
            IAndXTransport transport = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(
                token => node.EnrollAsync(endpoint, enrollmentToken, nodeId, name, store, transport, token),
                cancellationToken
            );
        }

        public static IObservable<NodeIdentity> SelfAsObservable(
            this Node node,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => node.SelfAsync(token), cancellationToken);
        }

        public static IObservable<NodeHeartbeatResult> HeartbeatAsObservable(
            this Node node,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => node.HeartbeatAsync(exhibitId, token), cancellationToken);
        }

        public static IObservable<NodeStateResult> ReportStateAsObservable(
            this Node node,
            string state,
            string orderNo = null,
            string commandId = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(
                token => node.ReportStateAsync(state, orderNo, commandId, exhibitId, token),
                cancellationToken
            );
        }

        public static IObservable<IReadOnlyList<NodeAuditEntry>> RecentAuditAsObservable(
            this Node node,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            return FromTask(token => node.RecentAuditAsync(limit, token), cancellationToken);
        }

        private static IObservable<T> FromTask<T>(
            Func<CancellationToken, Task<T>> taskFactory,
            CancellationToken cancellationToken)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            return Observable.Create<T>(observer =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var disposed = 0;
                var completed = 0;
                Task<T> task;

                try
                {
                    task = taskFactory(cts.Token);
                }
                catch (Exception ex)
                {
                    TryObserve(observer, () => observer.OnError(ex));
                    cts.Dispose();
                    return Disposable.Empty;
                }

                if (task == null)
                {
                    TryObserve(observer, () => observer.OnError(new InvalidOperationException("AndX 异步接口返回了空 Task")));
                    cts.Dispose();
                    return Disposable.Empty;
                }

                task.ContinueWith(
                    finished =>
                    {
                        if (Volatile.Read(ref disposed) != 0)
                        {
                            return;
                        }

                        Volatile.Write(ref completed, 1);
                        if (finished.IsCanceled)
                        {
                            TryObserve(observer, () => observer.OnError(new TaskCanceledException(finished)));
                        }
                        else if (finished.IsFaulted)
                        {
                            var error = finished.Exception == null
                                ? new InvalidOperationException("AndX 异步调用失败")
                                : finished.Exception.GetBaseException();
                            TryObserve(observer, () => observer.OnError(error));
                        }
                        else
                        {
                            TryObserve(observer, () =>
                            {
                                observer.OnNext(finished.Result);
                                observer.OnCompleted();
                            });
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );

                return Disposable.Create(() =>
                {
                    if (Interlocked.Exchange(ref disposed, 1) != 0)
                    {
                        return;
                    }
                    if (Volatile.Read(ref completed) == 0)
                    {
                        try
                        {
                            cts.Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Completion and disposal raced; the task has already ended.
                        }
                    }
                    cts.Dispose();
                });
            }).ObserveOnMainThread();
        }

        private static IObservable<Unit> FromTask(
            Func<CancellationToken, Task> taskFactory,
            CancellationToken cancellationToken)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            return FromTask(
                async token =>
                {
                    await taskFactory(token).ConfigureAwait(false);
                    return Unit.Default;
                },
                cancellationToken
            );
        }

        private static void TryObserve<T>(IObserver<T> observer, Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Observer disposal or cancellation can race with the final callback.
            }
        }
    }
}
