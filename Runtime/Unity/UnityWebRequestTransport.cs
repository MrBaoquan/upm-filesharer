using System;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AndX.Unity
{
    /// <summary>
    /// UnityWebRequest 传输实现：Windows / Android / WebGL 通吃。
    /// Unity API 必须在主线程调用，而 core 使用 <c>ConfigureAwait(false)</c> 可能让后续请求落在
    /// 后台线程（分片上传等多次连续请求必现），故此处把发送编组回主线程。
    /// 不抛网络异常，统一以 <see cref="TransportResponse"/> 返回。
    /// </summary>
    public sealed class UnityWebRequestTransport : IAndXTransport
    {
        private static SynchronizationContext _mainContext;
        private static int _mainThreadId;

        /// <summary>由 <c>AndXUnityBootstrap</c> 在主线程登记上下文，用于把请求编组回主线程。</summary>
        public static void InstallMainThread(SynchronizationContext context)
        {
            _mainContext = context;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>是否运行在已登记的主线程上。</summary>
        private static bool OnMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            // 已在主线程（或尚未登记上下文）时直接发送；否则编组回主线程，
            // 避免 "Create can only be called from the main thread"。
            if (_mainContext == null || OnMainThread)
            {
                return SendInternal(request, cancellationToken);
            }

            var tcs = new TaskCompletionSource<TransportResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainContext.Post(_ =>
            {
                SendInternal(request, cancellationToken).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled)
                        {
                            tcs.TrySetCanceled();
                        }
                        else if (t.IsFaulted)
                        {
                            tcs.TrySetException(t.Exception.InnerExceptions);
                        }
                        else
                        {
                            tcs.TrySetResult(t.Result);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }, null);
            return tcs.Task;
        }

        private static async Task<TransportResponse> SendInternal(TransportRequest request, CancellationToken cancellationToken)
        {
            using (var www = new UnityWebRequest(request.Url, request.Method))
            {
                www.downloadHandler = new DownloadHandlerBuffer();

                if (request.Body != null && request.Body.Length > 0)
                {
                    www.uploadHandler = new UploadHandlerRaw(request.Body);
                    if (!string.IsNullOrEmpty(request.ContentType))
                    {
                        www.uploadHandler.contentType = request.ContentType;
                    }
                }

                foreach (var header in request.Headers)
                {
                    www.SetRequestHeader(header.Key, header.Value);
                }
                if (!string.IsNullOrEmpty(request.ContentType))
                {
                    www.SetRequestHeader("Content-Type", request.ContentType);
                }

                www.timeout = Math.Max(1, (int)request.Timeout.TotalSeconds);

                var registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        www.Abort();
                    }
                    catch
                    {
                        // 忽略：请求可能已结束
                    }
                });

                try
                {
                    var operation = www.SendWebRequest();
                    while (!operation.isDone)
                    {
                        ReportProgress(request, www);
                        await Task.Yield();
                    }
                    ReportProgress(request, www);
                }
                finally
                {
                    registration.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                var response = new TransportResponse
                {
                    StatusCode = (int)www.responseCode,
                    ContentType = www.GetResponseHeader("Content-Type"),
                };

                if (IsConnectionFailure(www))
                {
                    response.NetworkError = true;
                    response.NetworkMessage = www.error;
                    response.TimedOut = !string.IsNullOrEmpty(www.error)
                        && www.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    response.Bytes = www.downloadHandler != null ? www.downloadHandler.data : null;
                    if (!request.RawResponse && www.downloadHandler != null)
                    {
                        response.Text = www.downloadHandler.text;
                    }
                }

                return response;
            }
        }

        private static void ReportProgress(TransportRequest request, UnityWebRequest www)
        {
            if (request.UploadProgress != null && www.uploadHandler != null)
            {
                request.UploadProgress.Report((long)www.uploadedBytes);
            }
        }

        private static bool IsConnectionFailure(UnityWebRequest www)
        {
#if UNITY_2020_2_OR_NEWER
            return www.result == UnityWebRequest.Result.ConnectionError
                || www.result == UnityWebRequest.Result.DataProcessingError;
#else
            return www.isNetworkError;
#endif
        }
    }
}
