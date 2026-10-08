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
    /// 不抛网络异常，统一以 <see cref="TransportResponse"/> 返回。
    /// </summary>
    public sealed class UnityWebRequestTransport : IAndXTransport
    {
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            return SendInternal(request, cancellationToken);
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
