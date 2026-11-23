using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using UnityEngine;

namespace Parful.AndX.Unity
{
    /// <summary>
    /// Unity Windows host adapter for AIP v1. Network callbacks are marshalled to Unity's main thread.
    /// Hardware ownership remains in the host; a command handler may be an IOToolkit adapter.
    /// </summary>
    [AddComponentMenu("AndX/AIP Host")]
    public sealed class AndXAipBehaviour : MonoBehaviour
    {
        [Header("AIP")]
        [SerializeField] private string configPath = "andx/aip.json";
        [SerializeField] private string endpoint = "ws://127.0.0.1:19132/api/aip";
        [SerializeField] private string deviceId = "unity-device";
        [SerializeField] private string secret = "";
        [SerializeField] private string exhibitId = "8";
        [SerializeField] private bool testMode = true;
        [SerializeField] private bool autoConnect = true;

        [Header("Host")]
        [SerializeField] private string defaultDevice = "Simulator";

        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private AipClient _client;
        private AipCommandDispatcher _dispatcher;
        private bool _connected;
        private CancellationTokenSource _lifetime;
        private Task _connectLoop;

        public event Action<bool> ConnectionChanged;
        public event Action<AipCommand> CommandReceived;

        public string SessionId { get; set; }
        public int StateRevision { get; private set; }
        public bool IsConnected { get { return _connected; } }
        public string DefaultDevice { get { return defaultDevice; } }

        public void SetCommandHandler(AipCommandDelegate handler)
        {
            _dispatcher = new AipCommandDispatcher(handler ?? throw new ArgumentNullException(nameof(handler)));
        }

        private void Awake()
        {
            LoadDefaults();
            _dispatcher = new AipCommandDispatcher(HandleCommandAsync);
            _client = new AipClient(new AipClientOptions
            {
                Endpoint = endpoint,
                DeviceId = deviceId,
                Secret = secret,
                ExhibitId = exhibitId,
                TestMode = testMode
            });
            _client.RequestHandler = HandleAipRequestAsync;
            _client.EventReceived = document =>
            {
                var eventName = (string)document?["name"];
                if (eventName == "state.changed" && document["payload"] is JObject state && state["revision"] != null)
                    StateRevision = (int?)state["revision"] ?? StateRevision;
                _mainThread.Enqueue(() => { });
            };
            _client.ConnectionChanged = connected => _mainThread.Enqueue(() =>
            {
                _connected = connected;
                ConnectionChanged?.Invoke(connected);
            });
        }


        private void Start()
        {
            if (!autoConnect) return;
            _lifetime = new CancellationTokenSource();
            _connectLoop = ConnectLoopAsync(_lifetime.Token);
        }

        private async Task ConnectLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected) await _client.ConnectAsync(cancellationToken);
                    await Task.Delay(3000, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    Debug.LogWarning("[AndX AIP] reconnect failed: " + exception.Message);
                    try { await Task.Delay(3000, cancellationToken); } catch { break; }
                }
            }
        }
        private void Update()
        {
            Action action;
            while (_mainThread.TryDequeue(out action))
            {
                try { action(); }
                catch (Exception exception) { Debug.LogError("[AndX AIP] main-thread callback failed: " + exception); }
            }
        }

        public async Task ConnectAsync()
        {
            try { await _client.ConnectAsync(); }
            catch (Exception exception) { Debug.LogWarning("[AndX AIP] connect failed: " + exception.Message); }
        }

        public Task<JObject> OpenSessionAsync(string role = "exhibit", string participantId = null)
        {
            return _client.RequestAsync("session.open", new JObject
            {
                ["exhibitId"] = exhibitId,
                ["role"] = role,
                ["participantId"] = participantId ?? ("unity-" + Guid.NewGuid().ToString("N"))
            }, null, null, "session-" + Guid.NewGuid().ToString("N"));
        }

        public async Task PublishStateAsync(string state, JObject data = null)
        {
            if (string.IsNullOrWhiteSpace(SessionId)) return;
            StateRevision++;
            await _client.PublishStateAsync(state, data ?? new JObject(), StateRevision, SessionId);
        }

        public async Task SafetyStopAsync(string reason = "unity safety stop")
        {
            if (_client == null || !_client.IsConnected) return;
            await _client.SafetyStopAsync(SessionId, reason);
        }

        private async Task<AipCommandResult> HandleAipRequestAsync(AipServerRequest request)
        {
            if (request.Name == "command.invoke" || request.Name == "safety.stop")
            {
                var commandTask = new TaskCompletionSource<AipCommandResult>();
                _mainThread.Enqueue(async () =>
                {
                    try
                    {
                        var result = await _dispatcher.DispatchAsync(request);
                        CommandReceived?.Invoke(new AipCommand
                        {
                            Action = request.Name == "safety.stop" ? "safety.stop" : (string)request.Payload?["action"],
                            Args = request.Payload?["args"] as JObject ?? new JObject(),
                            LeaseId = (string)request.Payload?["leaseId"],
                            SessionId = request.SessionId,
                            IdempotencyKey = (string)request.Raw?["idempotencyKey"],
                            Request = request.Raw
                        });
                        commandTask.TrySetResult(result);
                    }
                    catch (Exception exception)
                    {
                        commandTask.TrySetResult(AipCommandResult.Failure("AIP_HOST_ERROR", exception.Message));
                    }
                });
                return await commandTask.Task;
            }
            return AipCommandResult.Failure("AIP_UNSUPPORTED_PROTOCOL", "Unsupported AIP request: " + request.Name);
        }

        private Task<AipCommandResult> HandleCommandAsync(AipCommand command)
        {
            var received = CommandReceived;
            if (received == null)
                return Task.FromResult(AipCommandResult.Failure("AIP_HOST_NOT_READY", "No Unity command handler is registered."));
            received(command);
            return Task.FromResult(AipCommandResult.Success(new JObject { ["dispatched"] = true, ["action"] = command.Action }));
        }

        private void LoadDefaults()
        {
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, configPath);
                if (!File.Exists(path)) return;
                var root = JObject.Parse(File.ReadAllText(path));
                endpoint = (string)root["aipEndpoint"] ?? endpoint;
                deviceId = (string)root["deviceId"] ?? deviceId;
                secret = (string)root["secret"] ?? secret;
                exhibitId = (string)root["aipExhibitId"] ?? exhibitId;
                testMode = (bool?)root["aipTestMode"] ?? testMode;
                defaultDevice = (string)root["defaultDevice"] ?? defaultDevice;
            }
            catch (Exception exception) { Debug.LogWarning("[AndX AIP] config load failed: " + exception.Message); }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _client != null && _client.IsConnected)
                _ = SafetyStopAsync("unity lost focus");
        }

        private void OnDestroy()
        {
            try { _lifetime?.Cancel(); } catch { }
            _lifetime?.Dispose();
            _lifetime = null;
            if (_client != null) _client.Dispose();
            _client = null;
        }
    }
}
