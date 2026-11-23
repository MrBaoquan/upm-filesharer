using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using Parful.AndX.Unity;
using UnityEngine;

namespace Parful.AndX.Samples
{
    /// <summary>
    /// Low-level AIP host diagnostics sample.
    /// Production exhibits should use ExhibitRuntime and IExhibitRuntimeAdapter.
    /// </summary>
    public sealed class AndXAipHostExample : MonoBehaviour
    {
        public AndXAipBehaviour Aip;

        private void Start()
        {
            if (Aip == null) Aip = FindObjectOfType<AndXAipBehaviour>();
            if (Aip == null) return;
            Aip.SetCommandHandler(HandleCommandAsync);
        }

        private Task<AipCommandResult> HandleCommandAsync(AipCommand command)
        {
            if (command == null)
                return Task.FromResult(AipCommandResult.Failure("AIP_INVALID_REQUEST", "Command is missing."));

            if (command.Action == "safety.stop")
            {
                return Task.FromResult(AipCommandResult.Success(new JObject
                {
                    ["safeStop"] = true
                }));
            }

            var channel = (string)command.Args?["channel"];
            var value = (float?)command.Args?["value"] ?? 1f;
            if (string.IsNullOrEmpty(channel))
            {
                return Task.FromResult(AipCommandResult.Failure(
                    "AIP_INVALID_REQUEST",
                    "Command requires args.channel."));
            }

            Debug.Log($"[AIP] {command.Action} {channel}={value}");
            return Task.FromResult(AipCommandResult.Success(new JObject
            {
                ["dispatched"] = true,
                ["action"] = command.Action,
                ["channel"] = channel
            }));
        }
    }
}
