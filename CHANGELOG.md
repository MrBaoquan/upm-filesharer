# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **AI 示例改为图生图（换脸/融合）+ 结果图与二维码并行**：参考图取自 `Assets/StreamingAssets/`（默认「人脸.png」「模板.png」，可改），端侧**缩到长边 ≤1280 并转 JPEG** 后内联 base64 提交（示例实测 4.2MB → 326KB，请求体 5.65MB → 436KB）；服务端把参考图透传为厂商 `image` 字段（内网 MinIO 厂商不可达，故不走 URL）。示例新增**分段耗时日志**（读图编码 / 提交 / 生成等待 / 结果图+二维码），并把「显示结果图」与「出码+二维码」改为 `Task.WhenAll` 并行；轮询间隔可配（`pollIntervalMs`，默认 700ms）。
- **直连模式默认基址**：新增 `AndXContract.Defaults.CloudEndpoint = "https://andx.andcrane.com"`；`Config.InitAuto` 的直连基址回退顺序为「显式 `AndXOptions.Endpoint` → 环境变量 `ANDX_ENDPOINT` → 默认云服务基址」，因此 `InitAuto()` 在没配任何东西时也能直接连上（不再抛 `CONFIGURATION`），`ResolutionReason` 会写明基址来自哪里。
- **AI 产物取用（结果图，不止二维码）**：`AIJob.ResultCount` / `ResultUrl` / `ResultUrlAt(index)`；`AndX.AI.GetResultBytesAsync(job | jobNo, index)`（快路径 / 先刷新两条路）、`AndX.AI.SaveResultAsync(job | jobNo, index, filePath)`；Unity 侧 `AndX.Unity.AIResultLoader`（`LoadTextureAsync` 显示纹理、`SaveAsync` 落盘，默认 `persistentDataPath/andx/ai/`）与扩展方法 `job.LoadResultTextureAsync(index)`。**结果图地址是 presigned（10 分钟）**：带 `jobNo` 的重载会先查任务拿新地址——长期展示 / 延迟取图不再依赖 `ResultUrls` 的新鲜度。
- **示例（AI Image）新增结果图显示**：场景新增 `ResultImage`（RawImage）显示生成的目标图，齿轮菜单提供「显示结果图 / 上一张 / 下一张 / 保存结果图到本地」，并演示「快路径取图失败 → 用 jobNo 刷新重试」。
- **接入方式（直连 / 经 Edge）成为一等配置**：`AndXAccessMode`（`Direct` 默认 / `Edge` / `Auto`）、`AndXOptions.AccessMode`、`AndX.Config.InitAuto(AndXOptions?)`（按模式解析基址与凭据，结果写回 `Config.ActiveMode` / `ActiveNodeId` / `ResolutionReason` / `Describe()`，可读原因不含任何密钥）、`AndX.Config.InitAutoAsync(...)`（允许用一次性注册令牌完成首启注册）。`Config.InitLocal()` 变为 `InitAuto(AccessMode = Edge)` 的便捷别名，行为不变。
- **`AndXEnvironment`**（`ANDX_ENDPOINT` / `ANDX_EDGE_ENDPOINT` / `ANDX_ENROLLMENT_TOKEN` / `ANDX_NODE_ID` / `ANDX_ALLOW_INSECURE_HTTP`）与 **`AndXCredentialProvider.StoreFactory`**（平台注入凭据存储；`AndX.Unity` 启动时注册 `persistentDataPath/andx/node-credential.json`）：直连模式不再需要手抄「加载凭据 → 注册 → Init」样板。接入方式（Direct/Edge）**刻意不提供环境变量**，避免残留变量把现场选择悄悄改掉。
- **节点自省入口**：`AndX.Node.SelfAsync()`（`GET /api/devices/self`：角色 / 绑定展项 / 授权展项集合 / 启用状态 / 密钥提示位）与 `AndX.Node.RecentAuditAsync(limit)`（`GET /api/devices/self/audit`：本节点最近调用 + 服务端**已校验**的展项归属与结果码），使插件侧排障与 Edge 诊断页对称；新增公开模型 `NodeIdentity` / `NodeAuditEntry`。
- **示例的接入方式可在 Inspector 选择**：每个示例首字段 `accessMode`（`Direct` 默认 / `Edge` / `Auto`）+ `endpoint`（留空读环境变量），统一走 `Config.InitAuto`；`Share And QR` 新增节点自省演示（Play 时自动打印 + 齿轮菜单 `节点自省（我是谁）`）。
- **插件侧直连 E2E（手动、env-gated）**：`Tests~/AndXCore.Tests/ManualDirectE2ETests.cs` + `HttpTransport.cs`（真实 HTTP 传输，仅供手动 E2E）。`ANDX_E2E=1` 且提供真实节点凭据时，跑通「直连建客户端 → 节点自省 → 真实上传（节点签名 v2 + body 摘要）→ 审计自省」；首次实测（本地 dev 栈）通过：`identity_source=node_hmac`、`exhibit_id` 与节点绑定一致。
- 引擎门禁脚本 `scripts/unity-sample-gate.ps1`（新增第 4 步「示例场景检查」，检查器 `scripts/unity/SampleSceneCheck.cs`）：逐场景断言「无 missing script、示例组件在、关键引用已挂、目标 UI 元素存在」；导入示例前**清空目标目录**——示例改名 / 删文件时上一轮残留副本会与新副本 GUID 撞车（Unity 会静默改写 GUID，表现为场景 missing script）。
- **直连后端（节点身份）**：`AndX.Node.EnrollAsync`（一次性注册令牌 → 本机节点密钥，`POST /api/edge/enroll`）、`NodeCredential`、`NodeCredentialStore` / `ICredentialProtector` / `PassthroughCredentialProtector`、`AndX.Unity.NodeCredentialStorage`（`persistentDataPath/andx/node-credential.json`）。服务端据此判定租户/展项并审计到「节点 + 展项」（solution §16.2 / §16.3）。
- `AndX.Core.DeviceSigner`：节点请求签名（`x-device-id/timestamp/nonce/signature` + 流式端点 `x-device-body-sha256`），签名原文与服务端 `device-crypto.service.ts` 逐字节一致；跨端测试向量见 `Tests~/AndXCore.Tests/NodeIdentityTests.cs`。
- `AndXContract.Paths.EdgeEnroll` 与 `AndXContract.Headers.Device*` 正式启用（此前只有常量、无实现）。
- **四个示例齐备且随包入库**：`AndX Demo` / `Scan And Download` / `AI Image` / `Pay Ticket` 各含**可运行场景**（`QRCodePreview` / `ScanAndDownload` / `AiImage` / `PayTicket`）与 `autoRunOnStart`（导入后 Play 即可看到结果）；示例资源与场景均带 `.meta`，元数据由 `package.json` 的 `samples` 声明。示例已在真实 Unity 2023.1 工程内通过编译门禁。
- 文档：`Documentation~/usage.md` 新增 §15「直连后端（节点身份）」（凭据模型、归属链、DPAPI/Keystore 注入示例、轮换/吊销、排错速查），并补充示例章节的场景与触发方式。

