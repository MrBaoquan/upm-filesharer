using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using UnityEngine;

namespace Parful.AndX.Samples
{
    /// <summary>
    /// Runnable capability-router sample. It exercises IDLE -> STARTING -> PLAYING ->
    /// qiuqian.draw -> IDLE without requiring a live AIP connection.
    /// </summary>
    public sealed class AndXAipCapabilityDemoBehaviour : MonoBehaviour
    {
        [SerializeField] private bool autoRun = true;

        private readonly object _gate = new object();
        private readonly StringBuilder _log = new StringBuilder();
        private ExhibitRuntime _runtime;
        private CancellationTokenSource _lifetime;
        private string _state = "IDLE";
        private string _detail = "等待运行";
        private bool _running;
        private bool _completed;

        private void Start()
        {
            if (autoRun) RunDemo();
        }

        public void RunDemo()
        {
            if (_running) return;
            _running = true;
            _completed = false;
            _lifetime = new CancellationTokenSource();
            _runtime = new ExhibitRuntime();
            _runtime.StateChanged += OnStateChanged;
            _runtime.Configure(new ExhibitRuntimeOptions
            {
                ExhibitId = "exhibit-aip-capability-demo",
                Adapter = new DemoAdapter(AppendLog),
                StatePublisher = new DemoPublisher(AppendLog),
                CommandStore = new InMemoryExhibitCommandStore(),
                CapabilityHandlers = new IExhibitCapabilityHandler[]
                {
                    new QiuQianDrawCapabilityHandler()
                },
                StartConfirmationTimeout = TimeSpan.FromSeconds(10)
            });

            AppendLog("示例启动：使用本地 Adapter，不连接生产 AIP。");
            _ = RunSequenceAsync(_lifetime.Token);
        }

        private async Task RunSequenceAsync(CancellationToken cancellationToken)
        {
            try
            {
                AppendLog("1. 下发 start");
                var start = await _runtime.HandleCommandAsync(Command(
                    "start", "cap-demo-start", "cap-demo-exec-start", "qiuqian.draw"));
                RequireSuccess(start, "start");

                await Task.Delay(300, cancellationToken);
                AppendLog("2. Adapter 已完成启动，上报 PLAYING");
                if (!await _runtime.NotifyPlayingAsync("CAP-DEMO-001", "cap-demo-exec-start"))
                    throw new InvalidOperationException("NotifyPlayingAsync returned false.");

                AppendLog("3. PLAYING 状态下请求 qiuqian.draw");
                var draw = await _runtime.HandleCommandAsync(Command(
                    "command.invoke", "cap-demo-draw", "cap-demo-exec-draw", "qiuqian.draw"));
                RequireSuccess(draw, "qiuqian.draw");

                await Task.Delay(500, cancellationToken);
                AppendLog("4. 业务动作完成，上报 IDLE");
                if (!await _runtime.NotifyCompletedAsync("CAP-DEMO-001", "cap-demo-exec-draw"))
                    throw new InvalidOperationException("NotifyCompletedAsync returned false.");

                SetDetail("完成：Capability Handler 仅在 PLAYING 执行，生命周期已回到 IDLE。", true);
            }
            catch (OperationCanceledException)
            {
                SetDetail("示例已取消。", false);
            }
            catch (Exception error)
            {
                AppendLog("失败：" + error.Message);
                SetDetail("失败：" + error.Message, false);
            }
            finally
            {
                lock (_gate) _running = false;
            }
        }

        private AipCommand Command(string action, string idempotencyKey, string commandId, string capabilityKey)
        {
            var meta = new JObject
            {
                ["orderNo"] = "CAP-DEMO-001",
                ["commandId"] = commandId,
                ["capabilityKey"] = capabilityKey
            };
            return new AipCommand
            {
                Action = action,
                SessionId = "cap-demo-session",
                IdempotencyKey = idempotencyKey,
                Args = new JObject
                {
                    ["orderNo"] = "CAP-DEMO-001",
                    ["commandId"] = commandId,
                    ["sessionId"] = "cap-demo-session"
                },
                Request = new JObject
                {
                    ["id"] = "request-" + idempotencyKey,
                    ["meta"] = meta
                }
            };
        }

        private static void RequireSuccess(AipCommandResult result, string step)
        {
            if (result == null || !result.Ok)
            {
                throw new InvalidOperationException(
                    step + " failed: " + (result == null ? "null result" : result.ErrorCode + " " + result.ErrorMessage));
            }
        }

        private void OnStateChanged(ExhibitRuntimeSnapshot snapshot)
        {
            if (snapshot == null) return;
            lock (_gate) _state = snapshot.State;
        }

        private void AppendLog(string message)
        {
            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            lock (_gate)
            {
                _log.AppendLine(line);
                if (_log.Length > 5000) _log.Remove(0, _log.Length - 5000);
            }
            Debug.Log("[AndX Capability Demo] " + message);
        }

        private void SetDetail(string detail, bool completed)
        {
            lock (_gate)
            {
                _detail = detail;
                _completed = completed;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(24, 24, 720, 520), GUI.skin.box);
            GUILayout.Label("<size=22><b>AndX AIP Capability Demo</b></size>", RichLabel());
            GUILayout.Space(8);
            string state;
            string detail;
            string log;
            bool running;
            bool completed;
            lock (_gate)
            {
                state = _state;
                detail = _detail;
                log = _log.ToString();
                running = _running;
                completed = _completed;
            }
            GUILayout.Label("状态：" + state);
            GUILayout.Label(detail);
            GUILayout.Space(8);
            GUI.enabled = !running;
            if (GUILayout.Button(completed ? "重新运行" : "运行 Capability Demo", GUILayout.Height(36)))
            {
                ResetRuntime();
                RunDemo();
            }
            GUI.enabled = true;
            GUILayout.Space(8);
            GUILayout.TextArea(log, GUILayout.ExpandHeight(true));
            GUILayout.EndArea();
        }

        private static GUIStyle RichLabel()
        {
            return new GUIStyle(GUI.skin.label) { richText = true };
        }

        private void ResetRuntime()
        {
            try { _lifetime?.Cancel(); } catch { }
            try { _runtime?.Dispose(); } catch { }
            _runtime = null;
            _lifetime = null;
            lock (_gate)
            {
                _log.Length = 0;
                _state = "IDLE";
                _detail = "等待运行";
                _running = false;
                _completed = false;
            }
        }

        private void OnDestroy()
        {
            try { _lifetime?.Cancel(); } catch { }
            if (_runtime != null) _runtime.StateChanged -= OnStateChanged;
            try { _runtime?.Dispose(); } catch { }
            try { _lifetime?.Dispose(); } catch { }
        }

        private sealed class DemoAdapter : IExhibitRuntimeAdapter
        {
            private readonly Action<string> _log;

            public DemoAdapter(Action<string> log)
            {
                _log = log;
            }

            public Task StartAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken)
            {
                _log("Adapter.StartAsync order=" + command.OrderNo);
                return Task.CompletedTask;
            }

            public Task StopAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken)
            {
                _log("Adapter.StopAsync order=" + command.OrderNo);
                return Task.CompletedTask;
            }
        }

        private sealed class DemoPublisher : IExhibitStatePublisher
        {
            private readonly Action<string> _log;

            public DemoPublisher(Action<string> log)
            {
                _log = log;
            }

            public Task PublishAsync(ExhibitRuntimeSnapshot snapshot, CancellationToken cancellationToken)
            {
                _log("state.publish " + snapshot.State + " revision=" + snapshot.Revision);
                return Task.CompletedTask;
            }
        }
    }
}
