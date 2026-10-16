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
- [15. 直连后端（节点身份）](#15-直连后端节点身份)

## 1. 设计原则

1. **两种接入形态，一套业务 API**：**默认直连 AndX 云服务**（`AndX.Config.InitAuto()`：插件持本机节点凭据，服务端审计精确到「节点 + 展项」）；也可经本机 AndXEdge（`AndX.Config.InitLocal()`：鉴权 / 展项标识 / 公网地址由 Edge 代持，**插件不持密钥**）。`Share` / `Pay` / `AI` 的调用代码**完全一样**，差异只在「凭据归谁 / 展项身份从哪来」（见 §15.5 对照表）。
2. **参数最小化**：能省的参数都省。展项可用全局默认 `AndXOptions.ExhibitId`，上传/付费/AI 的调用处可覆盖。
3. **定价不由端侧决定**：端侧类型没有价格字段，价格一律由服务端/管理后台配置并回传。
4. **稳定错误码**：所有失败统一抛 `AndXException`，读 `Code`（稳定码），不依赖 HTTP 状态码做业务判断。
5. **能力域静态入口**：`AndX.Config` 一次配置，业务方法挂在 `AndX.Share.*` / `AndX.Pay.*` / `AndX.AI.*`，不引入实例门面。

```text
直连（默认）：[Unity 插件（持本机节点凭据 HMAC）] --HTTPS--> [AndX 服务端]
Edge：        [Unity 插件（不持密钥）] --HTTP--> [AndXEdge（本机，代持鉴权）] --公网--> [AndX 服务端]
```

## 2. 安装

Package Manager → **Add package from git URL**：

```text
https://github.com/MrBaoquan/upm-filesharer.git
```

> 也可将 `unity/com.parful.andx` 以本地（embedded）方式放入工程的 `Packages/` 目录。嵌入安装时，插件自带的引擎侧测试（`Tests/Editor`、`Tests/Runtime`）会被自动发现。

### 2.1 包内示例（Samples）

Package Manager → 选中 **AndX** → **Samples** → **Import**，导入后位于 `Assets/Samples/AndX/<版本>/<示例名>/`。

| 示例 | 覆盖能力 | 形态 |
|------|----------|------|
| **Share And QR** | `Config.InitAuto()` → `Share.UploadAsync` → `LoadQrTextureAsync`（并演示 `Node.SelfAsync` 节点自省） | 场景 `ShareAndQr` |
| **Scan And Download** | `Share.ResolveAsync`（预览 / 定价 / 是否已购）、`Share.GetDownloadAsync`（下载授权） | 场景 `ScanAndDownload` |
| **AI Image** | `AI.GetCapabilitiesAsync` → `AI.GenerateImageAsync` → **结果图显示（多图切换 / 落盘）** + `Share.IssueResourceTicketAsync` 出码 | 场景 `AiImage` |
| **Pay Ticket** | `Pay.CreateTicketAsync`、`Pay.QueryOrderAsync` | 场景 `PayTicket` |

- 每个示例都是「场景 + 脚本」：导入后打开场景 → 填好接入方式（见下）→ **Play 即出结果**。
- 组件参数都带了可用默认值（`endpoint` / `exhibitId` / `token` / `q` 留空即走 Edge / 服务端默认），并默认开启 `autoRunOnStart`，Play 后自动跑一遍主流程：
  - `Share And QR`：直连模式先打印一次节点自省（我是谁、代表哪个展项），再上传场景里已挂好的 `example.jpg` → 出码到 `RawImage`。
  - `Scan And Download`：没配 token 时**自举**——上传一张运行时生成的示例图换来真实票据，再走「解析 → 预览 → 下载授权」，完整跑通一码双端链路。
  - `AI Image`：自动查一次能力（免费，可立刻看出服务端 AI 聚合网关是否可用）。
  - `Pay Ticket`：自动签发票据并把付费码画到 `RawImage`。
- `AI Image` 的**生图会消耗服务端配额**，因此不自动触发；点场景里的「生图并出码」按钮或 Inspector 齿轮菜单即可。
- 想手动控制节奏：把 `autoRunOnStart` 关掉，改用场景按钮（已绑定 `ShareFile` / `Resolve` / `Download` / `GenerateAndShowQr` / `CheckCapabilities` / `CreateTicket` / `QueryOrder`）或 Inspector 齿轮菜单（`ContextMenu`）。状态与错误码统一回显在场景的 `Status` 文本上。
- 前置条件随接入方式而定：**直连（默认）**需要后端基址（Inspector 的 `endpoint`，或环境变量 `ANDX_ENDPOINT`）+ 本机节点凭据（见 §15.1）；**经 Edge** 需要本机跑着 AndXEdge（默认 `http://127.0.0.1:6699`）。缺什么示例会在 Console 里直接给出可操作的错误提示。
- 示例资源（含场景）随包带 `.meta`，导入时 GUID 保持不变，场景里的脚本与贴图引用不会丢。
- 场景 UI 使用 uGUI 内建的 `Text`（不依赖 TextMeshPro），示例不引入包清单之外的依赖。
- **接入方式在 Inspector 里选**：每个示例的第一个字段 `accessMode` = `Direct`（默认：直连后端）/ `Edge`（经本机 AndXEdge）/ `Auto`（本机有节点凭据则直连，否则走 Edge）；`endpoint` 留空时，直连读环境变量 `ANDX_ENDPOINT`、Edge 用 `127.0.0.1:6699`。

## 3. 快速开始

```csharp
using System;
using AndX;
using AndX.Unity;
using UnityEngine;

public class AndXQuickStart : MonoBehaviour
{
    async void Start()
    {
        // 1) 接入（二选一，业务调用完全一样）
        //    默认直连 AndX 云服务：插件持本机节点凭据（首启流程见 usage.md §15.1）
        AndX.Config.InitAuto(new AndXOptions { Endpoint = "https://api.example.com" });
        //    或经本机 AndXEdge（Edge 代持鉴权，插件不持密钥）
        // AndX.Config.InitLocal();   // = InitAuto(AccessMode = Edge)，默认 http://127.0.0.1:6699

        // 2) 上传素材并拿到二维码（exhibitId 可省略，由 Edge / 服务端兜底）
        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(screenshot),                                  // 或 FilePathPayload / ByteArrayPayload
            new UploadOptions { Title = "展项截图" },
            progress: new Progress<UploadProgress>(p => Debug.Log($"[AndX] {p.Percent:P0}")));

        // 3) 服务端出图，端侧只下载显示
        qrDisplay.texture = await result.LoadQrTextureAsync();
    }
}
```

> `LoadQrTextureAsync()` 是 `AndX.Unity` 提供的扩展方法，依赖 `UnityWebRequest`，**必须在主线程调用**（见[平台与线程](#10-平台与线程)）。

## 4. 配置 Config

```csharp
AndX.Config.IsConfigured;                        // 是否已配置
AndX.Config.InitAuto();                          // ★ 推荐：按 AccessMode（默认 Direct 直连）解析基址与凭据
AndX.Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Edge });   // 经本机 AndXEdge
await AndX.Config.InitAutoAsync(new AndXOptions { EnrollmentToken = token, NodeId = "DEV-001" });  // 首启注册
AndX.Config.InitLocal();                         // 便捷别名 = InitAuto(AccessMode = Edge)
AndX.Config.Init(new AndXOptions { ... });       // 显式配置：Endpoint 必填，不做模式推断
AndX.Config.Init(options, transport);            // 显式注入传输实现（测试 / 自定义）
AndX.Config.ActiveMode;                          // 生效的接入方式（Direct / Edge）
AndX.Config.ActiveNodeId;                        // 生效的节点身份（直连模式；经 Edge 为 null）
AndX.Config.Describe();                          // 一行接入摘要（不含密钥），日志与排障直接打印
AndX.Config.Reset();                             // 清空（测试与场景切换）
```

### AndXOptions

| 属性 | 默认 | 说明 |
|---|---|---|
| `AccessMode` | `Direct` | 接入方式：`Direct` 直连后端 / `Edge` 经本机 AndXEdge / `Auto` 自动（有节点凭据则直连，否则走 Edge）。仅 `InitAuto` 读取 |
| `Endpoint` | 直连：`https://andx.andcrane.com` | 基址（含协议，不含尾斜杠）。直连回退顺序：显式值 → 环境变量 `ANDX_ENDPOINT` → **默认云服务基址**；Edge = 本机 Edge 基址（缺省 `127.0.0.1:6699`）；显式 `Init` 时**必填**（不做推断、不读环境） |
| `NodeCredential` | 自动加载 | 节点身份（每机一把、可单独吊销）。缺省从本机凭据文件加载；**直连模式必须有**，经 Edge 时忽略 |
| `EnrollmentToken` / `NodeId` | 环境变量 | 只有「首启注册」用：配 `InitAutoAsync` 用一次性令牌换节点密钥（`ANDX_ENROLLMENT_TOKEN` / `ANDX_NODE_ID`） |
| `EdgeKey` | — | 边缘共享密钥 `X-Edge-Key`；**旧轨**：仅「无节点身份时用共享密钥直连后端」的过渡期需要，新接入请用节点身份 |
| `AccessToken` | — | 登录令牌；需登录接口自动带 `Authorization: Bearer` |
| `AccessTokenProvider` | — | 令牌提供者（每次请求取值，便于刷新）；设置后优先于 `AccessToken` |
| `ExhibitId` | — | 全局默认展项；`Share` / `Pay` / `AI` 未显式传参时兜底（调用处显式值优先） |
| `Timeout` | 30s | 单次请求超时 |
| `MaxRetries` | 2 | 网络错误 / 5xx 的最大重试次数（指数退避） |
| `ChunkSize` | 8 MiB | 分片阈值；`≤` 走一次性上传，`>` 自动分片续传（服务端有下限，见下） |
| `AllowInsecureHttp` | false | 允许 http 明文基址；WebGL 强制 https，内网联调可显式开启 |

**密钥只从环境变量加载，不入代码、不入包体：**

```csharp
string edgeKey = EdgeKey.FromEnvironment();   // 读取环境变量 ANDX_EDGE_KEY（旧轨）
```

`InitAuto` 还会读这些环境变量（**只有 `InitAuto` / `InitAutoAsync` 读**，显式 `Init` 完全按参数走）：

| 环境变量 | 作用 | 对应字段 |
|---|---|---|
| `ANDX_ENDPOINT` | 直连后端基址 | `AndXOptions.Endpoint` |
| `ANDX_EDGE_ENDPOINT` | 本机 Edge 基址 | `AndXOptions.Endpoint`（Edge 模式） |
| `ANDX_ENROLLMENT_TOKEN` | 首启注册的一次性令牌（**凭据，勿入日志**） | `AndXOptions.EnrollmentToken` |
| `ANDX_NODE_ID` | 首启注册的节点编号 | `AndXOptions.NodeId` |
| `ANDX_ALLOW_INSECURE_HTTP` | 放行内网 http 明文基址（仅联调；`1/true/yes/on`） | `AndXOptions.AllowInsecureHttp` |
| `ANDX_EDGE_KEY` | 旧轨共享密钥直连 | `AndXOptions.EdgeKey` |

> 接入方式（Direct / Edge）**刻意不提供环境变量**：它决定「展项身份从哪来」，必须由代码 / Inspector 显式选择，
> 避免现场一个残留变量把选择悄悄改掉。优先级：显式字段 &gt; 环境变量 &gt; 默认值（环回基址）。

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
    progress: new Progress<UploadProgress>(p => Debug.Log($"{p.Sent}/{p.Total} ({p.Percent:P0})")),
    cancellationToken: ct);
```

- **进度回调**：`IProgress<T>` 是接口，不能直接传 lambda/方法组，需包一层 `new Progress<T>(...)`（或自定义实现）；详见[平台与线程](#10-平台与线程)。

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
| `GetResultBytesAsync(job \| jobNo, index, ct)` | 取第 index 张结果图的**原始字节**（快路径 / 先刷新两条路） |
| `SaveResultAsync(job \| jobNo, index, filePath, ct)` | 把第 index 张结果图写成本地文件（目录自动创建） |
| `AndX.Unity.AIResultLoader.*` | 端侧便捷：显示纹理（`LoadTextureAsync`）与落盘（`SaveAsync`，默认 `persistentDataPath/andx/ai/`） |

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

`JobNo` / `Status` / `Capability` / `Provider` / `Model` / `Cached` / `MediaId` / `MediaIds` / `ResultUrls` / `ErrorCode` / `ErrorMessage` / `CreatedAt` / `UpdatedAt`；
便捷成员：`IsTerminal`（`SUCCEEDED` / `FAILED` / `CANCELED`）、`ResultCount`（结果张数，回退 `MediaIds`）、
`ResultUrl` / `ResultUrlAt(index)`（第 index 张图地址，越界返回 null）。**`ResultUrls` 是 presigned，10 分钟有效**，见 §7.5。

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
    progress:    new Progress<AIJob>(j => Debug.Log($"[AndX.AI] {j.Status}")));

await AndX.AI.CancelJobAsync(handle.JobNo);   // 用户中途放弃（仅 PENDING 有效）
```

> `progress` 参数类型是 `IProgress<AIJob>`（接口），需 `new Progress<AIJob>(...)` 包一层，不能直接传 lambda。

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

### 7.5 取产物：显示目标图 / 多图 / 落盘

产物有**三条互不排斥**的用法：屏幕上直接显示、出码给观众扫码带走、把原图存到本地。

| 用途 | 入口 | 说明 |
|---|---|---|
| 屏幕显示目标图 | `resultImage.texture = await job.LoadResultTextureAsync(index)` | Unity 侧扩展方法；多图用 `index` 切换 |
| 迟到取图 / 长期展示 | `await AIResultLoader.LoadTextureAsync(jobNo, index)` | 先重新查询任务拿**新地址**再下载 |
| 原始字节（自绘 / 二次处理） | `await AndX.AI.GetResultBytesAsync(job, index)`（快）<br>`await AndX.AI.GetResultBytesAsync(jobNo, index)`（稳） | 返回 PNG 字节 |
| 落盘留存 / 打印 | `await AIResultLoader.SaveAsync(job, index)`（快）<br>`await AIResultLoader.SaveAsync(jobNo, index)`（稳） | 默认 `persistentDataPath/andx/ai/{jobNo}-{index}.png`；也可传 `directory` / `fileName` |
| 出码扫码带走 | `await AndX.Share.IssueResourceTicketAsync(job.MediaId)` | 与上面几条**可同时用** |

```csharp
AIJob job = await AndX.AI.GenerateImageAsync("赛博朋克城市夜景", progress: new Progress<AIJob>(j => Debug.Log(j.Status)));

// ① 屏幕直接显示第 0 张（不需要二维码的现场只用这一步）
resultImage.texture = await job.LoadResultTextureAsync(0);
Debug.Log($"共 {job.ResultCount} 张，首张地址={job.ResultUrl}");

// ② 出码：产物 MediaId 直接复用分享链路
IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
qrDisplay.texture = await ticket.LoadQrTextureAsync();

// ③ 落盘（默认 persistentDataPath/andx/ai/{jobNo}-0.png）
string path = await AIResultLoader.SaveAsync(job, 0);
```

> ⚠️ **结果图地址是 presigned，有效期 10 分钟**：`job.ResultUrls` 只适合「刚拿到结果就显示」。
> 屏幕长期展示、延迟点「保存」、隔天复用同一 job 时，请用带 `jobNo` 的重载（或 `GetResultBytesAsync(jobNo, ...)`）
> ——它们会先 `GetJobAsync` 拿新地址。**`jobNo` 是长期有效的业务键**；拿到 403/404 只说明地址过期，重新查询即可，不必重新生图。
>
> 示例 `AI Image` 即按此实现：`ResultImage` 显示目标图（多图可「上一张 / 下一张」）、`DisplayQRCode` 出码、
> 齿轮菜单提供「显示结果图 / 上一张 / 下一张 / 保存结果图到本地」，并演示了「快路径失败 → 用 `jobNo` 刷新重试」。

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
- **进度回调线程**：`IProgress` 回调线程取决于传入的实现——`new System.Progress<T>(...)` 在构造处捕获同步上下文（主线程构造即回调回主线程，可直接刷新 UI）；自定义实现需自行派发回主线程。
- **传输已编组回主线程**：`AndX.Unity` 启动时（`RuntimeInitializeOnLoadMethod`）登记主线程上下文，传输层据此把 Unity API 调用编组回主线程，业务代码可安全 `await`。
- **WebGL**：强制 `https`；只能使用 `TexturePayload` / `ByteArrayPayload`，`FilePathPayload` 运行时会抛配置错误。
- **密钥**：直连模式插件持**本机节点凭据**（每机一把、可单独吊销；落盘保护器见 §15.2，生产必须注入 DPAPI / Keystore）；经 Edge 模式插件不持密钥（Edge 代持）。旧轨共享密钥 `EdgeKey` 只经环境变量注入，切勿写入代码或打进包体。

## 11. 常见场景

### 11.1 一键：上传截图并出码

```csharp
AndX.Config.InitAuto();      // 默认直连后端（基址见 ANDX_ENDPOINT 或 AndXOptions.Endpoint）
// AndX.Config.InitLocal();  // 或经本机 AndXEdge
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

**Q：直连和经 Edge 怎么选？切换成本大吗？**
A：由 `AndXOptions.AccessMode`（示例里是 Inspector 的第一个下拉框）决定，**业务代码一行都不用改**：
`Direct` 直连后端（插件持本机节点凭据，审计到「节点 + 展项」）、`Edge` 经本机 AndXEdge（Edge 代持鉴权；展项无外网 / 多展项共用出口时用）、`Auto` 自动选择。对照表见 §15.5。

**Q：直连的凭据从哪来？每个展项都要配吗？**
A：**展项不配凭据，机器才配凭据**：运维在后台签发一次性注册令牌 → 展项机首启用 `AndX.Node.EnrollAsync`（或 `Config.InitAutoAsync`）换到本机节点密钥并落盘；服务端按节点绑定裁决展项。展项维度配的是「授权关系」与「能力开关」，不是密钥。详见 §15.1。

**Q：需要自己保存/传递边缘密钥吗？**
A：直连不需要共享密钥（用每机一把的节点凭据）；经 Edge 也不需要（Edge 代持）。`EdgeKey.FromEnvironment()` 只服务于「旧轨共享密钥直连」的过渡期现场。

**Q：`exhibitId` 什么时候要传？**
A：单机单展项可不传；一个 Edge 服务多个展项时，建议在 `AndXOptions.ExhibitId` 配一次全局默认，个别调用再覆盖。

**Q：上传大文件会卡主线程吗？**
A：分片上传是流式的，进度回调可能在后台线程；不要在回调里直接操作 UI。

**Q：为什么 `PayTicket.OrderNo` 是 null？**
A：`/p/` 付费下单链路服务端尚未打通，签发阶段只出票据与二维码；订单号在下单后可得。

**Q：示例里的 `endpoint` 该填什么？**
A：**留空即可**：直连模式会用默认云服务基址 `https://andx.andcrane.com`；本机联调可填 `http://127.0.0.1`（dev 栈 nginx），或设环境变量 `ANDX_ENDPOINT`（优先级高于默认值、低于 Inspector 显式值）。
> 注意：Inspector 的改动要 **Ctrl+S 保存场景**才落盘；Play 模式里改的值退出后会回滚。若刚从 Package Manager 重新导入了示例，
> 请等 Unity 编译/域重载完成后再改（脚本字段集变化期间编辑，保存会把新字段（如 `accessMode` / `endpoint`）覆盖掉）。

**Q：结果图链接能一直用吗？屏幕上想一直显示怎么办？**
A：`job.ResultUrls` 是 presigned、**10 分钟**有效；长期展示请用 `AIResultLoader.LoadTextureAsync(jobNo, index)`（会先查任务拿新地址），
或干脆显示后不再重新下载（纹理已在内存里，不受链接过期影响）。详见 §7.5。

**Q：AI 返回“不可用”？**
A：服务端未配置聚合网关时 `GetCapabilitiesAsync().Available == false` 且带 `Reason`；这是降级而非异常，按 `Reason` 处理或回退人工素材。

**Q：金额能由端侧指定吗？**
A：不能。端侧类型不含价格字段，价格一律由服务端/后台配置。

## 13. API 速查

| 入口 | 成员 |
|---|---|
| `AndX.Config` | `InitAuto(AndXOptions?)` / `InitAutoAsync(AndXOptions?)` / `InitLocal()` / `Init(AndXOptions)` / `Init(AndXOptions, IAndXTransport)` / `ActiveMode` / `ActiveNodeId` / `ResolutionReason` / `Describe()` / `Reset()` / `IsConfigured` |
| `AndX.Node` | `EnrollAsync`（首启注册换节点密钥）/ `SelfAsync`（我是谁、能代表哪些展项）/ `RecentAuditAsync`（我最近发出的调用与结果码） |
| `AndX.AndXEnvironment` | `Endpoint` / `EdgeEndpoint` / `EnrollmentToken` / `NodeId` / `AllowInsecureHttp`（`InitAuto` 的取值来源） |
| `AndX.AndXCredentialProvider` | `StoreFactory`（平台注入凭据存储；Unity 侧已默认注册 `persistentDataPath/andx/node-credential.json`） |
| `AndX.Share` | `UploadAsync` / `IssueResourceTicketAsync` / `ResolveAsync` / `GetDownloadAsync` / `AbortAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.Pay` | `CreateTicketAsync` / `QueryOrderAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.AI` | `GetCapabilitiesAsync` / `CreateImageJobAsync` / `GetJobAsync` / `CancelJobAsync` / `WaitForJobAsync` / `GenerateImageAsync` / `GetResultBytesAsync` / `SaveResultAsync` |
| `AndX.Unity` | `TexturePayload` / `FilePathPayload` / `ByteArrayPayload` / `LoadQrTextureAsync()` / `AIResultLoader.LoadTextureAsync` / `AIResultLoader.SaveAsync` / `job.LoadResultTextureAsync()` |
| `AndX.Core.ShareLink` | `ExtractToken` / `BuildShareUrl` / `PathForPurpose` |
| `AndX.EdgeKey` | `FromEnvironment()`（旧轨共享密钥） |
| `AndXContract` | `Version` / `Paths` / `Purposes` / `Headers` / `ErrorCodes` / `SdkErrorCodes` / `AICapabilities` / `AIJobStatuses` / `Defaults` |

## 14. 契约与兼容

- 契约 **SSOT** 为语言中立的 `andx-sdk-spec`（OpenAPI + 链接规则 + 错误码 + 常量）；本插件只读引用，**禁止各自定义协议、链接或错误码**。
- 当前契约版本 `ANDX_CONTRACT_VERSION = 1.1`（新增 AI 能力域，向后兼容）；端侧镜像于 `AndXContract.Version`，由契约一致性测试守护。
- UPM 独立消费时，契约一致性测试（依赖仓库内 `andx-sdk-spec`）不生效；在 AndX 仓库内运行时生效。

---

> 更完整的平台设计与实现规格见主仓 `docs/design/solution.md`、`docs/design/implementation.md`。

## 15. 直连后端（节点身份）

> **直连是默认形态**（`AndXAccessMode.Direct`）：接入代码与经 Edge 只差一行——`AndX.Config.InitAuto()` 而不是 `InitLocal()`；
> 业务调用（`Share` / `Pay` / `AI`）、异常处理、二维码链路完全一致。两种模式的对照与切换成本见 §15.5。

**什么时候用**：展项机能直接路由到后端（同局域网内网后端，或能出外网的现场）。此时**不需要 AndXEdge**：
插件持**本机节点凭据**（每台机器一把、可单独吊销），服务端据此判定租户/展项，并把调用与产物**审计到「节点 + 展项」**。
AndXEdge 只在两类现场保留价值：① 展项无外网、局域网内有出口（`Relay` 透传）；② 多个展项共用一个出口（`Hub` 多展项）。

> 三条硬约定（solution §16）：
> 1. **展项不持凭据，机器才持凭据**：一台物理机一条节点身份；"哪个展项要配凭据"这个问法本身不成立——展项维度配的是**授权关系**与**能力开关**。
> 2. **身份凭据按节点独立，业务凭据按项目共享**：微信支付 / AI 网关密钥只在服务端（配置中心，按项目），端侧**永不**下发。
> 3. **插件不持共享密钥**：直连只用节点 HMAC；`EdgeKey` 属旧轨（经 Edge / 过渡期），后续版本退役。

### 15.1 首次接入（每台机器一次）

最省事的写法：令牌与节点编号走环境变量，凭据自动落盘，业务代码直接开始调用：

```csharp
// ANDX_ENDPOINT=https://api.example.com
// ANDX_ENROLLMENT_TOKEN=<运维签发的一次性令牌，短时效 / 限次数>
// ANDX_NODE_ID=DEV-001
await AndX.Config.InitAutoAsync();     // 用令牌自助注册 → 节点密钥落盘 → 建好客户端（Auto 也会因此判定为直连）
Debug.Log(AndX.Config.Describe());     // mode=Direct | identity=节点身份 node:DEV-001 | 说明=…
```

之后每次启动只需**同步**的 `AndX.Config.InitAuto()`：凭据已在盘上，不再需要令牌。

需要显式控制（自定义名称、指定凭据路径、注入平台保护器）时用长写法：

```csharp
using AndX;
using AndX.Unity;

const string endpoint = "https://api.example.com";   // 后端基址（不是本机 Edge 时必填）

// 1) 凭据存储（默认落在 persistentDataPath/andx/node-credential.json）
var store = NodeCredentialStorage.Create(/* 见 15.2：生产请注入平台保护器 */);

// 2) 首启：用运维签发的一次性注册令牌换本机节点密钥（明文只返回一次）
var credential = store.Load()
    ?? await AndX.Node.EnrollAsync(endpoint, enrollmentToken: token, nodeId: "DEV-001", name: "1 号展项机", store: store);

// 3) 直连模式：把节点身份交给 Config（不再需要 EdgeKey / 本机 Edge）
AndX.Config.InitAuto(new AndXOptions
{
    AccessMode = AndXAccessMode.Direct,
    Endpoint = endpoint,
    NodeCredential = credential,      // 已加载的凭据优先于本机凭据文件
});

// 4) 之后与经 Edge 完全一致（同一套 API，差异只在 Endpoint）
var result = await AndX.Share.UploadAsync(new TexturePayload(screenshot), new UploadOptions { Title = "展项截图" });
DisplayQRCode.texture = await result.LoadQrTextureAsync();
```

- 注册令牌由运维在后台签发（`POST /api/admin/enrollment-tokens`，短时效 / 限次数 / 限租户，服务端只存哈希）；
  展项机令牌可固定展项（`role=exhibit`），中控令牌 `role=hub` 不绑展项。
- **展项机不需要在插件里配 `ExhibitId`**：服务端按节点绑定裁决（这样客户端无法冒充展项）；
  只有 `role=hub` 的中控才必须逐次携带，且服务端会校验它是否在**授权展项集合**内（后台
  `PUT /api/admin/devices/{deviceId}/exhibits` 维护）。
- 注册**不做自动重试**：令牌一次性，重试可能落在"服务端已消费"之后；失败请让运维重发令牌。

#### 令牌如何决定「项目 / 展项」（归属链）

| 环节 | 归属来自哪里 | 备注 |
|---|---|---|
| ① 后台签发令牌 | `tenant_id` + `role` + 可选 `exhibit_id` | **展项机令牌的项目由展项反查**（运维不用手填项目）；中控令牌显式传 `projectId` |
| ② 首次注册 `/api/edge/enroll` | 服务端按令牌行**落一条节点记录**（`device`） | **租户只取令牌行**（客户端无法声明租户）；展项取令牌固定值（未固定时才允许调用方补，且须属令牌租户）；返回的一次性密钥须立即落盘 |
| ③ 之后每次调用 | 服务端按请求签名里的 `x-device-id` 查**节点记录** | **令牌不再参与**；租户/展项取自节点；`audit_request`/`media`/`ai_job` 全部写这份"已校验"的值 |

- 一个展项多台机器：签发一张 `maxUses=N` 的令牌（都绑同一展项），或每台一张 `maxUses=1`。
- 一台机器多展项：**必须用 `role=hub` 令牌**（不绑展项），每次请求显式带 `exhibitId`，并由后台维护的**授权展项集合**放行。
- 改绑展项/换机器：改的是**节点**（后台改绑定或授权集合、必要时轮换密钥）——令牌是一次性"出生证明"，不能改绑。

> 记忆法：**令牌 = 一次性出生证明（决定登记到哪个项目/展项）；节点记录 = 身份证（之后每次核查）；请求体里的展项 = 只有 Hub 能提的临时申请，且须过授权集合。**

### 15.2 凭据落盘：注入平台保护器

包内默认的 `PassthroughCredentialProtector` **只做 Base64，不提供机密性**，仅供联调（`store.IsEncryptedAtRest == false` 时应视为告警）。
生产请实现 `ICredentialProtector` 并注入真实平台密钥库：

```csharp
#if UNITY_STANDALONE_WIN && !UNITY_WEBGL
// Windows Standalone：DPAPI。注意——ProtectedData 属于 .NET Framework 面，
// 需把 Player Settings → Api Compatibility Level 设为 ".NET Framework"（.NET Standard 2.0 下该类型不可用），
// 或改用 NuGet 包 System.Security.Cryptography.ProtectedData。
using System.Security.Cryptography;
using System.Text;

public sealed class DpapiProtector : AndX.Core.ICredentialProtector
{
    public string Protect(string plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedValue) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));

    public bool IsEncryptedAtRest => true;
}
#endif
```

Android 用 `AndroidJavaObject` 调 Keystore（`KeyGenParameterSpec` + `AES/GCM`）实现同一接口；iOS/macOS 用 Keychain。
**WebGL 没有安全存储**：不要把节点密钥放进浏览器产物，WebGL 现场请走服务端签发的短期 scoped token（待实现）或仍用本机 Edge 形态。

### 15.3 密钥轮换与吊销

- 轮换：后台 `POST /api/admin/devices/{deviceId}/reset-secret` → 服务端把旧密钥降级为"上一代"并设到期时间
  （双活窗口 N/N-1，默认 24h）→ 端侧用新密钥替换本地凭据即可，窗口内旧密钥仍可用，**不会掉线**。
- 吊销/停用：`POST /api/admin/devices/{deviceId}/enabled`（`false`）→ 该节点所有调用立即 `DEVICE_DISABLED`。

### 15.4 排错速查

| 错误码 | 含义 | 处理 |
|---|---|---|
| `CONFIGURATION` | 插件本地参数问题（Endpoint 为空/非绝对、http 非环回未放行、缺 token/nodeId） | 按消息修正配置 |
| `ENROLLMENT_TOKEN_INVALID` / `EXPIRED` / `REVOKED` / `EXHAUSTED` | 注册令牌不存在/过期/吊销/次数用尽 | 让运维重发令牌 |
| `DEVICE_NOT_FOUND` / `DEVICE_SECRET_MISSING` | 节点未注册 / 服务端无密钥 | 后台注册节点或重置密钥 |
| `DEVICE_DISABLED` | 节点被停用 | 后台启用 |
| `DEVICE_REPLAY` | 时间戳超出 ±5min（时钟漂移）或 nonce 重放 | 校时（NTP）；确认没有复用请求 |
| `DEVICE_UNAUTHORIZED` | 签名不匹配（密钥不一致 / 签名原文口径变化） | 比对密钥；确认路径与 body 未被中间层改写 |
| `NODE_EXHIBIT_REQUIRED` | Hub 节点未携带 `exhibitId` | Hub 必须逐次显式携带 |
| `NODE_EXHIBIT_FORBIDDEN` | 节点无权代表该展项（Hub 不在授权集合 / 展项机携带了别的展项） | 后台维护授权集合，或修正调用参数 |
| `VALIDATION_FAILED` | 服务端没定出展项（无绑定且无 env 兜底） | 绑展项或显式传入 |

> ℹ️ 请求体摘要（`x-device-body-sha256`）由插件自动计算并随请求下发，且**纳入签名**；调用方无需关心。
> 服务端对上传类端点会校验实际字节与摘要一致（不一致返回 `VALIDATION_FAILED`）。
>
> ✅ 签名原文（v2）为 `[METHOD, PATH?sortedQuery, TIMESTAMP, NONCE, SHA256(body)]`。
> **查询串已纳入签名**（按 key 升序、不解码、空值渲染为 `key`、无参数省略 `?`），所以 `/api/edge/resources?...` 里的 `exhibitId/title/fileName` 也被保护：
> 中间人改写 query 会直接验签失败（`DEVICE_UNAUTHORIZED`）。注意因此**中间层（nginx/网关）不得改写查询串或重排序参数**。
### 15.5 两种模式怎么选、切换成本

**切换成本 = 一个字段**：`AndXOptions.AccessMode`（示例里是 Inspector 的第一个下拉框）。业务调用、异常处理、二维码链路**完全不变**。

| 维度 | `Direct`（默认，直连后端） | `Edge`（经本机 AndXEdge） |
|---|---|---|
| 谁持凭据 | 插件持本机节点凭据（每机一把，可单独吊销 / 轮换） | Edge 持共享密钥，插件不持密钥 |
| 展项身份 | 服务端按**节点绑定**裁决（客户端无法冒充；只有 Hub 中控才逐次携带） | Local / Relay 由 Edge 注入；Hub 按 IP 映射注入 |
| 服务端审计 | `identity_source=node_hmac`，精确到「节点 + 展项」 | 边缘节点身份 + 展项（Edge 用 v2 重签） |
| 现场要求 | 展项机能直达后端（内网后端或可出外网） | 展项无外网（Relay）/ 多展项共用一个出口（Hub） |
| 典型错误码 | `DEVICE_*` / `NODE_EXHIBIT_*` / `ENROLLMENT_TOKEN_*` | `EDGE_UNAUTHORIZED` / `EDGE_DISABLED` |
| 排障入口 | `AndX.Node.SelfAsync()` / `AndX.Node.RecentAuditAsync()`（插件侧自省） | Edge 诊断页 `GET /diagnostics/self` + 最近调用表 |

**治理结论（solution §16）**：要「审计到精确展项」，两种模式的正式形态都是**节点身份**——直连模式插件自带，
Edge 模式由 Edge 重签；`EdgeKey` 共享密钥是**过渡期旧轨**，只用于还没发节点身份的现场，新接入不应使用。

**本轮优化掉的三个隐性成本**（2026-10-16）：

1. **模式不再写死在示例代码里**：改 Inspector 下拉框即可，不用改码重编；
2. **凭据样板收进 SDK**：`Config.InitAuto()` 自动加载本机凭据、按需读环境变量，首启注册用 `InitAutoAsync()` 一步完成（不用手抄 `EnrollAsync` + 落盘 + `Init` 三步）；
3. **直连也有自省入口**：`AndX.Node.SelfAsync()` / `RecentAuditAsync()`，与 Edge 诊断页对称——现场可直接回答「我是谁、这次调用算在哪个展项头上、结果码是什么」。