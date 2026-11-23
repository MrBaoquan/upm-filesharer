# UNIHper 示例场景

要求：UNIHper `1.25.1108+`。

打开 `AndXUNIHperDemo` 场景并进入 Play：

1. 工程已有常驻 `UNIHperEntry` 时直接复用；没有时由示例场景的
   `AndXUNIHperDemoBootstrap` 实例化 `Resources/UNIHper/Prefabs/UNIHper`。
2. UI 是 UNIHper 管理的 UI Toolkit 页面：`AndXUNIHperDemoUI.cs` +
   `Resources/UIToolkit/AndXUNIHperDemoUI.uxml/.uss`。
3. `AndXUNIHperSettings.OnLoaded()` 自动读取 `Configs/AndX.xml`。
4. 场景进入后自动上传一张运行时纹理并显示二维码，无需点击。
5. 页面提供“扫码收图”：插件生成普通 HTTPS 二维码，微信扫码经普通链接规则进入小程序上传页，其他浏览器打开同一 URL 的 H5 兜底页；上传完成后插件自动下载并显示图片。
6. 页面还提供扫码解析、下载授权、付费票据、订单查询、AI 文字和显式 AI 生图入口。
7. 付费演示为「签付费码 → 扫码支付 → 付费开始游戏 → STARTING → PLAYING → 3 秒后 IDLE」，复位后可直接再次签付费码。
8. 测试项目默认 `Exhibit ID=2`，可在输入框修改；展项机可留空。
9. “重载 AndX.xml”演示外部修改配置后的运行时重载。

> 当前 SDK 请求字段使用内部 Exhibit ID；运营配置推荐使用 `P{项目码}-E{展项码}`，
> 后续应由业务契约增加编码解析，不要直接把业务编码当成数据库主键。

服务地址与接入方式由 `AndX.xml` 的 `Endpoint` / `AccessMode` 决定。
开发期允许在 `AndX.xml` 明文填写节点 Secret；生产包必须移除真实密钥并改用安全存储或宿主注入。
