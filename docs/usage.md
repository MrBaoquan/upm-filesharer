# AndX Unity 插件使用文档

> 面向接入开发者。`com.parful.andx` 把本地图片/视频上传到 AndX 展项能力平台并生成二维码；
> 用户扫码即可预览、下载，付费资源按服务端定价走扫码支付；AI 能力域可生成图片并直接复用同一条分发链路。
>
> - 包 ID：`com.parful.andx` ｜ 程序集：`AndX.Runtime` ｜ 命名空间：`AndX` / `AndX.Core` / `AndX.Unity`
> - 最低 Unity：`2020.3` ｜ 支持平台：Windows / Android / WebGL
> - 依赖：`com.unity.nuget.newtonsoft-json`（包已声明，自动拉取）
> - 契约版本：`1.1`（见文末「契约与兼容」）

## 目录

- [1. 设计原则](#1-设计原则)
- [2. 安装](#2-安装)
- [3. 快速开始](#3-快速开始)
- [4. 配置 Config](#4-配置-config)
- [5. 资源分享 Share](#5-资源分享-share)
- [6. 扫码付费 Pay](#6-扫码付费-pay)
- [7. AI 能力 AI](#7-ai-能力-ai)
- [8. 二维码与链接](#8-二维码与链接)
- [9. 错误处理](#9-错误处理)
- [10. 平台与线程](#10-平台与线程)
- [11. 常见场景](#11-常见场景)
- [12. FAQ](#12-faq)
- [13. API 速查](#13-api-速查)
- [14. 契约与兼容](#14-契约与兼容)

## 1. 设计原则

1. **接入最小化**：默认只认本机 AndXEdge（`AndX.Config.InitLocal()`）。鉴权、展项标识、公网地址均由 Edge 代持，**插件不持密钥**。
2. **参数最小化**：能省的参数都省。展项可用全局默认 `AndXOptions.ExhibitId`，上传/付费/AI 的调用处可覆盖。
3. **定价不由端侧决定**：端侧类型没有价格字段，价格一律由服务端/管理后台配置并回传。
4. **稳定错误码**：所有失败统一抛 `AndXException`，读 `Code`（稳定码），不依赖 HTTP 状态码做业务判断。
5. **能力域静态入口**：`AndX.Config` 一次配置，业务方法挂在 `AndX.Share.*` / `AndX.Pay.*` / `AndX.AI.*`，不引入实例门面。

```text
[Unity 插件] --HTTP--> [AndXEdge（本机，代持鉴权）] --公网--> [AndX 服务端]
     |                                                        |
     +---------------- 扫码用户（微信小程序资源页/付费页）<-----+
```

## 2. 安装

Package Manager → **Add package from git URL**：

```text
https://github.com/MrBaoquan/upm-filesharer.git
```

> 也可将 `unity/com.parful.andx` 以本地（embedded）方式放入工程的 `Packages/` 目录。嵌入安装时，插件自带的引擎侧测试（`Tests/Editor`、`Tests/Runtime`）会被自动发现。

## 3. 快速开始

```csharp
using AndX;
using AndX.Unity;
using UnityEngine;

public class AndXQuickStart : MonoBehaviour
{
    async void Start()
    {
        // 1) 零参数接入本机 AndXEdge（默认 http://127.0.0.1:6699）
        AndX.Config.InitLocal();

        // 2) 上传素材并拿到二维码（exhibitId 可省略，由 Edge / 服务端兜底）
        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(screenshot),                                  // 或 FilePathPayload / ByteArrayPayload
            new UploadOptions { Title = "展项截图" },
            progress: p => Debug.Log($"[AndX] {p.Percent:P0}"));

        // 3) 服务端出图，端侧只下载显示
        qrDisplay.texture = await result.LoadQrTextureAsync();
    }
}
```

> `LoadQrTextureAsync()` 是 `AndX.Unity` 提供的扩展方法，依赖 `UnityWebRequest`，**必须在主线程调用**（见[平台与线程](#10-平台与线程)）。

## 4. 配置 Config

```csharp
AndX.Config.IsConfigured;                 // 是否已配置
AndX.Config.InitLocal();                  // 零参数：指向本机 Edge，Edge 代持鉴权
AndX.Config.Init(new AndXOptions { ... });// 显式配置（或直连后端）
AndX.Config.Init(options, transport);     // 显式注入传输实现（测试 / 自定义）
AndX.Config.Reset();                      // 清空（测试与场景切换）
```

### AndXOptions

| 属性 | 默认 | 说明 |
|---|---|---|
| `Endpoint` | 必填 | 服务端 / Edge 基址（含协议，不含尾斜杠），如 `http://127.0.0.1:6699` |
| `EdgeKey` | — | 边缘共享密钥 `X-Edge-Key`；**仅直连后端时需要**，对 `/api/edge/*` 与 `/api/ai/*` 生效；经本机 Edge 时留空 |
| `AccessToken` | — | 登录令牌；需登录接口自动带 `Authorization: Bearer` |
| `AccessTokenProvider` | — | 令牌提供者（每次请求取值，便于刷新）；设置后优先于 `AccessToken` |
| `ExhibitId` | — | 全局默认展项；`Share` / `Pay` / `AI` 未显式传参时兜底（调用处显式值优先） |
| `Timeout` | 30s | 单次请求超时 |
| `MaxRetries` | 2 | 网络错误 / 5xx 的最大重试次数（指数退避） |
| `ChunkSize` | 8 MiB | 分片阈值；`≤` 走一次性上传，`>` 自动分片续传（服务端有下限，见下） |
| `AllowInsecureHttp` | false | 允许 http 明文基址；WebGL 强制 https，内网联调可显式开启 |

**密钥只从环境变量加载，不入代码、不入包体：**

```csharp
string edgeKey = EdgeKey.FromEnvironment();   // 读取环境变量 ANDX_EDGE_KEY
```

```csharp
// 直连后端（绕过边缘网关）示例
AndX.Config.Init(new AndXOptions
{
    Endpoint  = "https://api.example.com",
    EdgeKey   = EdgeKey.FromEnvironment(),
    ExhibitId = "1024",
});
```

## 5. 资源分享 Share

### 5.1 上传素材

```csharp
ShareResult result = await AndX.Share.UploadAsync(
    payload,
    new UploadOptions { ExhibitId = "1024", Title = "宣传片", MediaType = MediaType.Video },
    progress: p => Debug.Log($"{p.Sent}/{p.Total} ({p.Percent:P0})"),
    cancellationToken: ct);
```

- **分流**：`payload.Length ≤ ChunkSize` 走一次性透传（少两次往返）；更大自动分片流式续传。服务端单次上限更小时，SDK 会回退分片（处理 `413`）。
- **续传**：分片上传以对象存储已接收字节为准对齐续传偏移，失败**不自动中止**，保留会话便于重试；确认放弃时调用 `AbortAsync(uploadId)`。
- **空文件**：`Length == 0` 直接抛 `CONFIGURATION`（不发起请求）。

| 载荷（`AndX.Unity`） | 构造 | 适用平台 |
|---|---|---|
| `TexturePayload` | `new TexturePayload(Texture2D, fileName = null)` | 全平台（WebGL 可用；构造时在主线程编码为 PNG） |
| `FilePathPayload` | `new FilePathPayload(path, mediaType)` | Windows / Android（WebGL 运行时抛配置错误） |
| `ByteArrayPayload` | `new ByteArrayPayload(bytes, fileName = null, mediaType)` | 全平台（WebGL 唯一可用形态） |

> 自定义载荷实现 `AndX.Core.IAndXPayload`：`FileName` / `MediaType` / `Length` / `OpenReadAsync(ct)`。

### 5.2 签发资源票据 / 扫码解析 / 下载

```csharp
IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(mediaId: "88123");

ScanResolveResult scan = await AndX.Share.ResolveAsync(token);          // 公开，可选登录
if (scan.Entitled)
{
    DownloadResult dl = await AndX.Share.GetDownloadAsync(token);       // 需登录，返回 presigned 地址
    Application.OpenURL(dl.Url);
}

await AndX.Share.AbortAsync(uploadId);                                  // 中止上传会话
```

> `ResolveAsync` 未购买时只返回预览，**不会**返回全量下载地址；`GetDownloadAsync` 需配置 `AccessToken` / `AccessTokenProvider`。

## 6. 扫码付费 Pay

```csharp
// 签发展项付费票据（exhibitId 可省略，取全局默认）
PayTicket pay = await AndX.Pay.CreateTicketAsync(new PayTicketOptions());
qrDisplay.texture = await pay.LoadQrTextureAsync();

// 轮询订单（PENDING → PAID）
OrderStatusInfo status = await AndX.Pay.QueryOrderAsync(orderNo);
```

> `PayTicket.OrderNo` 在签发阶段通常为 `null`——`/p/` 展项付费的**下单链路服务端尚未打通**，票据先用于展示二维码；订单号在下单后可得。金额一律由服务端返回。

## 7. AI 能力 AI

经 AndXEdge 透明转发，**端侧不接触 AI 供应商密钥、不参与定价**；成功产物 `AIJob.MediaId` 可直接复用分享 / 二维码 / 下载 / 付费链路。

```csharp
// 能力清单（网关未配置时 Available=false 并回显 Reason，不抛错）
AICapabilitiesResult caps = await AndX.AI.GetCapabilitiesAsync();
if (!caps.Available) { Debug.LogWarning($"AI 暂不可用：{caps.Reason}"); return; }

// 一步生图：给出提示词即可（提交 + 轮询到终态）
AIJob job = await AndX.AI.GenerateImageAsync("赛博朋克城市夜景，霓虹倒影");
if (job.Status == AndXContract.AIJobStatuses.Succeeded)
{
    IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
    qrDisplay.texture = await ticket.LoadQrTextureAsync();
}
```

### 7.1 方法一览

| 方法 | 语义 |
|---|---|
| `GetCapabilitiesAsync(ct)` | 能力清单与整体可用性（降级不抛错） |
| `CreateImageJobAsync(string prompt, ct)` | 提交生图（最小参数） |
| `CreateImageJobAsync(AIImageRequest, ct)` | 提交生图（完整参数），返回 `jobNo` |
| `GetJobAsync(jobNo, ct)` | 查询任务（`SUCCEEDED` 时带 `MediaId` / `ResultUrls`） |
| `CancelJobAsync(jobNo, ct)` | 取消任务（仅 `PENDING` 生效） |
| `WaitForJobAsync(jobNo, waitOptions, progress, ct)` | 轮询到终态；到终态返回，**超时抛 `TIMEOUT`、取消抛 `CANCELED`** |
| `GenerateImageAsync(string prompt, ...)` | 提交 + 等到终态（最小参数） |
| `GenerateImageAsync(AIImageRequest, ...)` | 提交 + 等到终态（完整参数） |

### 7.2 请求参数 AIImageRequest

| 属性 | 说明 |
|---|---|
| `Prompt` | 提示词（**必填**） |
| `ExhibitId` | 展项 ID（可选，缺省取全局默认） |
| `Size` | 尺寸，如 `1024x1024`（可选） |
| `Count` | 生成数量 1~4（可选；**线格式字段名为 `n`**） |
| `Input` | 扩展入参（可选，如参考图 `mediaId`） |
| `IdemKey` | 同参幂等键（可选）：命中成功的同参任务直接复用结果 |

### 7.3 结果 AIJob

`JobNo` / `Status` / `Capability` / `Provider` / `Model` / `Cached` / `MediaId` / `MediaIds` / `ResultUrls` / `ErrorCode` / `ErrorMessage` / `CreatedAt` / `UpdatedAt`，以及便捷属性 `IsTerminal`（`SUCCEEDED` / `FAILED` / `CANCELED`）。

状态机：`PENDING → RUNNING → SUCCEEDED / FAILED`，`PENDING` 可 `CANCELED`（常量见 `AndXContract.AIJobStatuses`）。

### 7.4 等待选项 AIWaitOptions

| 属性 | 默认 | 说明 |
|---|---|---|
| `Timeout` | 120s | 最长等待时间 |
| `Interval` | 2s | 轮询间隔 |

```csharp
// 异步长任务：进度回调 + 取消
AIJob handle = await AndX.AI.CreateImageJobAsync(new AIImageRequest { Prompt = prompt });

AIJob done = await AndX.AI.WaitForJobAsync(
    handle.JobNo,
    waitOptions: new AIWaitOptions { Timeout = TimeSpan.FromMinutes(3) },
    progress:    j => Debug.Log($"[AndX.AI] {j.Status}"));

await AndX.AI.CancelJobAsync(handle.JobNo);   // 用户中途放弃（仅 PENDING 有效）
```

```csharp
// 同参幂等复用
AIJob job = await AndX.AI.GenerateImageAsync(new AIImageRequest
{
    Prompt  = prompt,
    Size    = "1024x1024",
    Count   = 1,
    IdemKey = $"exhibit-1024-{DateTime.Today:yyyyMMdd}",
});
```

> 视频能力（`ai.video.generate`）为预留，端侧接口后续增量。

## 8. 二维码与链接

- **普通链接二维码**（服务端出图，两类各一条规则）：
  - 资源类：`{BASE}/r/{token}` → 小程序 `pages/resource/index`
  - 付费类：`{BASE}/p/{token}` → 小程序 `pages/pay/index`
- 端侧**不得各自拼链接**，一律经 `AndX.Share.QrImageUrl(token)` / `Pay.QrImageUrl(token)` 取图，或用 `AndX.Core.ShareLink` 生成/解析。
- 显示二维码：`await result.LoadQrTextureAsync()`（扩展方法，`ShareResult` / `IssuedTicket` / `PayTicket` 均有）。
- 需要原始 PNG 字节：`await AndX.Share.GetQrPngAsync(token)`。

```csharp
// 小程序页面取参：query.q → 从 /r/ 或 /p/ 还原 token
string token = ShareLink.ExtractToken(q, scene, rawToken);

// 需要自定义出图地址时（base 不含尾斜杠）
string content = ShareLink.BuildShareUrl("https://example.com", AndXContract.Purposes.ExhibitPay, token);
```

## 9. 错误处理

所有失败统一抛 `AndXException`：

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
    Debug.LogError($"[AndX] code={e.Code} status={e.HttpStatus} trace={e.TraceId}");
}
```

| 属性 | 说明 |
|---|---|
| `Code` | 稳定错误码 |
| `HttpStatus` | HTTP 状态码（网络层错误时为 `null`） |
| `TraceId` | 服务端追踪标识（服务端未统一回传时为 `null`） |
| `Is(code)` | 判定错误码 |

**重试策略**：仅网络错误与 `5xx` 自动重试（指数退避 `200ms × 2^attempt`，最多 `MaxRetries` 次）；`4xx` 业务错误直接抛出，不重试。

### 错误码

SDK 侧（`AndXContract.SdkErrorCodes`）：

| 码 | 含义 |
|---|---|
| `NETWORK` | 网络错误 |
| `TIMEOUT` | 超时（含 `WaitForJobAsync` 等待超时） |
| `CANCELED` | 已取消 |
| `CONFIGURATION` | 配置/参数错误（未 Init、必填缺失、空载荷等） |

服务端业务码（节选，完整见 `AndXContract.ErrorCodes`）：

| 码 | 含义 |
|---|---|
| `ENTITLEMENT_REQUIRED` | 需付费/未授权下载 |
| `MEDIA_NOT_FOUND` / `RESOURCE_NOT_FOUND` | 资源不存在 |
| `TICKET_NOT_FOUND` / `TICKET_EXPIRED` / `TICKET_REVOKED` | 票据不存在/过期/作废 |
| `EXHIBIT_NOT_FOUND` | 展项不存在 |
| `PAY_SESSION_*` / `PAY_ORDER_EXISTS` / `PAY_NOTIFY_VERIFY_FAILED` | 支付相关 |
| `EDGE_UNAUTHORIZED` / `EDGE_DISABLED` / `EDGE_UPLOAD_*` | 边缘网关相关 |
| `AI_DISABLED` | 未配置聚合网关，AI 整体不可用 |
| `AI_CAPABILITY_UNSUPPORTED` | 未注册的能力标识 |
| `AI_JOB_NOT_FOUND` | AI 任务不存在 |
| `AI_PROVIDER_ERROR` | 聚合网关/供应商调用失败 |

## 10. 平台与线程

- **主线程**：`LoadQrTextureAsync()` 依赖 `UnityWebRequest`，必须在主线程调用。
- **进度回调线程**：`IProgress` 回调**可能不在主线程**，回调内请勿直接操作 `UnityEngine.UI`；需要更新 UI 时请派发回主线程（示例中的 `Debug.Log` 是安全的）。
- **传输已编组回主线程**：`AndX.Unity` 启动时（`RuntimeInitializeOnLoadMethod`）登记主线程上下文，传输层据此把 Unity API 调用编组回主线程，业务代码可安全 `await`。
- **WebGL**：强制 `https`；只能使用 `TexturePayload` / `ByteArrayPayload`，`FilePathPayload` 运行时会抛配置错误。
- **密钥**：插件默认不持密钥——`EdgeKey` 存于本机 AndXEdge，由 Edge 注入；仅「绕过 Edge 直连后端」时才需显式提供，且只经环境变量注入，切勿写入代码或打进包体。

## 11. 常见场景

### 11.1 一键：上传截图并出码

```csharp
AndX.Config.InitLocal();
ShareResult r = await AndX.Share.UploadAsync(new TexturePayload(screenshot), new UploadOptions { Title = "截图" });
qrDisplay.texture = await r.LoadQrTextureAsync();
```

### 11.2 支付后下载

```csharp
AndX.Config.Init(new AndXOptions
{
    Endpoint            = "https://edge.example.com",
    AccessTokenProvider = () => Session.Token,     // 便于刷新
});

ScanResolveResult scan = await AndX.Share.ResolveAsync(token);
if (scan.Entitled)
    Application.OpenURL((await AndX.Share.GetDownloadAsync(token)).Url);
```

### 11.3 AI 生图 → 出码 → 扫码领取

```csharp
AIJob job = await AndX.AI.GenerateImageAsync(prompt);
IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
qrDisplay.texture = await ticket.LoadQrTextureAsync();
```

### 11.4 自定义传输（测试 / 特殊平台）

```csharp
public sealed class MyTransport : AndX.Core.IAndXTransport
{
    public Task<AndX.Core.TransportResponse> SendAsync(AndX.Core.TransportRequest req, CancellationToken ct)
        => /* 返回 TransportResponse，网络异常统一映射为 NetworkError/TimedOut，不抛出 */;
}

AndX.Config.Init(new AndXOptions { Endpoint = "https://edge.example.com" }, new MyTransport());
```

## 12. FAQ

**Q：需要自己保存/传递边缘密钥吗？**
A：不需要。默认 `InitLocal()`，密钥由本机 AndXEdge 代持。仅直连后端时才用 `EdgeKey.FromEnvironment()`。

**Q：`exhibitId` 什么时候要传？**
A：单机单展项可不传；一个 Edge 服务多个展项时，建议在 `AndXOptions.ExhibitId` 配一次全局默认，个别调用再覆盖。

**Q：上传大文件会卡主线程吗？**
A：分片上传是流式的，进度回调可能在后台线程；不要在回调里直接操作 UI。

**Q：为什么 `PayTicket.OrderNo` 是 null？**
A：`/p/` 付费下单链路服务端尚未打通，签发阶段只出票据与二维码；订单号在下单后可得。

**Q：AI 返回“不可用”？**
A：服务端未配置聚合网关时 `GetCapabilitiesAsync().Available == false` 且带 `Reason`；这是降级而非异常，按 `Reason` 处理或回退人工素材。

**Q：金额能由端侧指定吗？**
A：不能。端侧类型不含价格字段，价格一律由服务端/后台配置。

## 13. API 速查

| 入口 | 成员 |
|---|---|
| `AndX.Config` | `InitLocal()` / `Init(AndXOptions)` / `Init(AndXOptions, IAndXTransport)` / `Reset()` / `IsConfigured` |
| `AndX.Share` | `UploadAsync` / `IssueResourceTicketAsync` / `ResolveAsync` / `GetDownloadAsync` / `AbortAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.Pay` | `CreateTicketAsync` / `QueryOrderAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.AI` | `GetCapabilitiesAsync` / `CreateImageJobAsync` / `GetJobAsync` / `CancelJobAsync` / `WaitForJobAsync` / `GenerateImageAsync` |
| `AndX.Unity` | `TexturePayload` / `FilePathPayload` / `ByteArrayPayload` / `LoadQrTextureAsync()` |
| `AndX.Core.ShareLink` | `ExtractToken` / `BuildShareUrl` / `PathForPurpose` |
| `AndX.EdgeKey` | `FromEnvironment()` |
| `AndXContract` | `Version` / `Paths` / `Purposes` / `Headers` / `ErrorCodes` / `SdkErrorCodes` / `AICapabilities` / `AIJobStatuses` / `Defaults` |

## 14. 契约与兼容

- 契约 **SSOT** 为语言中立的 `andx-sdk-spec`（OpenAPI + 链接规则 + 错误码 + 常量）；本插件只读引用，**禁止各自定义协议、链接或错误码**。
- 当前契约版本 `ANDX_CONTRACT_VERSION = 1.1`（新增 AI 能力域，向后兼容）；端侧镜像于 `AndXContract.Version`，由契约一致性测试守护。
- UPM 独立消费时，契约一致性测试（依赖仓库内 `andx-sdk-spec`）不生效；在 AndX 仓库内运行时生效。

---

> 更完整的平台设计与实现规格见主仓 `docs/design/solution.md`、`docs/design/implementation.md`。
