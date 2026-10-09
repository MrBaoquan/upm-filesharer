# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

### Changed

- 包重命名 `com.parful.filesharer` → `com.parful.andx`，程序集 `FileSharer.Runtime` → `AndX.Runtime`，命名空间 `FileSharer` → `AndX`，示例 `FileSharer Demo` → `AndX Demo`。
- 接口简化为静态入口：`AndX.Config.Init(options)` 一次配置，业务方法挂能力域 `AndX.Share.*` / `AndX.Pay.*`；移除实例门面 `AndXHub`、`Use<T>()` 能力注册与 `ShareCapability` / `PayCapability` 类型。
- 定价改由服务端/管理后台决定：端侧上传请求与 `UploadOptions` 不再包含定价；`ShareResult.Amount` 等仍为服务端回传。
- 票据签发去掉裸字符串：`Share.IssueTicketAsync(purpose, ...)` → `Share.IssueResourceTicketAsync(mediaId, ...)`（付费票据由 `AndX.Pay.CreateTicketAsync` 承担）。
- 空载荷（`Length == 0`）前置为 `CONFIGURATION` 错误，不再发起请求。

### Added

- `AndX.Config.IsConfigured` / `AndX.Config.Reset()`。
- 登录令牌注入：`AndXOptions.AccessToken` / `AccessTokenProvider`，需登录接口自动带 `Authorization: Bearer`。
- 重写 `README.md`：安装、快速开始、常见场景、错误处理、平台与线程注意、API 速查。

### Removed

- `UploadOptions.Amount`（定价不由端侧决定）。
- 旧示例 API（`FileUploader.ShareTexture2D` / `ShareLocalFile`）。

### Fixed

- 修正 `Runtime/Unity/QrTextureExtensions.cs` 的编译错误：`LoadQrTextureAsync` 误扩展 `Pay.PayTicket`（`Pay` 是静态类，`PayTicket` 实为 `AndX` 顶层类型），改回 `PayTicket`。
- 补齐 `Runtime/Core`、`Runtime/Unity` 目录及其全部源文件的 `.meta`（此前缺失，导致 Unity 每次导入重新分配 GUID）。
- 移除无对应代码、且会在每次导入时触发控制台告警的孤儿 `Editor.meta`。

## [1.1.0] - 2019-02-15

### Added

- Danish translation (#297).
- Georgian translation from (#337).
- Changelog inconsistency section in Bad Practices.

### Fixed

- Italian translation (#332).
- Indonesian translation (#336).

## [1.0.0] - 2017-06-20

### Added

- Simplified and Traditional Chinese translations from [@tianshuo](https://github.com/tianshuo).
- German translation from [@mpbzh](https://github.com/mpbzh) & [@Art4](https://github.com/Art4).
- Italian translation from [@azkidenz](https://github.com/azkidenz).
- Swedish translation from [@magol](https://github.com/magol).
- Turkish translation from [@emreerkan](https://github.com/emreerkan).
- French translation from [@zapashcanon](https://github.com/zapashcanon).
- Brazilian Portuguese translation from [@Webysther](https://github.com/Webysther).
- Polish translation from [@amielucha](https://github.com/amielucha) & [@m-aciek](https://github.com/m-aciek).
- Russian translation from [@aishek](https://github.com/aishek).
- Czech translation from [@h4vry](https://github.com/h4vry).
- Slovak translation from [@jkostolansky](https://github.com/jkostolansky).
- Korean translation from [@pierceh89](https://github.com/pierceh89).
- Croatian translation from [@porx](https://github.com/porx).
- Persian translation from [@Hameds](https://github.com/Hameds).
- Ukrainian translation from [@osadchyi-s](https://github.com/osadchyi-s).

### Changed

- Start using "changelog" over "change log" since it's the common usage.
- Start versioning based on the current English version at 0.3.0 to help
  translation authors keep things up-to-date.
- Rewrite "What makes unicorns cry?" section.

### Removed

- Section about "changelog" vs "CHANGELOG