### Changed

- **示例命名与工程约定对齐**：`AndX Demo` → **`Share And QR`**（目录 `Samples~/Share And QR`；场景 `QRCodePreview.unity` → `ShareAndQr.unity`；类 `AndXExample` → `ShareAndQrExample`），并给四个示例脚本统一加命名空间 **`AndX.Samples`**（此前落在全局命名空间，会污染接入方 `Assembly-CSharp`）。命名规则：目录 = 能力域名词短语；场景 = 目录名去空格（PascalCase，缩写沿用包内既有风格 `Qr`）；类 = 场景名 + `Example`。GUID 全部保持不变（`git mv` + 保留 `.meta`），场景内脚本 / 贴图引用不受影响。
- `AndXOptions.Endpoint` 的语义随 `AccessMode` 变化（直连 = 后端基址；Edge = 本机 Edge 基址），`InitAuto` 下支持环境变量缺省；显式 `Init(options)` 行为不变（Endpoint 仍必填、不做模式推断）。

### Changed（BREAKING）

- **节点签名升级为 v2（契约 `1.2`）**：签名原文从「路径（不含 query）」改为「**路径 + 规范化查询串**」——`canonical = [METHOD, PATH?sortedQuery, TIMESTAMP, NONCE, SHA256-HEX(body)].join('\n')`；查询串按 key 升序（key 相同按 value 升序）、**不解码**、空值渲染为 `key`、无参数省略 `?`。堵住「中间人改写 query 即换展项」的缺口（`exhibitId` 等归属字段原先不在保护范围内）。**无 query 时行为与 v1 完全一致**，但端侧与服务端必须同版本升级；`AndXContract.Version` 同步为 `1.2`。
- `AndXOptions.NodeCredential`（新增）优先于 `EdgeKey`：控制面（`/api/edge/*`、`/api/ai/*`、`/api/devices/*`）在存在节点凭据时下发节点签名，不再下发共享密钥。
- 重试（网络错误 / 5xx）**每次尝试重新签名**：nonce 一次性，复用会被服务端判为 `DEVICE_REPLAY`。
- `AndX.Pay.CreateTicketAsync` 不再在端侧校验 `ExhibitId` 非空（客户端不抢答展项归属）：省略时由服务端按节点绑定 / Hub 授权集合 / `ANDX_EDGE_EXHIBIT_ID` 裁决。

