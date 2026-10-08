# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

### Changed

- 包重命名 `com.parful.filesharer` → `com.parful.andx`，程序集 `FileSharer.Runtime` → `AndX.Runtime`，命名空间 `FileSharer` → `AndX`，示例 `FileSharer Demo` → `AndX Demo`。
- 接口简化为静态入口：`AndX.Config.Init(options)` 一次配置，业务方法挂能力域 `AndX.Share.*` / `AndX.Pay.*`；移除实例门面 `AndXHub`、`Use<T>()` 能力注册与 `ShareCapability` / `PayCapability` 类型。
- 定价改由服务端/管理后台决定：端侧上传请求与 `UploadOptions` 不再包含定价；`ShareResult.Amount` 等仍为服务端回传。

### Added

- `AndX.Config.IsConfigured` / `AndX.Config.Reset()`。
- 重写 `README.md`：安装、快速开始、常见场景、错误处理、平台与线程注意、API 速查。

### Removed

- `UploadOptions.Amount`（定价不由端侧决定）。
- 旧示例 API（`FileUploader.ShareTexture2D` / `ShareLocalFile`）。

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