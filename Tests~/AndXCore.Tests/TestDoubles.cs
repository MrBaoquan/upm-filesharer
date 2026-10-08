using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndX;
using AndX.Core;

namespace AndX.Tests
{
    internal sealed class RecordedRequest
    {
        public string Method;
        public string Url;
        public string Body;
        public string ContentType;
        public bool HasEdgeKey;
        public IDictionary<string, string> Headers;
    }

    /// <summary>可编排的假传输：按调用序号返回预设响应，并记录请求。</summary>
    internal sealed class FakeTransport : IAndXTransport
    {
        private readonly Func<TransportRequest, int, TransportResponse> _handler;

        public FakeTransport(Func<TransportRequest, int, TransportResponse> handler)
        {
            _handler = handler;
        }

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        public int Calls
        {
            get { return Requests.Count; }
        }

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            var index = Requests.Count;
            Requests.Add(new RecordedRequest
            {
                Method = request.Method,
                Url = request.Url,
                Body = request.Body == null ? null : Encoding.UTF8.GetString(request.Body),
                ContentType = request.ContentType,
                HasEdgeKey = request.Headers.ContainsKey(AndXContract.Headers.EdgeKey),
                Headers = new Dictionary<string, string>(request.Headers),
            });
            return Task.FromResult(_handler(request, index));
        }
    }

    internal static class FakeResponse
    {
        public static TransportResponse Json(int status, string body)
        {
            return new TransportResponse { StatusCode = status, Text = body, ContentType = "application/json" };
        }

        public static TransportResponse Ok(string dataJson)
        {
            return Json(200, "{\"code\":0,\"message\":\"ok\",\"data\":" + (dataJson ?? "null") + "}");
        }

        public static TransportResponse Error(int status, string code, string message)
        {
            return Json(status, "{\"code\":\"" + code + "\",\"message\":\"" + message
                + "\",\"data\":null,\"path\":\"/api/x\",\"timestamp\":\"2026-01-01T00:00:00.000Z\"}");
        }

        public static TransportResponse Network(bool timedOut = false)
        {
            return new TransportResponse
            {
                NetworkError = true,
                TimedOut = timedOut,
                NetworkMessage = timedOut ? "Request timeout" : "connect failed",
            };
        }
    }

    internal sealed class TestPayload : IAndXPayload
    {
        private readonly byte[] _bytes;

        public TestPayload(byte[] bytes, string fileName = "sample.png", MediaType mediaType = MediaType.Image)
        {
            _bytes = bytes;
            FileName = fileName;
            MediaType = mediaType;
        }

        public string FileName { get; }

        public MediaType MediaType { get; }

        public long Length
        {
            get { return _bytes.LongLength; }
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(new MemoryStream(_bytes, false));
        }
    }

    internal sealed class SampleData
    {
        public string Token { get; set; }
        public long Amount { get; set; }
    }
}