### Fixed

- **流式端点签名**：`POST /api/edge/resources`、`PUT /api/edge/uploads/:id` 的请求体是 `application/octet-stream`，服务端不缓冲 body，原先只能按空体计算签名原文 ⇒ **上传在节点身份下必然 401**。现改为客户端随请求下发 `x-device-body-sha256`（body 摘要，**纳入签名**），服务端据此组装 canonical 并校验实际字节（不一致 400 `VALIDATION_FAILED`）；无 body 的请求行为不变，既有跨端测试向量同样不变。
## [1.1.0] - 2026-10-16

> 本包由 `com.parful.filesharer` 重命名而来。`1.1.0` 是改名后的首个版本，也是首次以 `com.parful.andx` 发布；`1.0.x` 为前身包的发布记录。

### Added

- **`AndX.AI` 能力域**：AI 生成任务提交与轮询。`AI.GetCapabilitiesAsync()`、`AI.CreateImageJobAsync(prompt|AIImageRequest)`、`AI.GetJobAsync(jobNo)`、`AI.CancelJobAsync(jobNo)`、`AI.WaitForJobAsync(jobNo, AIWaitOptions)`、`AI.GenerateImageAsync(prompt|AIImageRequest, ...)`（提交+等待一步到位）。经 AndXEdge 透明转发，端侧不接触 AI 供应商密钥、不参与定价；成功产物 `AIJob.MediaId` 可直接复用分享/二维码/下载/付费链路。`WaitForJobAsync` 到终态返回结果，超时抛 `TIMEOUT`、取消抛 `CANCELED`。
- `AIJob` / `AICapabilitiesResult` / `AICapability` / `AIImageRequest` / `AIWaitOptions` 等对外模型；`AIJob.IsTerminal` 便捷判定。
- 契约同步（`andx-sdk-spec` → `AndXContract`）：AI 能力标识、任务状态机、接口路径（`/api/ai/capabilities`、`/api/ai/jobs`）与 4 个 AI 错误码；契约版本 `1.0` → `1.1`（新增 AI 能力域，向后兼容）。
- `AndX.Config.IsConfigured` / `AndX.Config.Reset()`。
- 独立使用文档 `Documentation~/usage.md`：安装、配置、Share / Pay / AI、二维码与链接、错误码、平台与线程、常见场景、FAQ 与 API 速查（README 保留快速开始并链接）。
- `AndXContract.Defaults`（`LocalEdgeEndpoint` / `LocalEdgePort`）：本机 AndXEdge 默认基址常量。
- 登录令牌注入：`AndXOptions.AccessToken` / `AccessTokenProvider`，需登录接口自动带 `Authorization: Bearer`。
- 重写 `README.md`：安装、快速开始、常见场景、错误处理、平台与线程注意、API 速查。
- 引擎侧测试程序集 `Tests/Editor`（EditMode，12 例）与 `Tests/Runtime`（PlayMode，3 例），覆盖 `Runtime/Unity` 中 dotnet 单测无法验证的部分：`TexturePayload` PNG 编码、`AndXUnityBootstrap` 启动注册、`UnityWebRequestTransport` 网络错误映射、`QrTextureLoader` 空 URL 兜底。
- `Tests~/AndXCore.Tests/AssemblyInfo.cs`：Core 单测程序集禁用并行（`Config` 为进程级全局单例，并行会互相覆盖配置）。
- 包根新增 `LICENSE.md`；移除占位 `licensesUrl`（其值为 Unity 模板默认的 `https://example.com/licensing.html`，不是真实许可地址）。
- **包内示例按能力域补齐（`Samples~/`）**：`AndX Demo`（零配置接入 + 上传 `Texture2D` + 出码，场景 `QRCodePreview`）、`Scan And Download`（`Share.ResolveAsync` / `Share.GetDownloadAsync`，场景 `ScanAndDownload`）、`AI Image`（`AI.GetCapabilitiesAsync` → `AI.GenerateImageAsync` → 复用分享链路出码，场景 `AiImage`）、`Pay Ticket`（`Pay.CreateTicketAsync` / `Pay.QueryOrderAsync`，场景 `PayTicket`）；**每个示例都配可运行场景**（Canvas + 出图/预览 `RawImage` + 已绑定 `onClick` 的按钮 + `Status` 文案），导入后打开场景即可 Play，脚本方法同时保留 `ContextMenu` 触发方式。每个示例目录及其文件均带 `.meta`（导入后 GUID 不变，场景引用不丢），元数据统一由 `package.json` 的 `samples` 声明（不使用 `.sample.json`，避免两处元数据冲突）。
- 示例场景标签使用 uGUI 内建的 `UnityEngine.UI.Text`（`com.unity.ugui`，随编辑器自带）：示例不再依赖包清单之外的 `com.unity.textmeshpro`，`AndX Demo` 原有的 `TextMeshProUGUI` 按钮标签已同步换成内建 `Text`（仅标签，出图仍是 `RawImage`）。
- **示例零配置可运行**：四个示例场景的组件参数都以显式默认值序列化（`endpoint` / `exhibitId` / `token` / `q` 留空即走 Edge / 服务端默认），并新增 `autoRunOnStart`（默认 `true`，场景里显式写 `1`）——导入 → 打开场景 → Play 即出结果，不必先改 Inspector：`AndX Demo` 自动上传出码、`Scan And Download` 自举票据后解析、`AI Image` 自动查能力（免费）、`Pay Ticket` 自动签发付费码。`AI Image` 的生图会消耗服务端配额，故保留为显式点击。
- **`Scan And Download` 新增「自举」路径（零配置）**：`token` / `q` 都为空时，示例先用运行时生成的示例纹理（渐变 + 网格，`TextureFormat.RGB24` → `TexturePayload` PNG 编码）上传换取真实票据，再走 `Share.ResolveAsync` → 预览 → `Share.GetDownloadAsync`，完整跑通一码双端链路；示例因此不依赖任何外部图片资源，也不要求接入方预先准备 token。
- `README.md` 与 `Documentation~/usage.md` 新增「包内示例」章节：示例清单（含各示例场景名）、Package Manager 导入路径与触发方式、零配置运行说明与 `autoRunOnStart` 开关。

