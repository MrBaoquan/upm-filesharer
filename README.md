# AndX

AndX 展项能力平台 Unity 包。把本地图片/视频上传到 AndX 平台并生成二维码；用户扫码即可预览、下载，付费资源按服务端定价走扫码支付。

- 包 ID：`com.parful.andx`
- 最低 Unity：`2020.3`
- 依赖：`com.unity.nuget.newtonsoft-json`（包已声明，自动拉取）
- 支持平台：Windows / Android / WebGL
- 完整使用文档：`Documentation~/usage.md`

## 安装

### 方式一：Git URL（仓库地址，当前可用）

Package Manager → **Add package from git URL**：

```
https://github.com/MrBaoquan/upm-filesharer.git
```

> 仓库名沿用改名前的 `upm-filesharer`，包 ID 已是 `com.parful.andx`。

### 方式二：内网 scoped registry（发布到 `http://upm.andcrane.com:4873` 后可用）

在工程 `Packages/manifest.json` 中登记 registry 并声明依赖：

```json
{
  "scopedRegistries": [
    {
      "name": "andcrane",
      "url": "http://upm.andcrane.com:4873",
      "scopes": ["com.parful", "com.software"]
    }
  ],
  "dependencies": {
    "com.parful.andx": "1.1.0"
  }
}
```

### 方式三：本地（embedded）

把包目录放进工程的 `Packages/` 下（如 `unity/com.parful.andx`）。

> 测试发现：**嵌入（embedded）安装时**包自带的引擎侧测试（`Tests/Editor`、`Tests/Runtime`）会被自动发现；**用 Git URL 或 registry 安装时**，需在 manifest 的 `testables` 中列出包名才能在工程内运行这些测试：
>
> ```json
> { "testables": ["com.parful.andx"] }
> ```

## 快速开始

```csharp
using System;
using AndX;
using AndX.Unity;

// 1) 接入：默认直连 AndX 云服务（插件持本机节点凭据，服务端审计到「节点 + 展项」）
AndX.Config.InitAuto(new AndXOptions { Endpoint = "https://api.example.com" });   // 基址也可走环境变量 ANDX_ENDPOINT
// 或经本机 AndXEdge（Edge 代持鉴权，插件不持密钥）：AndX.Config.InitLocal();

// 2) 上传素材并拿到二维码（exhibitId 可省略）
ShareResult result = await AndX.Share.UploadAsync(
    new TexturePayload(screenshot),                                        // 或 FilePathPayload / ByteArrayPayload
    new UploadOptions { Title = "展项截图" },
    progress: new Progress<UploadProgress>(p => Debug.Log($"[AndX] {p.Percent:P0}")));

// 3) 服务端出图，端侧只下载显示
DisplayQRCode.texture = await result.LoadQrTextureAsync();
```

> 上传对调用方透明：`payload.Length ≤ ChunkSize` 走一次性透传，服务端上限更小时自动回退分片；大文件自动分片续传。
> **两种接入形态，一套业务 API**：`Direct`（默认）插件持本机节点凭据直连后端；`Edge` 经本机 AndXEdge（Edge 代持鉴权）。切换只改 `AndXOptions.AccessMode`，`Share` / `Pay` / `AI` 调用一行都不用动——对照表见 `Documentation~/usage.md` §15.5。`exhibitId` 缺省由服务端按节点绑定（或 Edge / `ANDX_EDGE_EXHIBIT_ID`）兜底，一个 Edge 服务多展项时才显式传入。
> **定价不由端侧决定**：`UploadOptions` 没有价格字段，价格由服务端/管理后台配置。

## 两种接入方式（默认直连后端）

| 模式 | 谁持凭据 | 后端审计 | 现场 |
|---|---|---|---|
| `Direct`（默认） | 插件持**本机节点凭据**（每机一把、可单独吊销） | 精确到「节点 + 展项」（`identity_source=node_hmac`） | 展项机能直达后端（内网后端 / 可出外网） |
| `Edge` | 本机 AndXEdge（插件不持密钥） | 边缘节点 + 展项（Edge 用 v2 重签） | 展项无外网（`Relay`）/ 多展项共用一个出口（`Hub`） |

