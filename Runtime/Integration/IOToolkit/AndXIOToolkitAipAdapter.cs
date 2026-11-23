#if ANDX_IOTOOLKIT_PRESENT
using System;
using System.Threading.Tasks;
using IOToolkit;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using Parful.AndX.Unity;
using UnityEngine;

namespace Parful.AndX.Integration.IOToolkit
{
    /// <summary>
    /// Optional Unity bridge: AIP semantic command -> IODevice host API. It never opens serial ports or
    /// talks to Modbus directly; IODevice remains the sole hardware owner.
    /// </summary>
    [AddComponentMenu("AndX/AIP IOToolkit Adapter")]
    public sealed class AndXIOToolkitAipAdapter : MonoBehaviour
    {
        [SerializeField] private AndXAipBehaviour aip;
        [SerializeField] private string defaultDevice = "Simulator";

        private void Awake()
        {
            if (aip == null) aip = FindObjectOfType<AndXAipBehaviour>();
        }

        private void Start()
        {
            if (aip == null)
            {
                Debug.LogWarning("[AndX IOToolkit] AndXAipBehaviour was not found.");
                return;
            }
            aip.SetCommandHandler(HandleCommandAsync);
        }

        public Task<AipCommandResult> HandleCommandAsync(AipCommand command)
        {
            try
            {
                if (command == null) return Task.FromResult(AipCommandResult.Failure("AIP_INVALID_REQUEST", "Command is missing."));
                if (command.Action == "safety.stop")
                {
                    var safeResult = IODeviceController.EnterSafeState();
                    return Task.FromResult(safeResult > 0
                        ? AipCommandResult.Success(new JObject { ["dispatched"] = true, ["safeStop"] = true })
                        : AipCommandResult.Failure("AIP_HOST_ERROR", "IOToolkit safe stop failed: " + safeResult));
                }

                var args = command.Args ?? new JObject();
                var deviceName = (string)args["device"] ?? defaultDevice;
                var channel = (string)args["channel"] ?? (string)args["channelId"];
                var actionId = (string)args["actionId"];
                var valueToken = args["value"];
                var value = valueToken == null ? 0f : valueToken.Value<float>();
                var device = IODeviceController.GetIODevice(deviceName);
                if (device == null) return Task.FromResult(AipCommandResult.Failure("AIP_DEVICE_OFFLINE", "IOToolkit device was not found: " + deviceName));

                int result;
                if (!string.IsNullOrWhiteSpace(channel))
                    result = device.SetDO(channel, value);
                else if (!string.IsNullOrWhiteSpace(actionId))
                    result = device.SetDO(actionId, value);
                else
                    return Task.FromResult(AipCommandResult.Failure("AIP_INVALID_REQUEST", "Command requires args.channel or args.actionId."));

                if (result <= 0)
                    return Task.FromResult(AipCommandResult.Failure("AIP_HOST_ERROR", "IOToolkit write failed: " + result));
                device.DOImmediate();
                return Task.FromResult(AipCommandResult.Success(new JObject
                {
                    ["dispatched"] = true,
                    ["action"] = command.Action,
                    ["device"] = deviceName,
                    ["channel"] = channel ?? actionId,
                    ["value"] = value
                }));
            }
            catch (Exception exception)
            {
                return Task.FromResult(AipCommandResult.Failure("AIP_HOST_ERROR", exception.Message));
            }
        }
    }
}
#endif