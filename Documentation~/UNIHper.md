# UNIHper 集成

`com.parful.andx` 通过独立的可选程序集接入 UNIHper。未安装 UNIHper 时核心包仍可独立使用；
安装后自动获得 `UConfig` 生命周期配置和 UniRx 业务扩展。

- 适用 UNIHper：`1.25.1108+`
- 运行时程序集：`AndX.Integration.UNIHper`
- Editor 自动注册程序集：`AndX.Integration.UNIHper.Editor`
- 实现命名空间：`Parful.AndX.Integration.UNIHper`
- 业务命名空间：`Parful.AndX`

## 自动配置

Editor 注册器会将 `AndX.Integration.UNIHper` 写入
`Assets/Resources/UNIHper/assemblies.json`。UNIHper 启动时扫描并加载：

```csharp
[SerializedAt(AppPath.Auto, "Configs", "AndX.xml", Priority = 0)]
[SerializeWith(ConfigDriver.XML)]
public sealed class AndXUNIHperSettings : UConfig
```

首次启动生成：

```text
Assets/StreamingAssets/Configs/AndX.xml
```

`AndXUNIHperSettings.OnLoaded()` 会调用 `AndXUNIHperBridge.ApplySettings(this, false)`，
再由 `AndX.Config.InitAuto(options)` 建立全局客户端。因此业务代码**无需显式 Init**：

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
        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(source),
            new UploadOptions { Title = "展项截图" });

        qr.texture = await result.LoadQrTextureAsync();
    }
}
```

## 配置文件

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

- `AccessMode`：`Auto` / `Direct` / `Edge`。
- `Endpoint`：留空时由 AndX 按接入方式读取环境变量或默认基址。
- `ExhibitId`：可选全局展项兜底；`NodeId + NodeSecret` 直连时建议与节点绑定展项一致。
- `NodeId + NodeSecret`：直接使用节点 HMAC 身份；`NodeSecret` 必须与 `NodeId` 配套。
- `EdgeKey`：直连后端或本机 Edge 联调时使用；生产建议由 Edge 代持。
- `AccessToken`：静态登录令牌；运行期刷新优先用 `AccessTokenProvider`。
- `EnrollmentToken`：一次性首启注册令牌，只在显式调用 `InitializeAsync` 时有效。
- `AutoInitialize=false`：由宿主接管初始化。
- `AndX.Config.IsConfigured=true` 时不会覆盖已有配置。

开发期允许在 `AndX.xml` 明文填写上述凭据，便于现场交接；生产包不得携带真实密钥，
应改用环境变量、部署凭据或宿主注入。配置文件与日志都不得输出密钥原文。

配置路径使用 UNIHper 的 `AppPath.Auto`，与框架行为一致：编辑器/桌面构建读取
`StreamingAssets/Configs/AndX.xml`，Android/WebGL 使用平台可写目录。打包后修改 XML 即可生效，
无需重新编译；运行时可打印 `AndXUNIHperBridge.ConfigFilePath` 定位当前文件。

密钥按调用节点分配：

- **展项节点**：一台展项机一把密钥，只能代表绑定的展项。
- **项目中控节点**：一个中控实例一把密钥，归属项目，通过授权集合代表多个展项。
- 微信支付/AI 等项目级第三方凭据属于配置中心，不通过节点密钥下发。

遗忘密钥时，在管理后台节点列表点击「查看/复制」，可直接复制密钥或 `AndX.xml` 接入片段，
无需先轮换。注册令牌不同，仍然只在签发时显示一次。

项目级开发交接可在「节点与接入密钥」页导出 CSV，包含展项 ID、`P{项目编号}-E{展项编号}`、
节点信息与当前密钥。导出前必须输入项目编号确认；导出动作写审计，文件含明文，禁止提交 Git 或上传公共网盘。

运行时 Token 仍可通过宿主注入：

```csharp
AndXUNIHperBridge.ConfigureOptions = options =>
{
    options.AccessTokenProvider = () => currentUserToken;
};
```

## 运行时重载

外部修改 `AndX.xml` 后，默认下次启动生效；需要立即生效时：

```csharp
AndXUNIHperBridge.ReloadAndApply();
```

不要在请求尚未结束时重载。状态与排障信息：

```csharp
AndXUNIHperBridge.IsReady;
AndXUNIHperBridge.LastError;
AndXUNIHperBridge.ConfigFilePath;
AndX.Config.Describe();
```

## 示例场景

导入 Package Manager 中的 **AndX UNIHper Integration**，打开 `AndXUNIHperDemo` 场景并 Play。
`AndXUNIHperDemoScript` 由 UNIHper 按「场景名 + Script」自动发现，不挂在 GameObject 上：

```text
场景：AndXUNIHperDemo.unity
脚本：AndXUNIHperDemoScript : SceneScriptBase
```

示例界面是 `UIToolkitBase` 页面，由 `UIToolkitManager` 创建和显示，资源为
`Resources/UIToolkit/AndXUNIHperDemoUI.uxml/.uss`。示例不调用 `AndX.Config.Init`，
进入场景后自动执行一次 `AndX.Share.UploadAsObservable` 并显示二维码。

页面覆盖分享、扫码解析、下载授权、付费完整流程、订单查询、AI 文字和显式 AI 生图入口，
并显示配置摘要、配置文件路径与操作日志。所有能力按钮都有 `action-selected` 选中态，
标签卡、状态和操作日志会明确反映当前动作。

付费示例按三步展示：

1. 点击「签付费码」，展示支付二维码。
2. 扫码支付后，服务端产生 `start` 兜底指令。
3. 点击「付费开始游戏」，端侧调用 `AndX.Node.HeartbeatAsObservable(PaidExhibitId())`；
   收到 `action=start` 后先上报 `STARTING`，500ms 后上报 `PLAYING`；演示保留 3 秒后自动上报 `IDLE`，
   复位完成后才能再次签付费码，避免展项长期停留在 `PLAYING`。

若尚未收到支付启动指令，示例以 `PAYMENT_REQUIRED` 提示当前仍在第 2 步。Hub 节点必须传入
服务端已授权的展项 ID；展项节点可省略并以自身绑定展项为准。测试项目默认 `Exhibit ID=2`、
付费展项 ID=`6`，均可在页面中修改。

SDK 当前请求字段仍是内部 `exhibitId`；运营配置建议使用 `P{项目码}-E{展项码}`，
编码到内部 ID 的解析应由后续业务契约补齐，不能直接把业务编码当作主键传给现接口。

若工程已有常驻 `UNIHperEntry`，示例直接复用；否则 `AndXUNIHperDemoBootstrap` 会实例化
工程 `Resources/UNIHper/Prefabs/UNIHper`，使示例场景可以从任意入口直接 Play。

## 扫码收图（反向上传）

插件用一次业务调用即可完成“出普通链接二维码 → 等待用户上传 → 下载图片并展示”：

```csharp
AndX.Share.ReceiveImageAsObservable(new ReceiveUploadOptions
{
    ExhibitId = "6",
    Title = "请扫码上传一张图片",
    Timeout = TimeSpan.FromMinutes(3)
})
.Subscribe(state =>
{
    if (state.QrTexture != null)
        qrImage.texture = state.QrTexture;

    if (state.ImageTexture != null)
        preview.texture = state.ImageTexture;

    status.text = state.StatusText;
}, error => status.text = error.Message)
.AddTo(LifeCycleDisposables);
```

入口约定：

- 二维码内容固定为普通 HTTPS 链接：`https://{域名}/u/{token}`。
- 微信通过小程序后台 **“扫普通链接二维码打开小程序”** 规则进入 `pages/upload/index`。
- 不使用小程序码。
- 普通浏览器打开同一个 `/u/{token}` 时使用 H5 上传页兜底。
- 小程序主入口使用 `wx.chooseMedia` / `wx.compressImage` / `wx.uploadFile`；H5 使用普通文件上传。
- 两端共用同一个 token/session，首次上传成功后会话锁定，另一端不能重复提交。

