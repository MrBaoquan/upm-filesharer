using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AndX.Core
{
    /// <summary>
    /// AndX 控制面 HTTP 客户端：负责拼接地址、注入边缘密钥、统一信封解析、错误码映射与有限重试。
    /// 只依赖 <see cref="IAndXTransport"/>，平台差异全部下沉到传输实现。
    /// </summary>
    public sealed class AndXApiClient
    {
        private readonly IAndXTransport _transport;
        private readonly string _baseUrl;
        private readonly string _edgeKey;
        private readonly Func<string> _accessToken;
        private readonly TimeSpan _timeout;
        private readonly int _maxRetries;

        public AndXApiClient(IAndXTransport transport, string baseUrl, string edgeKey, TimeSpan timeout, int maxRetries, Func<string> accessToken = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _edgeKey = edgeKey;
            _accessToken = accessToken;
            _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout;
            _maxRetries = maxRetries < 0 ? 0 : maxRetries;
        }

        public string BaseUrl
        {
            get { return _baseUrl; }
        }

        /// <summary>把相对路径拼成绝对地址。</summary>
        public string ResolveUrl(string path)
        {
            var normalized = string.IsNullOrEmpty(path) ? "/" : (path[0] == '/' ? path : "/" + path);
            return _baseUrl + normalized;
        }

        public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
        {
            var token = await SendEnvelopeAsync(BuildRequest("GET", ResolveUrl(path), null, false, null), cancellationToken).ConfigureAwait(false);
            return Map<T>(token);
        }

        public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken = default)
        {
            var token = await SendEnvelopeAsync(BuildRequest("POST", ResolveUrl(path), body, false, null), cancellationToken).ConfigureAwait(false);
            return Map<T>(token);
        }

        public Task<JToken> PostRawAsync(string path, object body, CancellationToken cancellationToken = default)
        {
            return SendEnvelopeAsync(BuildRequest("POST", ResolveUrl(path), body, false, null), cancellationToken);
        }

        public Task<JToken> GetRawAsync(string path, CancellationToken cancellationToken = default)
        {
            return SendEnvelopeAsync(BuildRequest("GET", ResolveUrl(path), null, false, null), cancellationToken);
        }

        /// <summary>流式分片透传（PUT + application/octet-stream），返回信封 data。</summary>
        public Task<JToken> PutChunkAsync(string url, byte[] body, IProgress<long> progress, CancellationToken cancellationToken = default)
        {
            var request = new TransportRequest
            {
                Method = "PUT",
                Url = url,
                Body = body,
                ContentType = "application/octet-stream",
                UploadProgress = progress,
                Timeout = _timeout,
            };
            ApplyHeaders(request);
            return SendEnvelopeAsync(request, cancellationToken);
        }

        /// <summary>一次性二进制透传（POST + application/octet-stream），用于小文件上传。</summary>
        public Task<JToken> PostBinaryAsync(string path, byte[] body, IProgress<long> progress, CancellationToken cancellationToken = default)
        {
            var request = new TransportRequest
            {
                Method = "POST",
                Url = ResolveUrl(path),
                Body = body,
                ContentType = "application/octet-stream",
                UploadProgress = progress,
                Timeout = _timeout,
            };
            ApplyHeaders(request);
            return SendEnvelopeAsync(request, cancellationToken);
        }

        public async Task<JToken> DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var response = await ExecuteAsync(BuildRequest("DELETE", ResolveUrl(path), null, false, null), cancellationToken).ConfigureAwait(false);
            return ParseEnvelope(response);
        }

        /// <summary>原始字节响应（二维码 PNG 等）；失败时按错误信封抛出。</summary>
        public async Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            var response = await ExecuteAsync(BuildRequest("GET", ResolveUrl(path), null, true, null), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                ParseEnvelope(response); // 抛出统一业务错误
                throw new AndXException("HTTP_" + response.StatusCode, response.Text ?? "请求失败", response.StatusCode);
            }
            return response.Bytes ?? Array.Empty<byte>();
        }

        private TransportRequest BuildRequest(string method, string url, object jsonBody, bool raw, IProgress<long> progress)
        {
            var request = new TransportRequest
            {
                Method = method,
                Url = url,
                RawResponse = raw,
                UploadProgress = progress,
                Timeout = _timeout,
            };
            if (jsonBody != null)
            {
                request.ContentType = "application/json";
                request.Body = Encoding.UTF8.GetBytes(AndXJson.Serialize(jsonBody));
            }
            ApplyHeaders(request);
            return request;
        }

        /// <summary>统一注入请求头：边缘密钥（仅 /api/edge/*）与登录令牌（若有）。</summary>
        private void ApplyHeaders(TransportRequest request)
        {
            if (!string.IsNullOrEmpty(_edgeKey)
                && request.Url != null
                && request.Url.IndexOf("/api/edge/", StringComparison.Ordinal) >= 0)
            {
                request.Headers[AndXContract.Headers.EdgeKey] = _edgeKey;
            }

            var token = _accessToken?.Invoke();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers[AndXContract.Headers.Authorization] = "Bearer " + token;
            }
        }

        private async Task<JToken> SendEnvelopeAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return ParseEnvelope(response);
        }

        private async Task<TransportResponse> ExecuteAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                ThrowIfCanceled(cancellationToken);

                TransportResponse response;
                try
                {
                    response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new AndXException(AndXContract.SdkErrorCodes.Canceled, "请求已取消");
                }
                catch (Exception e)
                {
                    response = new TransportResponse { NetworkError = true, NetworkMessage = e.Message };
                }

                ThrowIfCanceled(cancellationToken);

                var retryable = response.NetworkError || response.StatusCode >= 500;
                if (!retryable || attempt >= _maxRetries)
                {
                    if (response.NetworkError)
                    {
                        var code = response.TimedOut ? AndXContract.SdkErrorCodes.Timeout : AndXContract.SdkErrorCodes.Network;
                        throw new AndXException(code, response.NetworkMessage ?? "网络请求失败");
                    }
                    return response;
                }

                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new AndXException(AndXContract.SdkErrorCodes.Canceled, "请求已取消");
                }
            }
        }

        private static void ThrowIfCanceled(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Canceled, "请求已取消");
            }
        }

        /// <summary>解析统一信封：2xx 取 data；非 2xx 映射为 <see cref="AndXException"/>。</summary>
        private static JToken ParseEnvelope(TransportResponse response)
        {
            JObject json = null;
            if (!string.IsNullOrEmpty(response.Text))
            {
                try
                {
                    json = JObject.Parse(response.Text);
                }
                catch
                {
                    json = null;
                }
            }

            if (response.IsSuccess)
            {
                if (json == null)
                {
                    return null;
                }
                var code = json["code"];
                if (code != null && code.Type == JTokenType.Integer && code.Value<int>() == 0)
                {
                    return json["data"];
                }
                // 校验失败等可能以 2xx 返回业务错误码
                var successCode = json["code"];
                if (successCode != null && successCode.Type == JTokenType.String
                    && string.Equals(successCode.ToString(), "0", StringComparison.Ordinal))
                {
                    return json["data"];
                }
                if (IsErrorEnvelope(json))
                {
                    throw BuildException(json, response.StatusCode);
                }
                return json;
            }

            throw BuildException(json, response.StatusCode);
        }

        private static bool IsErrorEnvelope(JObject json)
        {
            var code = json?["code"];
            return code != null && code.Type == JTokenType.String && !string.Equals(code.ToString(), "0", StringComparison.Ordinal);
        }

        private static AndXException BuildException(JObject json, int statusCode)
        {
            var code = json?["code"]?.ToString();
            if (string.IsNullOrEmpty(code))
            {
                code = "HTTP_" + statusCode;
            }
            var message = json?["message"]?.ToString();
            if (string.IsNullOrEmpty(message))
            {
                message = "HTTP " + statusCode;
            }
            var traceId = json?["traceId"]?.ToString() ?? json?["path"]?.ToString();
            return new AndXException(code, message, statusCode, traceId);
        }

        private static T Map<T>(JToken token)
        {
            return token == null ? default : token.ToObject<T>(AndXJson.Serializer);
        }
    }
}
