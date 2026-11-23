using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using UnityEngine;

namespace Parful.AndX.Samples
{
    /// <summary>求签机业务能力骨架；真实项目在 ExecuteAsync 中调用硬件 Adapter。</summary>
    public sealed class QiuQianDrawCapabilityHandler : IExhibitCapabilityHandler
    {
        public string Capability { get { return "qiuqian.draw"; } }

        public Task<AipCommandResult> ExecuteAsync(
            ExhibitRuntimeCommand command,
            CancellationToken cancellationToken)
        {
            // 真实现场应调用机械/出签适配器，并尊重 cancellationToken。
            Debug.Log("[AndX AIP] qiuqian.draw order=" + command.OrderNo);
            return Task.FromResult(AipCommandResult.Success(new JObject
            {
                ["accepted"] = true,
                ["capability"] = Capability,
                ["orderNo"] = command.OrderNo
            }));
        }
    }
}