### Changed

- 包重命名 `com.parful.filesharer` → `com.parful.andx`，程序集 `FileSharer.Runtime` → `AndX.Runtime`，命名空间 `FileSharer` → `AndX`，示例 `FileSharer Demo` → `AndX Demo`。
- 接口简化为静态入口：`AndX.Config.Init(options)` 一次配置，业务方法挂能力域 `AndX.Share.*` / `AndX.Pay.*`；移除实例门面 `AndXHub`、`Use<T>()` 能力注册与 `ShareCapability` / `PayCapability` 类型。
- 定价改由服务端/管理后台决定：端侧上传请求与 `UploadOptions` 不再包含定价；`ShareResult.Amount` 等仍为服务端回传。
- 票据签发去掉裸字符串：`Share.IssueTicketAsync(purpose, ...)` → `Share.IssueResourceTicketAsync(mediaId, ...)`（付费票据由 `AndX.Pay.CreateTicketAsync` 承担）。
- 空载荷（`Length == 0`）前置为 `CONFIGURATION` 错误，不再发起请求。
- **接入最小化**：新增 `AndX.Config.InitLocal()` 零参数接入本机 AndXEdge（默认 `http://127.0.0.1:6699`）；`AndXOptions.EdgeKey` / `AccessToken` 降级为「绕过 Edge 直连后端」高级选项，环回地址 http 免显式 `AllowInsecureHttp`。
- `UploadOptions.ExhibitId` / `PayTicketOptions.ExhibitId` 改为可选：缺省由 Edge / 服务端 `ANDX_EDGE_EXHIBIT_ID` 提供，一个 Edge 服务多展项时才显式传入。
- 新增全局默认展项：`AndXOptions.ExhibitId` 作为 `AndX.Share` / `AndX.Pay` / `AndX.AI` 未显式传参时的兜底（调用处显式值优先）；`Config.Reset()` 一并清空。
- `AndXApiClient.ResolveUrl` 对绝对 URL（`http(s)://`）原样透传，仅相对路径才拼接 `Endpoint`。
- 包清单补 `"license": "UNLICENSED"`（与内网专有包口径一致）。
- 使用文档从 `docs/` 移到 `Documentation~/`：`~` 目录不会被 Unity 导入，避免包内 Markdown 被当作资源导入。
- `Runtime/AndX.Runtime.asmdef` 补 `rootNamespace: "AndX"`（此前运行时程序集缺该字段，只有测试程序集声明了）。
- 示例 `AndX Demo` 改用推荐接入路径：默认 `AndX.Config.InitLocal()` 指向本机 AndXEdge（`endpoint` 留空即零配置，鉴权 / 展项 / 公网地址由 Edge 代持）；Inspector 填 `endpoint` 仍可直连指定基址（`EdgeKey` 只从环境变量取）；新增上传进度、`AndXException` 错误码日志与结果信息输出。
- `package.json` 的 `samples` 数组补齐四个示例的 `displayName` / `description` / `path`；`keywords` 补 `ai`。

