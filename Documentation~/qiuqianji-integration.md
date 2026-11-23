# 求签机 Unity 插件接入交接

> 适用包：`com.parful.andx` `2.4.1`  
> 协议：AndX AIP `v1.0` / Fulfillment `andx-fulfillment/1.0`  
> 推荐平台：Unity Editor / Windows Standalone / Android  
> 完整 API 与错误码：[usage.md](usage.md)

## 1. 交付内容

| 内容 | 说明 |
|---|---|
| `com.parful.andx-2.4.1.tgz` | Unity Package Manager 的 tarball 安装包 |
| `com.parful.andx/` | 可直接嵌入 `Packages/` 的完整包目录 |
| `usage.md` | 插件完整使用文档 |
| `qiuqianji-integration.md` | 本文档，求签机最小接入与验收 |
| AIP Schema / Fulfillment Profile | 联调时可核对消息字段，但业务代码不直接拼协议 |

## 2. 插件已提供的能力

- AIP WebSocket 连接、设备签名、20 秒 KeepAlive、请求超时和 1–30 秒指数退避重连；
- `ExhibitRuntime` 统一状态机：`IDLE → STARTING → PLAYING → IDLE`；
- `start`、`stop`、`safety.stop` 命令处理；
- 同一 `commandId` / `idempotencyKey` 去重；
- 120 秒未确认 `PLAYING` 自动安全停止；
- 命令执行前持久化 pending 记录，崩溃后禁止自动重放；
- 状态变更通过 AIP `state.publish` 单调 revision 上报；
- 实体动作适配接口 `IExhibitRuntimeAdapter`；
- Windows/Android 持久化命令库；
- 可选 IOToolkit Adapter，但求签机应优先实现自己的 Adapter；
- `IExhibitCapabilityHandler` 能力路由，支持 `qiuqian.draw` 等业务动作。

## 3. 开发方需要实现的部分

插件不拥有求签机硬件，因此开发方只需完成：

1. **硬件 Adapter**：实现 `IExhibitRuntimeAdapter`。
2. **完成检测**：实体真正可交互后调用 `NotifyPlayingAsync`。
3. **自然结束**：出签和互动结束、机械复位后调用 `NotifyCompletedAsync`。
4. **安全停止**：`StopAsync` 必须幂等，优先保证机械和出签安全。
5. **生产凭据保护**：Windows 建议使用 DPAPI 实现 `ICredentialProtector`，不要把节点密钥写进场景或代码。
6. **真机验收**：支付 → 出签 → PLAYING → IDLE → 管理员停止。

## 4. 安装

### 方式 A：Package Manager Tarball

Package Manager → `+` → **Add package from tarball...** → 选择 `com.parful.andx-2.4.1.tgz`。

### 方式 B：Embedded Package

把 `com.parful.andx/` 放到目标工程：

```text
YourUnityProject/
  Packages/
    com.parful.andx/
      package.json
      Runtime/
      Documentation~/
```

包依赖 `com.unity.nuget.newtonsoft-json` `3.2.1`，由 Unity 自动解析。

## 5. 最小接入代码

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Parful.AndX.Aip;
using Parful.AndX.Unity;
using UnityEngine;

public sealed class QiuQianAdapter : IExhibitRuntimeAdapter
{
    private readonly QiuQianHardware hardware;

    public QiuQianAdapter(QiuQianHardware hardware)
    {
        this.hardware = hardware;
    }

    public Task StartAsync(
        ExhibitRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        // 只触发求签动作；不要在这里假设已经进入 PLAYING。
        return hardware.StartDrawAsync(command.OrderNo, cancellationToken);
    }

    public Task StopAsync(
        ExhibitRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        // 必须幂等；重复调用不能造成二次出签或二次机械动作。
        return hardware.SafeStopAsync(command.OrderNo, cancellationToken);
    }
}

public sealed class QiuQianBootstrap : MonoBehaviour
{
    [SerializeField] private QiuQianHardware hardware;
    [SerializeField] private string exhibitId;
    [SerializeField] private string endpoint = "wss://andx.andcrane.com/api/aip";

    private AipClient aip;
    private AipConnectionSupervisor connection;
    private Action<ExhibitRuntimeSnapshot> onStateChanged;

    private async void Start()
    {
        var credential = NodeCredentialStorage.Create().Load();
        if (credential == null)
        {
            Debug.LogError("缺少节点凭据");
            return;
        }

        AndX.ExhibitRuntime.Configure(new ExhibitRuntimeOptions
        {
            ExhibitId = exhibitId,
            Adapter = new QiuQianAdapter(hardware),
            CommandStore = UnityExhibitCommandStore.Create(exhibitId),
            StartConfirmationTimeout = TimeSpan.FromSeconds(120)
        });

        aip = new AipClient(new AipClientOptions
        {
            Endpoint = endpoint,
            DeviceId = credential.NodeId,
            Secret = credential.Secret,
            ExhibitId = exhibitId,
            RequestTimeoutMs = 30000
        });

        AndX.ExhibitRuntime.BindAipClient(aip);
        connection = new AipConnectionSupervisor(aip);
        connection.ReconnectFailed += error => Debug.LogWarning($"AIP reconnect: {error.Message}");
        connection.Start();
        onStateChanged = snapshot =>
            Debug.Log($"AIP {snapshot.State} revision={snapshot.Revision}");
        AndX.ExhibitRuntime.StateChanged += onStateChanged;
        await aip.ConnectAsync();
    }

