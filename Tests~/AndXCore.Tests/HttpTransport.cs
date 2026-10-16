using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX.Tests
{
    /// <summary>
    /// 真实 HTTP 传输（**仅用于手动端到端测试**）：把 <see cref="TransportRequest"/> 原样打到真实后端，
    /// 网络异常统一映射为 <see cref="TransportResponse.NetworkError"/>（与 Unity 传输同一份契约）。
    /// </summary>
    internal sealed class HttpTransport : IAndXTransport
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            using (var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url))
            {
                foreach (var header in request.Headers)
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                if (request.Body != null)
                {
                    message.Content = new ByteArrayContent(request.Body);
                    if (!string.IsNullOrEmpty(request.ContentType))
                    {
                        message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
                    }
                }

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(request.Timeout);
                    try
                    {
                        using (var response = await Client
                            .SendAsync(message, HttpCompletionOption.ResponseContentRead, cts.Token)
                            .ConfigureAwait(false))
                        {
                            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                            return new TransportResponse
                            {
                                StatusCode = (int)response.StatusCode,
                                Text = Encoding.UTF8.GetString(bytes),
                                Bytes = bytes,
                                ContentType = response.Content.Headers.ContentType == null
                                    ? null
                                    : response.Content.Headers.ContentType.MediaType,
                            };
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return new TransportResponse { NetworkError = true, TimedOut = true, NetworkMessage = "请求超时" };
                    }
                    catch (Exception e)
                    {
                        return new TransportResponse { NetworkError = true, NetworkMessage = e.Message };
                    }
                }
            }
        }
    }
}