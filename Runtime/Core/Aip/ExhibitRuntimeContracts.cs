using System;
using System.Threading;
using System.Threading.Tasks;

namespace Parful.AndX.Aip
{
    /// <summary>AIP 冻结的展项运行状态；不得自行扩展为厂商 IO 状态。</summary>
    public enum ExhibitRuntimeState
    {
        Idle,
        Starting,
        Playing,
        Error,
        Offline
    }

    public static class ExhibitRuntimeStates
    {
        public static string ToAipState(ExhibitRuntimeState state)
        {
            switch (state)
            {
                case ExhibitRuntimeState.Idle: return "IDLE";
                case ExhibitRuntimeState.Starting: return "STARTING";
                case ExhibitRuntimeState.Playing: return "PLAYING";
                case ExhibitRuntimeState.Error: return "ERROR";
                case ExhibitRuntimeState.Offline: return "OFFLINE";
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }
    }

    /// <summary>宿主对业务动作的标准化视图；不暴露原始 IO/通道。</summary>
    public sealed class ExhibitRuntimeCommand
    {
        public string Action;
        public string OrderNo;
        public string CommandId;
        public string SessionId;
        public string IdempotencyKey;
        public string LeaseId;
        public Newtonsoft.Json.Linq.JObject Args;
    }

    public sealed class ExhibitRuntimeSnapshot
    {
        public int Revision;
        public string State;
        public string OrderNo;
        public string CommandId;
        public string SessionId;
        public string OccurredAt;
        public string ErrorCode;
        public string ErrorMessage;
    }

    /// <summary>平台宿主负责实体副作用；实现必须尊重取消，并把 stop 作为安全优先动作。</summary>
    /// <summary>可选业务能力 Handler；只接受已由服务端能力清单授权的 semantic action。</summary>
    public interface IExhibitCapabilityHandler
    {
        string Capability { get; }

        Task<AipCommandResult> ExecuteAsync(
            ExhibitRuntimeCommand command,
            CancellationToken cancellationToken);
    }

    public interface IExhibitRuntimeAdapter
    {
        Task StartAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken);

        Task StopAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken);
    }

    public interface IExhibitStatePublisher
    {
        Task PublishAsync(ExhibitRuntimeSnapshot snapshot, CancellationToken cancellationToken);
    }

    public sealed class ExhibitCommandRecord
    {
        public string Key;
        public string Action;
        public string OrderNo;
        public string CommandId;
        public string SessionId;
        public string CreatedAt;
        public string CompletedAt;
        public bool ResultOk;
        public string ResultCode;
        public string ResultMessage;
        public string ResultDataJson;
        public bool Pending;

        public ExhibitCommandRecord Clone()
        {
            return new ExhibitCommandRecord
            {
                Key = Key,
                Action = Action,
                OrderNo = OrderNo,
                CommandId = CommandId,
                SessionId = SessionId,
                CreatedAt = CreatedAt,
                CompletedAt = CompletedAt,
                ResultOk = ResultOk,
                ResultCode = ResultCode,
                ResultMessage = ResultMessage,
                ResultDataJson = ResultDataJson,
                Pending = Pending
            };
        }
    }

    /// <summary>命令去重存储；至少保留 7 天或最近 100 条。</summary>
    public interface IExhibitCommandStore
    {
        Task<ExhibitCommandRecord> GetAsync(string key, CancellationToken cancellationToken = default(CancellationToken));

        Task SaveAsync(ExhibitCommandRecord record, CancellationToken cancellationToken = default(CancellationToken));
    }

    public sealed class ExhibitRuntimeOptions
    {
        public string ExhibitId;
        public IExhibitRuntimeAdapter Adapter;
        public IExhibitStatePublisher StatePublisher;
        public IExhibitCommandStore CommandStore;
        public IExhibitCapabilityHandler[] CapabilityHandlers = new IExhibitCapabilityHandler[0];
        public TimeSpan StartConfirmationTimeout = TimeSpan.FromSeconds(120);
        public Func<DateTimeOffset> UtcNow = () => DateTimeOffset.UtcNow;
    }

    /// <summary>把运行状态投影到 AIP state.publish；不改变 action/state 语义。</summary>
    public sealed class AipExhibitStatePublisher : IExhibitStatePublisher
    {
        private readonly AipClient _client;

        public AipExhibitStatePublisher(AipClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task PublishAsync(ExhibitRuntimeSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(snapshot.SessionId))
            {
                throw new InvalidOperationException("AIP state.publish requires a sessionId.");
            }
            var data = new Newtonsoft.Json.Linq.JObject
            {
                ["commandId"] = snapshot.CommandId ?? string.Empty,
                ["orderNo"] = snapshot.OrderNo ?? string.Empty,
                ["occurredAt"] = snapshot.OccurredAt
            };
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorCode) || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                data["error"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["code"] = snapshot.ErrorCode ?? "EXHIBIT_RUNTIME_ERROR",
                    ["message"] = snapshot.ErrorMessage ?? "Exhibit runtime error."
                };
            }
            await _client.PublishStateAsync(
                snapshot.State,
                data,
                snapshot.Revision,
                snapshot.SessionId,
                cancellationToken).ConfigureAwait(false);
        }
    }
}