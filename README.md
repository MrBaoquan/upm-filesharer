# AndX

Unity 展项能力包：上传图片/视频/AI 产物并出码，支持扫码预览、下载、付费与 AI 生成。

- 包 ID：`com.parful.andx` · 版本：`2.4.1` · 最低 Unity：`2020.3`
- 平台：Windows / Android / WebGL
- 发布详情：<http://upm.andcrane.com:4873/-/web/detail/com.parful.andx>
- UNIHper 集成：`1.25.1108+`（可选）
- 完整文档：[Documentation~/usage.md](Documentation~/usage.md)
- 求签机交付：[Documentation~/qiuqianji-integration.md](Documentation~/qiuqianji-integration.md)

## 最小接入

### 独立使用

不依赖 UNIHper，初始化一次后直接调用业务门面：

```csharp
using Parful.AndX;
using Parful.AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

public sealed class UploadExample : MonoBehaviour
{
    [SerializeField] private Texture2D source;
    [SerializeField] private RawImage qr;

    private async void Start()
    {
        AndX.Config.InitLocal(); // 经本机 AndXEdge；也可用 InitAuto 连接后端

        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(source),
            new UploadOptions { Title = "展项截图" });

        qr.texture = await result.LoadQrTextureAsync();
    }
}
```

### UNIHper 工程

安装 UNIHper 后，`AndX.Integration.UNIHper` 自动注册，首次启动生成
`Assets/StreamingAssets/Configs/AndX.xml`，并由 `AndXUNIHperSettings.OnLoaded()`
自动初始化 `AndX.Config`。**业务代码不再调用任何 Init。**

```csharp
using Parful.AndX;
using Parful.AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

public sealed class UNIHperUploadExample : MonoBehaviour
{
    [SerializeField] private Texture2D source;
    [SerializeField] private RawImage qr;

    private async void Start()
    {
        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(source),
            new UploadOptions { Title = "展项截图" });

        qr.texture = await result.LoadQrTextureAsync();
    }
}
```

外部直接修改 `AndX.xml` 即可切换服务域名和接入方式，无需改代码：

```xml
<AndXUNIHperSettings
  SchemaVersion="1"
  AutoInitialize="true"
  AccessMode="Auto"
  Endpoint=""
  ExhibitId=""
  NodeId=""
  NodeSecret=""
  EdgeKey=""
  AccessToken=""
  EnrollmentToken=""
  TimeoutSeconds="30"
  MaxRetries="2"
  ChunkSize="8388608"
  AllowInsecureHttp="false" />
```

开发期可直接在 `AndX.xml` 填写节点密钥，便于现场交接：

```xml
<AndXUNIHperSettings
  SchemaVersion="1"
  AccessMode="Direct"
  NodeId="DEV-A-001"
  NodeSecret="开发节点密钥"
  ExhibitId="2" />
```

- `NodeSecret` 必须与 `NodeId` 配套；密钥归属由后台节点决定：展项节点归属单个展项，中控节点归属项目并可授权多个展项。
- 配置路径跟随 UNIHper 的 `AppPath.Auto`：编辑器与桌面构建读取 `StreamingAssets/Configs/AndX.xml`，Android/WebGL 使用框架对应的可写目录；打包后仍可直接修改该 XML。
- 管理后台支持随时查看、复制节点密钥和 `AndX.xml` 接入片段，查看动作会留审计。
- 管理后台支持按项目导出开发交接 CSV：展项 ID、`P{项目编号}-E{展项编号}`、节点与密钥，可直接发给开发成员。
- 开发配置允许明文；生产包不要携带真实密钥，改用环境变量、部署凭据或 `AccessTokenProvider`。
- `EnrollmentToken` 只在显式调用 `InitializeAsync` 时使用，普通自动启动不走异步注册。

运行时重载：

```csharp
AndXUNIHperBridge.ReloadAndApply();
```

## UNIHper 示例场景

Package Manager 导入 **AndX UNIHper Integration** 后，打开 `AndXUNIHperDemo` 场景直接 Play。
场景脚本 `AndXUNIHperDemoScript` 使用 UNIHper 的 `SceneScriptBase` 生命周期；界面由
`UIToolkitManager` 管理，源文件为 `AndXUNIHperDemoUI.uxml/.uss`。业务代码不调用任何
`AndX.Config.Init`，配置由 `AndXUNIHperSettings.OnLoaded()` 自动完成。

场景进入后**自动上传测试纹理并显示二维码**，无需填写参数。页面同时覆盖主要能力：

- **分享**：上传并出码、扫码解析、下载授权。
- **扫码收图**：`AndX.Share.ReceiveImageAsObservable` 创建普通 HTTPS 二维码；微信按普通链接规则进入小程序上传页，其他环境打开同一 URL 的 H5 兜底页；上传完成后自动下载并显示图片。
- **付费完整流程**：签付费码 → 扫码支付 →「付费开始游戏」；收到 `start` 指令后依次上报 `STARTING`、`PLAYING`，演示 3 秒后自动上报 `IDLE`，可直接重复演示。
- **节点状态**：`AndX.Node.HeartbeatAsync(exhibitId)` 拉取兜底指令，`ReportStateAsync` 上报状态；均提供 UniRx 版本。
- **AI**：AI 文字自动生成；AI 生图作为显式按钮，避免启动时产生额外费用。
- **交互反馈**：能力按钮具有明确的选中/运行态，自动任务不会覆盖用户最后一次操作提示。
- **配置**：显示配置摘要和路径，支持运行时重载 `AndX.xml`。
- **Exhibit ID**：测试项目默认 `2`，展项机可留空，Hub 节点可改成服务端已授权展项。