默认边界：

```text
仅单张图片
JPEG / PNG / WebP
15 MB
会话 180 秒
服务端统一转码为 JPEG、纠正方向并移除 EXIF
插件每秒轮询，Dispose 时停止
```

底层 Task 接口仍可直接使用：

```csharp
var session = await AndX.Share.CreateReceiveUploadSessionAsync(options);
var current = await AndX.Share.GetReceiveUploadSessionAsync(session.SessionId);
await AndX.Share.CancelReceiveUploadSessionAsync(session.SessionId);
```

## UniRx

UNIHper 自带 UniRx。AndX 在 `Parful.AndX` 命名空间提供与 Task 接口一一对应的扩展方法：

```csharp
using Parful.AndX;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

AndX.AI.GetCapabilitiesAsObservable()
    .Subscribe(
        caps => status.text = caps.Available ? "AI 可用" : caps.Reason,
        error => Debug.LogError(error))
    .AddTo(this);
```

语义：

1. **冷流**：每次订阅才启动一次业务调用。
2. **可取消**：`Dispose()` 会取消传入底层 Task 的 `CancellationToken`。
3. **主线程**：`OnNext` / `OnError` / `OnCompleted` 统一通过 `ObserveOnMainThread()` 回调。
4. **不复制核心 API**：扩展仅做 `Task → Observable` 适配，服务逻辑仍只有一份。

常用映射：

| Task | UniRx |
| --- | --- |
| `AndX.Share.UploadAsync` | `AndX.Share.UploadAsObservable` |
| `AndX.Share.ResolveAsync` | `AndX.Share.ResolveAsObservable` |
| `AndX.Share.GetDownloadAsync` | `AndX.Share.GetDownloadAsObservable` |
| `AndX.Pay.CreateTicketAsync` | `AndX.Pay.CreateTicketAsObservable` |
| `AndX.Pay.QueryOrderAsync` | `AndX.Pay.QueryOrderAsObservable` |
| `AndX.AI.GenerateImageAsync` | `AndX.AI.GenerateImageAsObservable` |
| `AndX.AI.GenerateTextAsync` | `AndX.AI.GenerateTextAsObservable` |
| `AndX.Node.HeartbeatAsync(exhibitId)` | `AndX.Node.HeartbeatAsObservable(exhibitId)` |
| `AndX.Node.ReportStateAsync(...)` | `AndX.Node.ReportStateAsObservable(...)` |

## 首次注册

只有首次注册节点身份时才需要显式异步调用：

```csharp
await AndXUNIHperBridge.InitializeAsync();
```

令牌通过环境变量注入：

```text
ANDX_ENROLLMENT_TOKEN=<一次性令牌>
ANDX_NODE_ID=<节点编号>
```

注册完成后凭据由 UNIHper/AndX 的安全存储管理，后续启动自动初始化。

## 排障

- 配置未生成：检查 UNIHper 是否初始化，以及 `assemblies.json` 是否包含 `AndX.Integration.UNIHper`。
- 初始化失败：读取 `AndXUNIHperBridge.LastError`，检查 `AndX.xml`、节点凭据与 AndXEdge 状态。
- 模式异常：打印 `AndX.Config.Describe()`，不要输出任何密钥。
