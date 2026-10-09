# AndX

AndX 展项能力平台 Unity 包。把本地图片/视频上传到 AndX 平台并生成二维码；用户扫码即可预览、下载，付费资源按服务端定价走扫码支付。

- 包 ID：`com.parful.andx`
- 最低 Unity：`2020.3`
- 依赖：`com.unity.nuget.newtonsoft-json`（包已声明，自动拉取）
- 支持平台：Windows / Android / WebGL
- 完整使用文档：`docs/usage.md`

## 安装

Package Manager → **Add package from git URL**：

```
https://github.com/MrBaoquan/upm-filesharer.git
```

## 快速开始

```csharp
using AndX;
using AndX.Unity;

// 1) 零参数接入本机 AndXEdge（鉴权、展项标识、公网地址均由 Edge 代持，插件不持密钥）
AndX.Config.InitLocal();                       // 默认 http://127.0.0.1:6699
// 需要时显式指定：AndX.Config.Init(new AndXOptions { Endpoint = "http://127.0.0.1:6699" });

// 2) 上传素材并拿到二维码（exhibitId 可省略）
ShareResult result = await AndX.Share.UploadAsync(
    new TexturePayload(screenshot),                                        // 或 FilePathPayload / ByteArrayPayload
    new UploadOptions { Title = "展项截图" },
    progress: p => Debug.Log($"[AndX] {p.Percent:P0}"));

// 3) 服务端出图，端侧只下载显示
DisplayQRCode.texture = await result.LoadQrTextureAsync();
```

> 上传对调用方透明：`payload.Length ≤ ChunkSize` 走一次性透传，服务端上限更小时自动回退分片；大文件自动分片续传。
> **参数最小化**：默认只认本机 Edge（`InitLocal()`）；`EdgeKey` / `AccessToken` 仅「绕过 Edge 直连后端」等高级场景才需要；`exhibitId` 缺省由 Edge / 服务端 `ANDX_EDGE_EXHIBIT_ID` 提供（一个 Edge 服务多展项时才显式传入）。
> **定价不由端侧决定**：`UploadOptions` 没有价格字段，价格由服务端/管理后台配置。

## 常见场景

### 上传本地文件

```csharp
var result = await AndX.Share.UploadAsync(
    new FilePathPayload(Application.streamingAssetsPath + "/demo.mp4", MediaType.Video),
    new UploadOptions { Title = "宣传片" });
```
> WebGL 无本地文件系统，请用 `ByteArrayPayload` 或 `TexturePayload`；`FilePathPayload` 在 WebGL 运行时会抛配置错误。

### 扫码解析与下载（资源页 / H5）

```csharp
// 页面取参：query.q → decodeURIComponent → 从 /r/ 或 /p/ 提取 token（也可用 ShareLink.ExtractToken）
string token = ShareLink.ExtractToken(q, scene, rawToken);

ScanResolveResult scan = await AndX.Share.ResolveAsync(token);
if (scan.Entitled)
{
    DownloadResult dl = await AndX.Share.GetDownloadAsync(token); // presigned 地址（需登录）
    Application.OpenURL(dl.Url);
}
```
> 未购买时 `ResolveAsync` 只返回预览，**不会**返回全量下载地址。
> `GetDownloadAsync` 需登录：配置 `AndXOptions.AccessToken`（或 `AccessTokenProvider`，便于刷新）后，SDK 自动注入 `Authorization: Bearer`。`ResolveAsync` 为可选登录，带令牌时会额外返回 `entitled`。

### 签发票据 / 扫码付费

```csharp
// 边缘侧签发资源下载票据（mediaId）
IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(mediaId: "88123");

// 展项付费票据 + 轮询订单（exhibitId 可省略，由 Edge / 服务端边缘配置提供）
PayTicket pay = await AndX.Pay.CreateTicketAsync(new PayTicketOptions());
DisplayQRCode.texture = await pay.LoadQrTextureAsync();
OrderStatusInfo status = await AndX.Pay.QueryOrderAsync(pay.OrderNo);
```

### AI 生图（经 Edge 转发，端侧不持密钥）