切换只改一个字段：`AndXOptions.AccessMode`（示例里是 Inspector 的下拉框），**业务代码零改动**。

```csharp
using AndX;
using AndX.Unity;

// 默认：直连后端（Endpoint 也可留空，走环境变量 ANDX_ENDPOINT）
AndX.Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Direct, Endpoint = "https://api.example.com" });

// 首启（每台机器一次）：用运维签发的一次性令牌换本机节点密钥（之后启动不再需要令牌）
// ANDX_ENROLLMENT_TOKEN / ANDX_NODE_ID 走环境变量即可：
//   await AndX.Config.InitAutoAsync();

// 或经本机 AndXEdge（等价于 AndX.Config.InitLocal()）
AndX.Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Edge });

// 自省：我是谁、能代表哪些展项、最近调用算在哪个展项头上（直连模式）
NodeIdentity me = await AndX.Node.SelfAsync();
foreach (var entry in await AndX.Node.RecentAuditAsync(limit: 20))
{
    Debug.Log($"{entry.Method} {entry.Path} → {entry.StatusCode} exhibit={entry.ExhibitId} src={entry.IdentitySource}");
}
```

> - **展项不配凭据、机器才配**：展项机不需要填 `ExhibitId`（服务端按节点绑定裁决，客户端无法冒充展项）；`role=hub` 的中控才必须逐次携带，并由服务端校验**授权展项集合**。凭据由 `AndX.Node.EnrollAsync`（或 `Config.InitAutoAsync`）自助注册获得，落盘在本机 `persistentDataPath/andx/node-credential.json`。
> - **业务凭据（微信支付 / AI 网关）只在服务端**，端侧永不下发；`EdgeKey` 是旧轨（经 Edge / 过渡期），直连请用 `NodeCredential`。
> - AndXEdge 仍然保留两类不可替代的现场：① 展项无外网但有局域网出口（`Relay` 透传）；② 多展项共用一个出口（`Hub`）。
> - 完整步骤、凭据落盘（DPAPI/Keystore 示例）、轮换与排错见 `Documentation~/usage.md` §15「直连后端（节点身份）」。

## 示例（Samples）

包内示例按能力域分为四个，在 **Window > Package Manager** 里选中 **AndX** → **Samples** → **Import**，
导入后位于 `Assets/Samples/AndX/<版本>/<示例名>/`：

| 示例 | 覆盖能力 | 形态 |
|------|----------|------|
| **Share And QR** | `Config.InitAuto()` → `Share.UploadAsync` → `LoadQrTextureAsync` 出码（并演示 `Node.SelfAsync` 节点自省） | 场景 `ShareAndQr` |
| **Scan And Download** | `Share.ResolveAsync`（预览 / 定价 / 是否已购）、`Share.GetDownloadAsync`（下载授权） | 场景 `ScanAndDownload` |
| **AI Image** | `AI.GetCapabilitiesAsync` → `AI.GenerateImageAsync` → 产物出码 | 场景 `AiImage` |
| **Pay Ticket** | `Pay.CreateTicketAsync` 付费票据、`Pay.QueryOrderAsync` 订单状态 | 场景 `PayTicket` |