    // 实体和互动画面真正可操作时调用。
    public Task MarkPlaying()
    {
        return AndX.ExhibitRuntime.NotifyPlayingAsync();
    }

    // 出签完成并完成安全复位后调用。
    public Task MarkCompleted()
    {
        return AndX.ExhibitRuntime.NotifyCompletedAsync();
    }

    private void OnDestroy()
    {
        if (onStateChanged != null)
            AndX.ExhibitRuntime.StateChanged -= onStateChanged;
        connection?.Dispose();
        aip?.Dispose();
    }
}
```

## 6. 业务能力路由（Capability Handler）

固定生命周期仍然只有 `start`、`stop`、`safety.stop`。求签、解签等业务动作必须通过版本化 Capability Manifest 和服务端授权，不能复用生命周期 action。

```csharp
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;

public sealed class QiuQianDrawCapabilityHandler : IExhibitCapabilityHandler
{
    private readonly QiuQianHardware hardware;

    public QiuQianDrawCapabilityHandler(QiuQianHardware hardware)
    {
        this.hardware = hardware;
    }

    public string Capability { get { return "qiuqian.draw"; } }

    public async Task<AipCommandResult> ExecuteAsync(
        ExhibitRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        var drawNo = await hardware.DrawAsync(command.OrderNo, cancellationToken);
        return AipCommandResult.Success(new JObject
        {
            ["accepted"] = true,
            ["capability"] = Capability,
            ["orderNo"] = command.OrderNo,
            ["drawNo"] = drawNo
        });
    }
}
```

注册到运行时：

```csharp
AndX.ExhibitRuntime.Configure(new ExhibitRuntimeOptions
{
    ExhibitId = exhibitId,
    Adapter = new QiuQianAdapter(hardware),
    CapabilityHandlers = new IExhibitCapabilityHandler[]
    {
        new QiuQianDrawCapabilityHandler(hardware)
    },
    CommandStore = UnityExhibitCommandStore.Create(exhibitId),
    StartConfirmationTimeout = TimeSpan.FromSeconds(120)
});
```

约束：

- 服务端必须先激活对应 Capability Manifest，并校验能力开关、参数 Schema 和履约 session/lease；
- Handler 只在 `PLAYING` 状态执行，非 `PLAYING` 返回 `EXHIBIT_BUSY`；
- `safety.stop` 会取消进行中的 Handler，硬件实现必须尊重 `CancellationToken`；
- Handler 返回语义结果，不直接发送串口、Modbus、SetDO 或 PluginChannel 原始 IO；
- 同一 `commandId` / `idempotencyKey` 不重复执行。

## 7. 状态与责任边界

```text
支付成功
  → AndX 服务端下发 start
  → Unity Adapter.StartAsync
  → STARTING
  → 真机可交互后 NotifyPlayingAsync
  → PLAYING
  → 出签完成并复位后 NotifyCompletedAsync
  → IDLE
```

- 管理端退款、管理员停止或安全兜底会下发 `stop` / `safety.stop`。
- `command.result.ok=true` 只代表命令已被 Adapter 接收，不代表实体动作完成。
- 禁止在 AIP 消息中直接传串口、Modbus、`SetDO`、`PluginChannel` 等原始 IO 指令。
- 超过 120 秒未进入 `PLAYING`，服务端会判定启动超时并按配置自动退款。

## 8. 联调前必须确认

- [ ] 后台节点已注册、启用并绑定求签机展项；
- [ ] 节点 `DeviceId`、密钥、`ExhibitId` 与后台一致；
- [ ] Unity 能连接 `wss://andx.andcrane.com/api/aip`；
- [ ] 密钥未进入 Git、场景、日志和打包目录；
- [ ] Adapter 的 Start/Stop 可取消、幂等、可安全停止；
- [ ] 实际可交互后才调用 `NotifyPlayingAsync`；
- [ ] 出签完成后调用 `NotifyCompletedAsync`；
- [ ] 断网、重复命令、应用重启和人工停止均已演练；
- [ ] 真机完成一次完整支付履约并通过 A11 验收。

## 9. 验证命令

```powershell
npm --prefix <AndX仓库>/andx-sdk-spec run validate:exhibit-interaction
dotnet test unity/com.parful.andx/Tests~/AndXCore.Tests/AndXCore.Tests.csproj -c Release
```

插件当前自动化基线：AIP 29/29、Fulfillment 10/10、Capability 6/6，Unity Core 136/136，Unity EditMode 15/15、PlayMode 4/4，Unity 2022.1 Editor 脚本编译通过。真实求签机硬件动作仍必须由展项开发方和现场完成验收。