```csharp
// 可选：全局默认展项（一个 Edge 服务多个展项时配置一次；调用处显式值优先）
AndX.Config.Init(new AndXOptions { Endpoint = "http://127.0.0.1:6699", ExhibitId = "1024" });

// 能力可用性（网关未配置时 Available=false 并回显 Reason，不抛错）
AICapabilitiesResult caps = await AndX.AI.GetCapabilitiesAsync();

// 一步生图：给出提示词即可（提交 + 轮询），成功后用 MediaId 复用分享 / 二维码 / 下载 / 付费链路
AIJob job = await AndX.AI.GenerateImageAsync("赛博朋克城市夜景");
if (job.Status == AndXContract.AIJobStatuses.Succeeded)
{
    IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
}

// 需要更多控制时用对象重载（尺寸 / 数量 / 幂等键 / 参考图 / 进度）
AIJob styled = await AndX.AI.GenerateImageAsync(
    new AIImageRequest { Prompt = "赛博朋克城市夜景", Size = "1024x1024" },
    progress: j => Debug.Log($"[AndX.AI] {j.Status}"));
```

> 异步用法：`CreateImageJobAsync` 提交拿 `jobNo`，再 `GetJobAsync` / `WaitForJobAsync` 轮询，`CancelJobAsync` 取消（仅 `PENDING` 生效）。
> 端侧只传 `prompt`（+ 可选尺寸/数量/参考图 `input`），AI 供应商密钥只在服务端聚合网关，端侧不接触、不参与定价。
> 同参可用 `AIImageRequest.IdemKey` 幂等复用成功结果；`AIJob.IsTerminal` 判断是否已结束。
> `WaitForJobAsync` 到终态返回结果，超时抛 `TIMEOUT`、取消抛 `CANCELED`。

## 错误处理

所有失败统一抛 `AndXException`，`Code` 为稳定错误码：

```csharp
try
{
    await AndX.Share.UploadAsync(payload, options);
}
catch (AndXException e) when (e.Is(AndXContract.SdkErrorCodes.Timeout))
{
    // 超时：SDK 已对网络错误 / 5xx 自动重试 MaxRetries 次
}
catch (AndXException e)
{
    Debug.LogError($"AndX 失败 code={e.Code} status={e.HttpStatus} trace={e.TraceId}");
}
```
> 仅网络错误与 5xx 会自动重试；4xx 业务错误直接抛出，不重试。

## 平台与线程注意

- **主线程**：`LoadQrTextureAsync()` 依赖 `UnityWebRequest`，必须在主线程调用。
- **进度回调线程**：`IProgress` 回调可能不在主线程，回调内请勿直接操作 `UnityEngine.UI`；需要更新 UI 时请派发回主线程（示例中的 `Debug.Log` 是安全的）。
- **WebGL**：强制 `https`；只能使用内存/纹理载荷。
- **密钥**：插件默认不持密钥——鉴权凭据（`EdgeKey`）存于本机 AndXEdge，由 Edge 注入；仅「绕过 Edge 直连后端」时才需 `EdgeKey`，且只经环境变量注入，切勿写入代码或打进包体。

## 测试

- **Core（纯 C#，可在任意 .NET 环境跑）**：`Tests~/AndXCore.Tests`，`dotnet test Tests~/AndXCore.Tests/AndXCore.Tests.csproj`。只编译 `Runtime/Core`，不需要 Unity。
- **引擎侧（Unity）**：`Tests/Editor`（EditMode 12 例）+ `Tests/Runtime`（PlayMode 3 例），覆盖 `Runtime/Unity` 里 dotnet 测不到的部分（PNG 编码、启动注册、网络错误映射等）。在 Unity 中打开 **Window > General > Test Runner** 即可运行；嵌入（embedded）安装时测试程序集会被自动发现。

## API 速查

| 入口 | 方法 |
|------|------|
| `AndX.Config` | `InitLocal()` / `Init(AndXOptions)` / `Init(AndXOptions, IAndXTransport)` / `Reset()` / `IsConfigured` |
| `AndX.Share` | `UploadAsync` / `IssueResourceTicketAsync` / `ResolveAsync` / `GetDownloadAsync` / `AbortAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.Pay` | `CreateTicketAsync` / `QueryOrderAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.AI` | `GetCapabilitiesAsync` / `CreateImageJobAsync` / `GetJobAsync` / `CancelJobAsync` / `WaitForJobAsync` / `GenerateImageAsync` |
| `AndX.Unity` | `TexturePayload` / `FilePathPayload` / `ByteArrayPayload` / `LoadQrTextureAsync()` |

完整接口与平台说明见仓库文档 `docs/design/implementation.md` §10。