> SDK 当前接口仍以内部 `exhibitId` 为传输字段；运营侧推荐展示 `P{项目码}-E{展项码}`，由后续契约提供编码到内部 ID 的解析。

## UniRx 接口

安装 UNIHper 后，所有公开异步业务接口都提供 `*AsObservable` 扩展，命名与 Task 接口一一对应。
扩展位于 `Parful.AndX` 命名空间；订阅时启动调用，`Dispose()` 取消底层请求，回调自动切回 Unity 主线程。

```csharp
using Parful.AndX;
using UniRx;
using UnityEngine;

AndX.Share.UploadAsObservable(
        new TexturePayload(source),
        new UploadOptions { Title = "展项截图" })
    .SelectMany(result => result.LoadQrTextureAsync().ToObservable())
    .Subscribe(texture => qr.texture = texture, error => Debug.LogError(error))
    .AddTo(this);
```

```csharp
using System.Collections.Generic;
using Parful.AndX;
using UniRx;
using UnityEngine;

AndX.Pay.CreateTicketAsObservable().Subscribe(ticket => Debug.Log(ticket.ShareUrl));
AndX.AI.GenerateTextAsObservable("acrostic", new Dictionary<string, object> { ["keyword"] = "春风" })
    .Subscribe(result => status.text = result.Text)
    .AddTo(this);
```

### 扫码收图

```csharp
AndX.Share.ReceiveImageAsObservable(new ReceiveUploadOptions
{
    Title = "请扫码上传一张图片",
    Timeout = TimeSpan.FromMinutes(3)
})
.Subscribe(state =>
{
    if (state.QrTexture != null) qrImage.texture = state.QrTexture;
    if (state.ImageTexture != null) preview.texture = state.ImageTexture;
    status.text = state.StatusText;
}, error => status.text = error.Message)
.AddTo(LifeCycleDisposables);
```

二维码内容固定为普通 HTTPS 链接 `{PUBLIC_BASE_URL}/u/{token}`。微信通过后台“扫普通链接二维码打开小程序”规则进入
`pages/upload/index`；普通浏览器打开同一 URL 时自动使用 H5 兜底页。

## AIP 展项运行时

完整接入说明、Adapter 示例、初始化、状态机、错误码和验收清单见 [`Documentation~/usage.md` 第 16 章](Documentation~/usage.md#16-aip-展项运行时对接求签机--unity)。

付费履约只接受 AIP 的 `start`、`stop` 和 `safety.stop`。Unity 侧实现 `IExhibitRuntimeAdapter` 后，由运行时统一处理命令去重、状态机和安全停止：

```csharp
using Parful.AndX.Aip;
using Parful.AndX.Unity;

AndX.ExhibitRuntime.Configure(new ExhibitRuntimeOptions
{
    ExhibitId = "exhibit-qiuqianji",
    Adapter = new QiuQianAdapter(),
    CommandStore = UnityExhibitCommandStore.Create("exhibit-qiuqianji"),
    StartConfirmationTimeout = TimeSpan.FromSeconds(120)
});

var aip = new AipClient(new AipClientOptions
{
    DeviceId = "DEV-QQJ-001",
    Secret = nodeSecret,
    ExhibitId = "exhibit-qiuqianji"
});
AndX.ExhibitRuntime.BindAipClient(aip);
await aip.ConnectAsync();
```

- `start` 成功只进入 `STARTING`；实体动作真正可交互后调用 `NotifyPlayingAsync`。
- 互动结束调用 `NotifyCompletedAsync`；超时未确认 `PLAYING` 时自动安全停止并进入 `ERROR`。
- `safety.stop` 会取消进行中的 start，并由 Adapter 执行幂等停止。
- 命令在执行前先写入持久化 pending 记录；如果进程在实体动作后崩溃，重启后要求人工复核，不自动重放。
- 默认保留最近 100 条且至少 7 天；禁止通过本门面下发 IO、串口、Modbus 或 PluginChannel。
- 业务语义能力通过 `ExhibitRuntimeOptions.CapabilityHandlers` 注册；Handler 只在 `PLAYING` 执行，必须服从服务端 Manifest 授权、Schema 校验和 `safety.stop` 取消。

## 常用接口

| 能力 | Task | UniRx |
| --- | --- | --- |
| 上传出码 | `AndX.Share.UploadAsync` | `AndX.Share.UploadAsObservable` |
| 扫码解析 | `AndX.Share.ResolveAsync` | `AndX.Share.ResolveAsObservable` |
| 下载授权 | `AndX.Share.GetDownloadAsync` | `AndX.Share.GetDownloadAsObservable` |
| 付费票据 | `AndX.Pay.CreateTicketAsync` | `AndX.Pay.CreateTicketAsObservable` |
| 订单查询 | `AndX.Pay.QueryOrderAsync` | `AndX.Pay.QueryOrderAsObservable` |
| AI 生图 | `AndX.AI.GenerateImageAsync` | `AndX.AI.GenerateImageAsObservable` |
| AI 文字 | `AndX.AI.GenerateTextAsync` | `AndX.AI.GenerateTextAsObservable` |
| 节点心跳 | `AndX.Node.HeartbeatAsync(exhibitId)` | `AndX.Node.HeartbeatAsObservable(exhibitId)` |
| 节点状态 | `AndX.Node.ReportStateAsync(...)` | `AndX.Node.ReportStateAsObservable(...)` |
| 扫码收图 | `AndX.Share.CreateReceiveUploadSessionAsync` | `AndX.Share.ReceiveImageAsObservable` |

开发期允许在 `AndX.xml` 明文填写节点 Secret，便于现场交接；生产包必须移除真实密钥并改用环境变量、部署系统或安全存储注入。任何密钥都不得写入场景、代码、Git 或日志。
