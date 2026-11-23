using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Parful.AndX.Aip
{
    public sealed class AipClientOptions
    {
        public string Endpoint = "wss://andx.andcrane.com/api/aip";
        public string DeviceId;
        public string Secret;
        public string ExhibitId;
        public bool TestMode;
        public int RequestTimeoutMs = 30000;

        public void Validate()
        {
            Uri uri;
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("AIP endpoint must be a ws:// or wss:// URL.", "Endpoint");
            if (string.IsNullOrWhiteSpace(DeviceId)) throw new ArgumentException("AIP DeviceId is required.", "DeviceId");
            if (RequestTimeoutMs < 1000 || RequestTimeoutMs > 60000)
                throw new ArgumentException("AIP RequestTimeoutMs must be between 1000 and 60000.", "RequestTimeoutMs");
            if (!TestMode && string.IsNullOrWhiteSpace(Secret))
                throw new ArgumentException("AIP Secret is required when TestMode is false.", "Secret");
        }
    }

    public sealed class AipServerRequest
    {
        public string Id;
        public string Name;
        public string SessionId;
        public int? Seq;
        public JObject Payload;
        public JObject Raw;
    }

    public sealed class AipCommand
    {
        public string Action;
        public JObject Args;
        public string LeaseId;
        public string SessionId;
        public string IdempotencyKey;
        public JObject Request;
    }

    public sealed class AipCommandResult
    {
        public bool Ok;
        public JToken Data;
        public string ErrorCode;
        public string ErrorMessage;

        public static AipCommandResult Success(JToken data = null)
        {
            return new AipCommandResult { Ok = true, Data = data ?? JValue.CreateNull() };
        }

        public static AipCommandResult Failure(string code, string message)
        {
            return new AipCommandResult { Ok = false, ErrorCode = code ?? "AIP_HOST_ERROR", ErrorMessage = message ?? "AIP host command failed." };
        }
    }

    public sealed class AipClient : IDisposable
    {
        private readonly AipClientOptions _options;
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly object _pendingLock = new object();
        private readonly Dictionary<string, TaskCompletionSource<JObject>> _pending = new Dictionary<string, TaskCompletionSource<JObject>>();
        private ClientWebSocket _socket;
        private CancellationTokenSource _lifetime;
        private int _seq;
        private bool _disposed;

        public AipClient(AipClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
        }

        public bool IsConnected
        {
            get { return _socket != null && _socket.State == WebSocketState.Open; }
        }

        public Func<AipServerRequest, Task<AipCommandResult>> RequestHandler { get; set; }
        public Action<JObject> EventReceived { get; set; }
        public Action<bool> ConnectionChanged { get; set; }

        public async Task ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AipClient));
            if (IsConnected) return;
            await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected) return;
                CloseSocket();
                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                if (_options.TestMode)
                {
                    socket.Options.SetRequestHeader("x-aip-test-node", _options.DeviceId);
                }
                else
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    var nonce = Guid.NewGuid().ToString("N");
                    var emptyHash = Sha256Hex(Array.Empty<byte>());
                    var canonical = string.Join("\n", "GET", CanonicalPath(new Uri(_options.Endpoint)), timestamp, nonce, emptyHash);
                    socket.Options.SetRequestHeader("x-device-id", _options.DeviceId);
                    socket.Options.SetRequestHeader("x-device-timestamp", timestamp);
                    socket.Options.SetRequestHeader("x-device-nonce", nonce);
                    socket.Options.SetRequestHeader("x-device-signature", HmacHex(_options.Secret, canonical));
                }

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(_options.RequestTimeoutMs);
                    await socket.ConnectAsync(new Uri(_options.Endpoint), timeout.Token).ConfigureAwait(false);
                }
                _socket = socket;
                _lifetime = new CancellationTokenSource();
                ConnectionChanged?.Invoke(true);
                _ = ReceiveLoopAsync(socket, _lifetime.Token);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task<JObject> RequestAsync(
            string name,
            JObject payload = null,
            string sessionId = null,
            DateTimeOffset? expiresAt = null,
            string idempotencyKey = null,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AipClient));
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            var id = "unity-" + Guid.NewGuid().ToString("N");
            var message = new JObject
            {
                ["v"] = "1.0",
                ["type"] = "request",
                ["id"] = id,
                ["name"] = name,
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["seq"] = Interlocked.Increment(ref _seq),
                ["payload"] = payload ?? new JObject()
            };
            if (!string.IsNullOrWhiteSpace(sessionId)) message["sessionId"] = sessionId;
            if (expiresAt.HasValue) message["expiresAt"] = expiresAt.Value.ToString("O");
            if (!string.IsNullOrWhiteSpace(idempotencyKey)) message["idempotencyKey"] = idempotencyKey;

            var completion = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock) _pending[id] = completion;
            try
            {
                await SendTextAsync(message.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
                return await WaitWithTimeoutAsync(completion.Task, timeoutMs ?? _options.RequestTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("AIP request timed out: " + name);
            }
            finally
            {
                lock (_pendingLock) _pending.Remove(id);
            }
        }

        public async Task OpenSessionAsync(string role = "exhibit", string participantId = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var payload = new JObject
            {
                ["exhibitId"] = _options.ExhibitId ?? string.Empty,
                ["role"] = role,
                ["participantId"] = participantId ?? ("unity-" + Guid.NewGuid().ToString("N"))
            };
            var response = await RequestAsync("session.open", payload, null, null, "session-" + Guid.NewGuid().ToString("N"), null, cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(response, "session.open");
        }

        public async Task PublishStateAsync(string state, JObject data, int revision, string sessionId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var payload = new JObject
            {
                ["revision"] = revision,
                ["state"] = state,
                ["data"] = data ?? new JObject()
            };
            var response = await RequestAsync("state.publish", payload, sessionId, DateTimeOffset.UtcNow.AddMinutes(1), "state-" + revision, null, cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(response, "state.publish");
        }

        public async Task SafetyStopAsync(string sessionId, string reason, CancellationToken cancellationToken = default(CancellationToken))
        {
            var payload = new JObject { ["reason"] = reason ?? "unity safety stop", ["scope"] = "all" };
            var response = await RequestAsync("safety.stop", payload, sessionId, DateTimeOffset.UtcNow.AddMinutes(1), "safety-" + Guid.NewGuid().ToString("N"), null, cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(response, "safety.stop");
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using (var stream = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            stream.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        var text = Encoding.UTF8.GetString(stream.ToArray());
                        var document = JObject.Parse(text);
                        await DispatchAsync(document, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { }
            finally
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                    ConnectionChanged?.Invoke(false);
                }
                FailPending("AIP_CONNECTION_CLOSED", "AIP connection closed.");
            }
        }

        private async Task DispatchAsync(JObject document, CancellationToken cancellationToken)
        {
            var type = (string)document["type"];
            if (type == "response")
            {
                var id = (string)document["id"];
                TaskCompletionSource<JObject> completion = null;
                lock (_pendingLock) _pending.TryGetValue(id, out completion);
                if (completion != null) completion.TrySetResult(document);
                return;
            }

            if (type == "event")
            {
                EventReceived?.Invoke(document);
                return;
            }

            if (type != "request") return;
            var request = new AipServerRequest
            {
                Id = (string)document["id"] ?? string.Empty,
                Name = (string)document["name"] ?? string.Empty,
                SessionId = (string)document["sessionId"],
                Seq = (int?)document["seq"],
                Payload = document["payload"] as JObject ?? new JObject(),
                Raw = document
            };
            var result = RequestHandler == null
                ? AipCommandResult.Failure("AIP_HOST_ERROR", "Unity host has no AIP request handler.")
                : await RequestHandler(request).ConfigureAwait(false);
            var payload = new JObject { ["ok"] = result != null && result.Ok };
            if (result != null && result.Ok)
            {
                payload["data"] = result.Data ?? JValue.CreateNull();
            }
            else
            {
                payload["error"] = new JObject
                {
                    ["code"] = result == null ? "AIP_HOST_ERROR" : result.ErrorCode,
                    ["message"] = result == null ? "Unity host command failed." : result.ErrorMessage
                };
            }
            var response = new JObject
            {
                ["v"] = "1.0",
                ["type"] = "response",
                ["id"] = request.Id,
                ["name"] = request.Name,
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["payload"] = payload
            };
            if (!string.IsNullOrWhiteSpace(request.SessionId)) response["sessionId"] = request.SessionId;
            if (request.Seq.HasValue) response["seq"] = request.Seq.Value;
            await SendTextAsync(response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
        }

        private async Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
                throw new InvalidOperationException("AIP is not connected.");
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void FailPending(string code, string message)
        {
            TaskCompletionSource<JObject>[] pending;
            lock (_pendingLock)
            {
                pending = new TaskCompletionSource<JObject>[_pending.Values.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
            }
            foreach (var item in pending) item.TrySetException(new InvalidOperationException(code + ": " + message));
        }

        private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, int timeoutMs, CancellationToken cancellationToken)
        {
            var delay = Task.Delay(timeoutMs, cancellationToken);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay) throw new TimeoutException();
            return await task.ConfigureAwait(false);
        }

        private static void ThrowIfFailed(JObject response, string operation)
        {
            var payload = response["payload"] as JObject;
            if (payload == null || (bool?)payload["ok"] != true)
            {
                var error = payload?["error"] as JObject;
                throw new InvalidOperationException((string)error?["code"] + ": " + ((string)error?["message"] ?? operation));
            }
        }

        private static string CanonicalPath(Uri uri)
        {
            var query = uri.Query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(query)) return uri.AbsolutePath;
            var parts = query.Split('&');
            Array.Sort(parts, StringComparer.Ordinal);
            return uri.AbsolutePath + "?" + string.Join("&", parts);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }
        private static string HmacHex(string secret, string value)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private void CloseSocket()
        {
            try { _lifetime?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
            _socket = null;
            _lifetime?.Dispose();
            _lifetime = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CloseSocket();
            _connectLock.Dispose();
            _sendLock.Dispose();
        }
    }
}