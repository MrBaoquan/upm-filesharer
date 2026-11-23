using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Parful.AndX.Aip
{
    /// <summary>
    /// Unity 展项运行时唯一业务门面：AIP start/stop → 平台 Adapter → 单调状态上报。
    /// 只接受 start/stop/safety.stop，不暴露 IO、串口、Modbus 或 PluginChannel。
    /// </summary>
    public sealed class ExhibitRuntime : IDisposable
    {
        private readonly SemaphoreSlim _commandGate = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();
        private IExhibitRuntimeAdapter _adapter;
        private readonly Dictionary<string, IExhibitCapabilityHandler> _capabilityHandlers = new Dictionary<string, IExhibitCapabilityHandler>(StringComparer.Ordinal);
        private IExhibitStatePublisher _publisher;
        private IExhibitCommandStore _commandStore = new InMemoryExhibitCommandStore();
        private ExhibitRuntimeOptions _options = new ExhibitRuntimeOptions();
        private ExhibitRuntimeState _state = ExhibitRuntimeState.Idle;
        private int _revision;
        private string _currentOrderNo;
        private string _currentCommandId;
        private string _currentSessionId;
        private string _currentErrorCode;
        private string _currentErrorMessage;
        private CancellationTokenSource _activeStartCancellation;
        private CancellationTokenSource _activeCapabilityCancellation;
        private CancellationTokenSource _startupConfirmationCancellation;
        private bool _disposed;

        public event Action<ExhibitRuntimeSnapshot> StateChanged;

        public void Configure(ExhibitRuntimeOptions options)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExhibitRuntime));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Adapter == null) throw new ArgumentException("ExhibitRuntime adapter is required.", nameof(options));
            if (options.StartConfirmationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentException("StartConfirmationTimeout must be positive.", nameof(options));
            }
            CancelPendingActions();
            _options = options;
            _adapter = options.Adapter;
            _publisher = options.StatePublisher;
            _commandStore = options.CommandStore ?? new InMemoryExhibitCommandStore();
            _capabilityHandlers.Clear();
            foreach (var handler in options.CapabilityHandlers ?? new IExhibitCapabilityHandler[0])
            {
                if (handler == null || string.IsNullOrWhiteSpace(handler.Capability))
                    throw new ArgumentException("Capability handlers require a capability name.", nameof(options));
                if (_capabilityHandlers.ContainsKey(handler.Capability.Trim()))
                    throw new ArgumentException("Duplicate capability handler: " + handler.Capability, nameof(options));
                _capabilityHandlers.Add(handler.Capability.Trim(), handler);
            }
            lock (_stateLock)
            {
                _state = ExhibitRuntimeState.Idle;
                _revision = 0;
                _currentOrderNo = null;
                _currentCommandId = null;
                _currentSessionId = null;
                _currentErrorCode = null;
                _currentErrorMessage = null;
            }
        }

        /// <summary>
        /// 将运行时绑定到 AIP Client。若未显式提供 StatePublisher，则使用 AIP state.publish。
        /// </summary>
        public void BindAipClient(AipClient client)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExhibitRuntime));
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (_adapter == null) throw new InvalidOperationException("Configure ExhibitRuntime before binding AIP.");
            if (_publisher == null) _publisher = new AipExhibitStatePublisher(client);
            client.RequestHandler = new AipCommandDispatcher(HandleCommandAsync).DispatchAsync;
        }

        public ExhibitRuntimeSnapshot Snapshot
        {
            get
            {
                lock (_stateLock)
                {
                    return BuildSnapshot(_state, _revision, _currentOrderNo, _currentCommandId, _currentSessionId, _currentErrorCode, _currentErrorMessage, _options.UtcNow());
                }
            }
        }

        /// <summary>Adapter 接口入口；测试可绕过 AIP 直接调用。</summary>
        public async Task<AipCommandResult> HandleCommandAsync(AipCommand command)
        {
            if (_disposed) return AipCommandResult.Failure("AIP_HOST_ERROR", "ExhibitRuntime is disposed.");
            var requestedAction = command == null ? null : command.Action;
            var action = NormalizeAction(requestedAction);
            if (action == null && string.IsNullOrWhiteSpace(requestedAction))
            {
                return AipCommandResult.Failure("AIP_UNSUPPORTED_PROTOCOL", "ExhibitRuntime requires a semantic action.");
            }
            if (action == "stop") CancelPendingActions();

            await _commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_adapter == null || _publisher == null)
                {
                    return AipCommandResult.Failure("AIP_HOST_ERROR", "ExhibitRuntime is not fully configured.");
                }
                var capabilityKey = action == null ? ResolveCapabilityKey(command, requestedAction) : null;
                var handler = action == null ? ResolveCapabilityHandler(capabilityKey) : null;
                if (action == null && handler == null)
                {
                    return AipCommandResult.Failure(
                        "AIP_UNSUPPORTED_PROTOCOL",
                        "No capability handler registered for action: " + requestedAction);
                }
                var runtimeCommand = action == null
                    ? ToCapabilityCommand(command, capabilityKey)
                    : ToRuntimeCommand(command, action);
                if (string.IsNullOrWhiteSpace(runtimeCommand.OrderNo)
                    || string.IsNullOrWhiteSpace(runtimeCommand.CommandId)
                    || string.IsNullOrWhiteSpace(runtimeCommand.SessionId))
                {
                    return AipCommandResult.Failure(
                        "AIP_INVALID_REQUEST",
                        "Commands require orderNo, commandId and sessionId.");
                }
                var key = FirstNonEmpty(command.IdempotencyKey, runtimeCommand.CommandId, (string)command.Request?["id"]);
                if (string.IsNullOrWhiteSpace(key))
                {
                    return AipCommandResult.Failure("AIP_INVALID_REQUEST", "A command deduplication key is required.");
                }
                runtimeCommand.IdempotencyKey = key;

                var existing = await _commandStore.GetAsync(key).ConfigureAwait(false);
                if (existing != null) return Replay(existing, runtimeCommand.Action, runtimeCommand);

                var record = NewPendingRecord(key, runtimeCommand);
                try
                {
                    await _commandStore.SaveAsync(record).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    return AipCommandResult.Failure("AIP_COMMAND_STORE_FAILED", error.Message);
                }

                AipCommandResult result;
                try
                {
                    if (action == null)
                        result = await ExecuteCapabilityAsync(handler, runtimeCommand).ConfigureAwait(false);
                    else
                        result = action == "start"
                            ? await ExecuteStartAsync(runtimeCommand).ConfigureAwait(false)
                            : await ExecuteStopAsync(runtimeCommand).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    result = AipCommandResult.Failure("EXHIBIT_RUNTIME_ERROR", error.Message);
                }

                CompleteRecord(record, result);
                try
                {
                    await _commandStore.SaveAsync(record).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    return AipCommandResult.Failure("AIP_COMMAND_STORE_FINALIZE_FAILED", error.Message);
                }
                return result;
            }
            finally
            {
                _commandGate.Release();
            }
        }

        public async Task<bool> NotifyPlayingAsync(string orderNo = null, string commandId = null)
        {
            if (_disposed) return false;
            CancelStartupTimeout();
            await _commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_stateLock)
                {
                    if (_state != ExhibitRuntimeState.Starting) return false;
                }
                var command = new ExhibitRuntimeCommand
                {
                    Action = "start",
                    OrderNo = orderNo ?? _currentOrderNo,
                    CommandId = commandId ?? _currentCommandId,
                    SessionId = _currentSessionId
                };
                await SetStateAsync(ExhibitRuntimeState.Playing, command, null, null, true).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _commandGate.Release();
            }
        }

        public async Task<bool> NotifyCompletedAsync(string orderNo = null, string commandId = null)
        {
            if (_disposed) return false;
            CancelStartupTimeout();
            await _commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_stateLock)
                {
                    if (_state != ExhibitRuntimeState.Starting && _state != ExhibitRuntimeState.Playing) return false;
                }
                var command = new ExhibitRuntimeCommand
                {
                    Action = "stop",
                    OrderNo = orderNo ?? _currentOrderNo,
                    CommandId = commandId ?? _currentCommandId,
                    SessionId = _currentSessionId
                };
                await SetStateAsync(ExhibitRuntimeState.Idle, command, null, null, true).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _commandGate.Release();
            }
        }

        public async Task MarkOfflineAsync(string code = "EXHIBIT_OFFLINE", string message = "Exhibit runtime is offline.")
        {
            if (_disposed) return;
            CancelPendingActions();
            await _commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var command = new ExhibitRuntimeCommand
                {
                    Action = "stop",
                    OrderNo = _currentOrderNo,
                    CommandId = _currentCommandId,
                    SessionId = _currentSessionId
                };
                await SetStateAsync(ExhibitRuntimeState.Offline, command, code, message, true).ConfigureAwait(false);
            }
            finally
            {
                _commandGate.Release();
            }
        }

        private string ResolveCapabilityKey(AipCommand command, string requestedAction)
        {
            var metaKey = command?.Request?["meta"]?["capabilityKey"]?.ToString();
            return FirstNonEmpty(metaKey, requestedAction)?.Trim();
        }

        private IExhibitCapabilityHandler ResolveCapabilityHandler(string capabilityKey)
        {
            if (string.IsNullOrWhiteSpace(capabilityKey)) return null;
            IExhibitCapabilityHandler handler;
            return _capabilityHandlers.TryGetValue(capabilityKey, out handler) ? handler : null;
        }

        private static ExhibitRuntimeCommand ToCapabilityCommand(AipCommand command, string capabilityKey)
        {
            var args = command.Args ?? new JObject();
            return new ExhibitRuntimeCommand
            {
                Action = capabilityKey,
                Args = args,
                LeaseId = command.LeaseId,
                OrderNo = FirstNonEmpty((string)args["orderNo"], (string)command.Request?["meta"]?["orderNo"]),
                CommandId = FirstNonEmpty(
                    (string)args["commandId"],
                    (string)command.Request?["meta"]?["commandId"],
                    (string)command.Request?["id"]),
                SessionId = FirstNonEmpty(command.SessionId, (string)args["sessionId"]),
                IdempotencyKey = command.IdempotencyKey
            };
        }

        private async Task<AipCommandResult> ExecuteCapabilityAsync(
            IExhibitCapabilityHandler handler,
            ExhibitRuntimeCommand command)
        {
            lock (_stateLock)
            {
                if (_state != ExhibitRuntimeState.Playing)
                    return AipCommandResult.Failure("EXHIBIT_BUSY", "Exhibit capability requires PLAYING state.");
            }
            var cancellation = new CancellationTokenSource();
            _activeCapabilityCancellation = cancellation;
            try
            {
                return await handler.ExecuteAsync(command, cancellation.Token).ConfigureAwait(false)
                    ?? AipCommandResult.Failure("EXHIBIT_CAPABILITY_EMPTY", "Capability handler returned no result.");
            }
            catch (OperationCanceledException)
            {
                return AipCommandResult.Failure("EXHIBIT_CAPABILITY_CANCELLED", "Capability execution was cancelled.");
            }
            catch (Exception error)
            {
                return AipCommandResult.Failure("EXHIBIT_CAPABILITY_FAILED", error.Message);
            }
            finally
            {
                if (ReferenceEquals(_activeCapabilityCancellation, cancellation))
                    _activeCapabilityCancellation = null;
                cancellation.Dispose();
            }
        }

        private async Task<AipCommandResult> ExecuteStartAsync(ExhibitRuntimeCommand command)
        {
            lock (_stateLock)
            {
                if (_state != ExhibitRuntimeState.Idle)
                {
                    return AipCommandResult.Failure("EXHIBIT_BUSY", "Exhibit is not idle.");
                }
                _currentSessionId = command.SessionId;
            }

            try
            {
                await SetStateAsync(ExhibitRuntimeState.Starting, command, null, null, false).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                return AipCommandResult.Failure("EXHIBIT_STATE_PUBLISH_FAILED", error.Message);
            }

            var cancellation = new CancellationTokenSource();
            _activeStartCancellation = cancellation;
            try
            {
                await _adapter.StartAsync(command, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await TransitionToErrorAfterFailedActionAsync(command, "EXHIBIT_START_CANCELED", "Exhibit start was canceled.").ConfigureAwait(false);
                return AipCommandResult.Failure("EXHIBIT_START_CANCELED", "Exhibit start was canceled.");
            }
            catch (Exception error)
            {
                await TrySafeStopAsync(command).ConfigureAwait(false);
                await TransitionToErrorAfterFailedActionAsync(command, "EXHIBIT_START_FAILED", error.Message).ConfigureAwait(false);
                return AipCommandResult.Failure("EXHIBIT_START_FAILED", error.Message);
            }
            finally
            {
                if (ReferenceEquals(_activeStartCancellation, cancellation)) _activeStartCancellation = null;
                cancellation.Dispose();
            }

            ScheduleStartupTimeout(command);
            return Accepted(command, "STARTING");
        }

        private async Task<AipCommandResult> ExecuteStopAsync(ExhibitRuntimeCommand command)
        {
            CancelStartupTimeout();
            lock (_stateLock) _currentSessionId = FirstNonEmpty(command.SessionId, _currentSessionId);
            try
            {
                await _adapter.StopAsync(command, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                try
                {
                    await SetStateAsync(
                        ExhibitRuntimeState.Error,
                        command,
                        "EXHIBIT_STOP_FAILED",
                        error.Message,
                        true).ConfigureAwait(false);
                }
                catch
                {
                    // 保留原始停止失败作为命令结果；状态上报异常不得触发新的实体动作。
                }
                return AipCommandResult.Failure("EXHIBIT_STOP_FAILED", error.Message);
            }

            try
            {
                await SetStateAsync(ExhibitRuntimeState.Idle, command, null, null, true).ConfigureAwait(false);
                return Accepted(command, "IDLE");
            }
            catch (Exception error)
            {
                return AipCommandResult.Failure("EXHIBIT_STATE_PUBLISH_FAILED", error.Message);
            }
        }

        private async Task TrySafeStopAsync(ExhibitRuntimeCommand startCommand)
        {
            try
            {
                await _adapter.StopAsync(new ExhibitRuntimeCommand
                {
                    Action = "stop",
                    OrderNo = startCommand.OrderNo,
                    CommandId = startCommand.CommandId,
                    SessionId = startCommand.SessionId,
                    IdempotencyKey = startCommand.IdempotencyKey + ":safe-stop",
                    Args = new JObject()
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 后续状态会转为 ERROR；安全停止失败不得再次触发实体 start。
            }
        }

        private async Task TransitionToErrorAfterFailedActionAsync(ExhibitRuntimeCommand command, string code, string message)
        {
            try
            {
                await SetStateAsync(ExhibitRuntimeState.Error, command, code, message, true).ConfigureAwait(false);
            }
            catch
            {
                // 命令结果保留原始失败，状态上报异常由宿主日志/健康检查处理。
            }
        }

        private void ScheduleStartupTimeout(ExhibitRuntimeCommand command)
        {
            CancelStartupTimeout();
            var cancellation = new CancellationTokenSource();
            _startupConfirmationCancellation = cancellation;
            _ = ConfirmStartupTimeoutAsync(command, cancellation);
        }

        private async Task ConfirmStartupTimeoutAsync(ExhibitRuntimeCommand command, CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(_options.StartConfirmationTimeout, cancellation.Token).ConfigureAwait(false);
                await _commandGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    lock (_stateLock)
                    {
                        if (_state != ExhibitRuntimeState.Starting || _currentCommandId != command.CommandId) return;
                    }
                    try
                    {
                        await _adapter.StopAsync(new ExhibitRuntimeCommand
                        {
                            Action = "stop",
                            OrderNo = command.OrderNo,
                            CommandId = command.CommandId,
                            SessionId = command.SessionId,
                            IdempotencyKey = command.IdempotencyKey + ":timeout-stop",
                            Args = new JObject()
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        await SetStateAsync(
                            ExhibitRuntimeState.Offline,
                            command,
                            "EXHIBIT_START_TIMEOUT_STOP_FAILED",
                            "Start confirmation timed out and safe stop failed.",
                            true).ConfigureAwait(false);
                        return;
                    }
                    await SetStateAsync(
                        ExhibitRuntimeState.Error,
                        command,
                        "EXHIBIT_START_TIMEOUT",
                        "Exhibit did not confirm PLAYING within the startup timeout.",
                        true).ConfigureAwait(false);
                }
                finally
                {
                    _commandGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // PLAYING/stop canceled the timeout as expected.
            }
            catch
            {
                // 不产生未观察任务异常；运行状态仍由 AIP/健康检查兜底。
            }
            finally
            {
                if (ReferenceEquals(_startupConfirmationCancellation, cancellation))
                {
                    _startupConfirmationCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private async Task SetStateAsync(
            ExhibitRuntimeState next,
            ExhibitRuntimeCommand command,
            string errorCode,
            string errorMessage,
            bool commitBeforePublish)
        {
            ExhibitRuntimeSnapshot snapshot;
            lock (_stateLock)
            {
                if (_state == next)
                {
                    return;
                }
                if (!CanTransition(_state, next))
                {
                    throw new InvalidOperationException("Invalid exhibit state transition: " + _state + " -> " + next);
                }
                snapshot = BuildSnapshot(
                    next,
                    _revision + 1,
                    FirstNonEmpty(command.OrderNo, _currentOrderNo),
                    FirstNonEmpty(command.CommandId, _currentCommandId),
                    FirstNonEmpty(command.SessionId, _currentSessionId),
                    errorCode,
                    errorMessage,
                    _options.UtcNow());
                if (commitBeforePublish) Commit(snapshot);
            }

            if (!commitBeforePublish) await _publisher.PublishAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            if (!commitBeforePublish) lock (_stateLock) Commit(snapshot);
            if (commitBeforePublish)
            {
                try
                {
                    await _publisher.PublishAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    RaiseStateChanged(snapshot);
                }
                return;
            }
            RaiseStateChanged(snapshot);
        }

        private void Commit(ExhibitRuntimeSnapshot snapshot)
        {
            _state = ParseState(snapshot.State);
            _revision = snapshot.Revision;
            _currentOrderNo = snapshot.OrderNo;
            _currentCommandId = snapshot.CommandId;
            _currentSessionId = snapshot.SessionId;
            _currentErrorCode = snapshot.ErrorCode;
            _currentErrorMessage = snapshot.ErrorMessage;
        }

        private ExhibitRuntimeSnapshot BuildSnapshot(
            ExhibitRuntimeState state,
            int revision,
            string orderNo,
            string commandId,
            string sessionId,
            string errorCode,
            string errorMessage,
            DateTimeOffset now)
        {
            return new ExhibitRuntimeSnapshot
            {
                Revision = revision,
                State = ExhibitRuntimeStates.ToAipState(state),
                OrderNo = orderNo,
                CommandId = commandId,
                SessionId = sessionId,
                OccurredAt = now.ToString("O"),
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };
        }

        private void RaiseStateChanged(ExhibitRuntimeSnapshot snapshot)
        {
            var handler = StateChanged;
            if (handler != null) handler(snapshot);
        }

        private static bool CanTransition(ExhibitRuntimeState current, ExhibitRuntimeState next)
        {
            if (current == next) return true;
            switch (current)
            {
                case ExhibitRuntimeState.Idle:
                    return next == ExhibitRuntimeState.Starting || next == ExhibitRuntimeState.Offline;
                case ExhibitRuntimeState.Starting:
                    return next == ExhibitRuntimeState.Playing
                        || next == ExhibitRuntimeState.Idle
                        || next == ExhibitRuntimeState.Error
                        || next == ExhibitRuntimeState.Offline;
                case ExhibitRuntimeState.Playing:
                    return next == ExhibitRuntimeState.Idle
                        || next == ExhibitRuntimeState.Error
                        || next == ExhibitRuntimeState.Offline;
                case ExhibitRuntimeState.Error:
                    return next == ExhibitRuntimeState.Idle || next == ExhibitRuntimeState.Offline;
                case ExhibitRuntimeState.Offline:
                    return next == ExhibitRuntimeState.Idle;
                default:
                    return false;
            }
        }

        private static ExhibitRuntimeState ParseState(string state)
        {
            switch (state)
            {
                case "IDLE": return ExhibitRuntimeState.Idle;
                case "STARTING": return ExhibitRuntimeState.Starting;
                case "PLAYING": return ExhibitRuntimeState.Playing;
                case "ERROR": return ExhibitRuntimeState.Error;
                case "OFFLINE": return ExhibitRuntimeState.Offline;
                default: throw new InvalidOperationException("Unknown runtime state: " + state);
            }
        }

        private static string NormalizeAction(string action)
        {
            var normalized = action == null ? string.Empty : action.Trim().ToLowerInvariant();
            if (normalized == "start") return "start";
            if (normalized == "stop" || normalized == "safety.stop") return "stop";
            return null;
        }

        private static ExhibitRuntimeCommand ToRuntimeCommand(AipCommand command, string action)
        {
            var args = command.Args ?? new JObject();
            return new ExhibitRuntimeCommand
            {
                Action = action,
                Args = args,
                LeaseId = command.LeaseId,
                OrderNo = FirstNonEmpty((string)args["orderNo"], (string)command.Request?["meta"]?["orderNo"]),
                CommandId = FirstNonEmpty(
                    (string)args["commandId"],
                    (string)command.Request?["meta"]?["commandId"],
                    (string)command.Request?["id"]),
                SessionId = FirstNonEmpty(command.SessionId, (string)args["sessionId"]),
                IdempotencyKey = command.IdempotencyKey
            };
        }

        private static ExhibitCommandRecord NewPendingRecord(string key, ExhibitRuntimeCommand command)
        {
            return new ExhibitCommandRecord
            {
                Key = key,
                Action = command.Action,
                OrderNo = command.OrderNo,
                CommandId = command.CommandId,
                SessionId = command.SessionId,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Pending = true
            };
        }

        private static AipCommandResult Replay(ExhibitCommandRecord record, string requestedAction, ExhibitRuntimeCommand command)
        {
            if (!string.Equals(record.Action, requestedAction, StringComparison.Ordinal))
            {
                return AipCommandResult.Failure("EXHIBIT_COMMAND_CONFLICT", "Idempotency key is already bound to another action.");
            }
            if (record.Pending)
            {
                return AipCommandResult.Failure(
                    "AIP_COMMAND_PENDING_REVIEW",
                    "Previous command may have produced a physical side effect; require manual review before retry.");
            }
            if (!record.ResultOk)
            {
                return AipCommandResult.Failure(record.ResultCode ?? "EXHIBIT_COMMAND_FAILED", record.ResultMessage ?? "Exhibit command failed.");
            }
            JToken data = string.IsNullOrWhiteSpace(record.ResultDataJson)
                ? new JObject()
                : JToken.Parse(record.ResultDataJson);
            var body = data as JObject ?? new JObject();
            body["duplicate"] = true;
            return AipCommandResult.Success(body);
        }

        private static void CompleteRecord(ExhibitCommandRecord record, AipCommandResult result)
        {
            record.Pending = false;
            record.CompletedAt = DateTimeOffset.UtcNow.ToString("O");
            record.ResultOk = result != null && result.Ok;
            record.ResultCode = result == null ? "EXHIBIT_RUNTIME_ERROR" : result.ErrorCode;
            record.ResultMessage = result == null ? "Exhibit runtime error." : result.ErrorMessage;
            record.ResultDataJson = result != null && result.Ok && result.Data != null
                ? result.Data.ToString(Formatting.None)
                : null;
        }

        private static AipCommandResult Accepted(ExhibitRuntimeCommand command, string state)
        {
            return AipCommandResult.Success(new JObject
            {
                ["accepted"] = true,
                ["commandId"] = command.CommandId ?? string.Empty,
                ["orderNo"] = command.OrderNo ?? string.Empty,
                ["state"] = state,
                ["duplicate"] = false
            });
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }

        private void CancelPendingActions()
        {
            try { _activeStartCancellation?.Cancel(); } catch { }
            try { _activeCapabilityCancellation?.Cancel(); } catch { }
            CancelStartupTimeout();
        }

        private void CancelStartupTimeout()
        {
            try { _startupConfirmationCancellation?.Cancel(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingActions();
            _commandGate.Dispose();
            _activeStartCancellation?.Dispose();
            _activeCapabilityCancellation?.Dispose();
            _startupConfirmationCancellation?.Dispose();
        }
    }
}