### Removed

- `UploadOptions.Amount`（定价不由端侧决定）。
- 旧示例 API（`FileUploader.ShareTexture2D` / `ShareLocalFile`）。
- 孤儿 `Editor.meta`（无对应代码，且每次导入都会触发控制台告警）。

### Fixed

- 示例与文档里的进度回调写法改为 `new Progress<T>(...)`：`Share.UploadAsync` / `AI.WaitForJobAsync` / `AI.GenerateImageAsync` 的 `progress` 形参类型是 `IProgress<T>`（接口），`progress: p => ...` 会被编译器判为 `CS1660`（无法把 lambda 转换为非委托类型），**原 `AndX Demo` 示例与 `README.md` / `Documentation~/usage.md` 中的对应片段实际都编译不过**；示例与文档已同步修正，并在文档中说明 `Progress<T>` 的线程语义。

- 修正 `Runtime/Unity/QrTextureExtensions.cs` 的编译错误：`LoadQrTextureAsync` 误扩展 `Pay.PayTicket`（`Pay` 是静态类，`PayTicket` 实为 `AndX` 顶层类型），改回 `PayTicket`。
- 补齐 `Runtime/Core`、`Runtime/Unity` 目录及其全部源文件的 `.meta`（此前缺失，导致 Unity 每次导入重新分配 GUID）。
- 传输层把请求编组回主线程，修复分片上传时后台线程调用 Unity API 报 `Create can only be called from the main thread`（被统一映射为 `NETWORK` 错误）。

## [1.0.1] - 2025-06-24

> 前身包 `com.parful.filesharer` 的最后一次发布（仓库提交 `v1.0.1`）。更早的逐条变更未留存，故不逐条罗列。

## [1.0.0] - 2024-07-15

> 前身包 `com.parful.filesharer` 的首个发布版本（仓库提交 `v1.0.0`）。
