# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- **包内示例按能力域补齐（`Samples~/`）**：`AndX Demo`（零配置接入 + 上传 `Texture2D` + 出码，含可运行场景 `QRCodePreview`）、`Scan And Download`（`Share.ResolveAsync` / `Share.GetDownloadAsync`）、`AI Image`（`AI.GetCapabilitiesAsync` → `AI.GenerateImageAsync` → 复用分享链路出码）、`Pay Ticket`（`Pay.CreateTicketAsync` / `Pay.QueryOrderAsync`）；每个示例目录及其文件均带 `.meta`（导入后 GUID 不变，场景引用不丢），元数据统一由 `package.json` 的 `samples` 声明（不使用 `.sample.json`，避免两处元数据冲突）。
- `README.md` 与 `Documentation~/usage.md` 新增「包内示例」章节：示例清单、Package Manager 导入路径与触发方式。

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