> **组件参数默认留空 + `autoRunOnStart`，导入 → 打开场景 → 选好接入方式 → Play 就能看到结果。** 每个示例的第一个字段是 `accessMode`（`Direct` 默认直连 / `Edge` 经本机 AndXEdge / `Auto`），`endpoint` 留空时直连读环境变量 `ANDX_ENDPOINT`、Edge 用 `127.0.0.1:6699`：
> `Share And QR` 自动上传场景里已挂好的示例图并出码（直连模式还会先打印节点自省）；`Scan And Download` 在没配 token 时**自举**一张真实票据（上传运行时生成的示例图）再走「解析 → 预览 → 下载授权」；`AI Image` 自动查一次能力（免费）；`Pay Ticket` 自动签发票据出付费码。
> 前置条件：**直连（默认）**要后端基址（`endpoint` 或 `ANDX_ENDPOINT`）+ 本机节点凭据（首启见 usage.md §15.1）；**经 Edge** 要本机跑着 AndXEdge（默认 `http://127.0.0.1:6699`）。缺什么 Console 会直接报出可操作的错误。想手动触发就关掉 `autoRunOnStart`，改用场景按钮或 Inspector 齿轮菜单（`ContextMenu`）。
> `AI Image` 的生图会消耗服务端配额，因此**不**自动触发（自动跑的只是免费的能力查询）；场景里的 `Status` 文本会回显每一步结果或错误码。
> 示例资源（含场景）随包带 `.meta`，导入时 GUID 不变，场景内的脚本与贴图引用不会丢。
> 场景 UI 用 uGUI 内建的 `Text`（不依赖 TextMeshPro），示例不引入包清单之外的依赖。

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
    progress: new Progress<AIJob>(j => Debug.Log($"[AndX.AI] {j.Status}")));
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
- **进度回调线程**：`IProgress` 回调线程取决于传入的实现；用 `new System.Progress<T>(...)` 时它在构造处捕获同步上下文（主线程构造即回调回主线程，可安全刷新 UI），自定义实现需自行派发回主线程。
- **WebGL**：强制 `https`；只能使用内存/纹理载荷。
- **密钥**：直连模式插件持**本机节点凭据**（每机一把、可单独吊销，落盘走 `ICredentialProtector`，生产请注入 DPAPI / Keystore，见 usage.md §15.2）；经 Edge 模式插件不持密钥（Edge 代持）。旧轨共享密钥 `EdgeKey` 只从环境变量注入，切勿写入代码或打进包体。

## 测试

- **Core（纯 C#，可在任意 .NET 环境跑）**：`Tests~/AndXCore.Tests`，`dotnet test Tests~/AndXCore.Tests/AndXCore.Tests.csproj`。只编译 `Runtime/Core`，不需要 Unity。
- **引擎侧（Unity）**：`Tests/Editor`（EditMode）与 `Tests/Runtime`（PlayMode），覆盖 `Runtime/Unity` 里 dotnet 测不到的部分（PNG 编码、启动注册、网络错误映射等）。在 Unity 中打开 **Window > General > Test Runner** 即可运行；嵌入（embedded）安装时测试程序集会被自动发现。用例数以 Test Runner 实测为准（不在此处写死数字，避免与实现漂移）。

## API 速查

| 入口 | 方法 |
|------|------|
| `AndX.Config` | `InitAuto(AndXOptions?)` / `InitAutoAsync(AndXOptions?)` / `InitLocal()` / `Init(AndXOptions)` / `Init(AndXOptions, IAndXTransport)` / `ActiveMode` / `ActiveNodeId` / `ResolutionReason` / `Describe()` / `Reset()` / `IsConfigured` |
| `AndX.Node` | `EnrollAsync`（首启注册）/ `SelfAsync`（我是谁）/ `RecentAuditAsync`（最近调用与结果码） |
| `AndX.AndXEnvironment` | `Endpoint` / `EdgeEndpoint` / `EnrollmentToken` / `NodeId` / `AllowInsecureHttp` |
| `AndX.Share` | `UploadAsync` / `IssueResourceTicketAsync` / `ResolveAsync` / `GetDownloadAsync` / `AbortAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.Pay` | `CreateTicketAsync` / `QueryOrderAsync` / `QrImageUrl` / `GetQrPngAsync` |
| `AndX.AI` | `GetCapabilitiesAsync` / `CreateImageJobAsync` / `GetJobAsync` / `CancelJobAsync` / `WaitForJobAsync` / `GenerateImageAsync` |
| `AndX.Unity` | `TexturePayload` / `FilePathPayload` / `ByteArrayPayload` / `LoadQrTextureAsync()` |

完整接口与平台说明见 AndX 主仓文档 `docs/design/implementation.md` §10。
