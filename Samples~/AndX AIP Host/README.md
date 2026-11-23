# AndX AIP Host Sample

This sample demonstrates the low-level AIP host API and the versioned capability router.

## Capability scene

Package Manager 导入 **AndX AIP Host** 后，打开 `AndXAipCapabilityDemo.unity` 并点击 Play。

场景使用本地 `DemoAdapter` 和 `DemoPublisher`，不连接生产 AIP，会自动验证：

1. `start` 进入 `STARTING`；
2. Adapter 初始化完成后上报 `PLAYING`；
3. 在 `PLAYING` 状态下路由 `qiuqian.draw`；
4. 业务动作完成后上报 `IDLE`。

界面会显示当前状态、命令日志和 `state.publish` revision。真实展项必须把 `DemoAdapter` 替换为硬件 Adapter，并按实际可交互、出签完成和安全复位时点上报状态。

## Low-level AIP host

For production exhibit integration, use `ExhibitRuntime` with `IExhibitRuntimeAdapter`:
see `Documentation~/qiuqianji-integration.md` and chapter 16 of `Documentation~/usage.md`.

1. Add `AndXAipBehaviour` to a scene object.
2. Put `aip.json` under `StreamingAssets/andx/`.
3. Add `AndXAipHostExample` and assign the `AndXAipBehaviour` reference.
4. The sample calls `SetCommandHandler` and returns an `AipCommandResult`.
5. Do not call IODevice from a WebSocket callback thread; register a handler that marshals hardware work to the Unity main thread.

## 求签机业务能力

把 `QiuQianDrawCapabilityHandler` 注册到 `ExhibitRuntimeOptions.CapabilityHandlers`，服务端下发 `qiuqian.draw` 时只会经过该 Handler；`start/stop/safety.stop` 仍由 ExhibitRuntime 生命周期处理。`AndXAipCapabilityDemoBehaviour` 提供了无需后端的可运行参考实现。
