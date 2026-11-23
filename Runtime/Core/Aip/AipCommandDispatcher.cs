using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Parful.AndX.Aip
{
    public delegate Task<AipCommandResult> AipCommandDelegate(AipCommand command);

    /// <summary>
    /// Converts AIP requests into host-neutral commands. The platform host owns all side effects.
    /// </summary>
    public sealed class AipCommandDispatcher
    {
        private readonly AipCommandDelegate _handler;

        public AipCommandDispatcher(AipCommandDelegate handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public async Task<AipCommandResult> DispatchAsync(AipServerRequest request)
        {
            if (request == null) return AipCommandResult.Failure("AIP_INVALID_REQUEST", "AIP request is missing.");
            if (request.Name == "safety.stop")
            {
                return await _handler(new AipCommand
                {
                    Action = "safety.stop",
                    Args = request.Payload?["data"] as JObject ?? new JObject(),
                    LeaseId = null,
                    SessionId = request.SessionId,
                    IdempotencyKey = (string)request.Raw?["idempotencyKey"],
                    Request = request.Raw
                }).ConfigureAwait(false);
            }

            if (request.Name != "command.invoke")
                return AipCommandResult.Failure("AIP_UNSUPPORTED_PROTOCOL", "Unsupported AIP request: " + request.Name);

            var action = (string)request.Payload?["action"];
            if (string.IsNullOrWhiteSpace(action))
                return AipCommandResult.Failure("AIP_INVALID_REQUEST", "command.invoke requires action.");

            return await _handler(new AipCommand
            {
                Action = action,
                Args = request.Payload?["args"] as JObject ?? new JObject(),
                LeaseId = (string)request.Payload?["leaseId"],
                SessionId = request.SessionId,
                IdempotencyKey = (string)request.Raw?["idempotencyKey"],
                Request = request.Raw
            }).ConfigureAwait(false);
        }
    }
}