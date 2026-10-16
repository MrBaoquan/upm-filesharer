using System;
using AndX;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 示例 1：上传素材并出码。
/// 场景结构：Canvas &gt; RawImage（显示二维码）+ Button（onClick 绑定 <see cref="ShareFile"/>）。
/// 默认零配置接入本机 AndXEdge（http://127.0.0.1:6699）；导入后直接 Play 即自动上传
/// 场景里已挂好的示例图并出码；接入说明见 Documentation~/usage.md §3。
/// </summary>
public class AndXExample : MonoBehaviour
{
    [Header("场景引用")]
    [Tooltip("用于显示二维码的 RawImage")]
    public RawImage DisplayQRCode;

    [Tooltip("待上传的图片资源")]
    public Texture2D targetTex;

    [Header("接入配置（留空 = 零配置接入本机 AndXEdge）")]
    [Tooltip("留空走 AndX.Config.InitLocal()；填了则直连该基址（直连推荐用节点身份 NodeCredential，见 usage.md §15）")]
    public string endpoint = "";

    [Tooltip("展项 ID（可选）：本机 Edge 已绑定展项时留空")]
    public string exhibitId = "";

    [Tooltip("素材标题（可选）")]
    public string title = "展项截图";

    [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到结果）；关掉后只等按钮 / 齿轮菜单触发")]
    public bool autoRunOnStart = true;

    private void Awake()
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            // 推荐：零参数接入本机 AndXEdge，鉴权 / 展项标识 / 公网地址均由 Edge 代持，插件不持密钥
            AndX.Config.InitLocal();
        }
        else
        {
            // 高级：直连指定基址。
            // 直连后端的推荐方式是**节点身份**（每机一把、可单独吊销、服务端审计到「节点 + 展项」）：
            //   var store = NodeCredentialStorage.Create();
            //   var credential = store.Load() ?? await AndX.Node.EnrollAsync(endpoint, token, nodeId, store: store);
            //   AndX.Config.Init(new AndXOptions { Endpoint = endpoint, NodeCredential = credential });
            // 下面的 EdgeKey 仅「旧轨/过渡期」共享密钥直连时才需要（只从环境变量取，不入包、不入库）。
            AndX.Config.Init(new AndXOptions
            {
                Endpoint = endpoint,
                ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                EdgeKey = EdgeKey.FromEnvironment(),
            });
        }
    }

    private void Start()
    {
        if (autoRunOnStart)
        {
            // 零配置：Awake 已 InitLocal()，这里直接跑一遍上传 + 出码
            ShareFile();
        }
    }

    /// <summary>Button.onClick 绑定：上传素材 → 服务端出码 → 显示二维码。</summary>
    public async void ShareFile()
    {
        if (targetTex == null || DisplayQRCode == null)
        {
            Debug.LogError("[AndX] 请在 Inspector 指定 targetTex 与 DisplayQRCode");
            return;
        }

        try
        {
            // 上传对调用方透明：≤ ChunkSize 一次性透传，大文件自动分片续传
            // 进度用 System.Progress<T> 包一层（IProgress<T> 是接口，不能直接传 lambda）；
            // 它在构造时捕获同步上下文，回调会回到主线程，可直接刷新 UI。
            ShareResult result = await AndX.Share.UploadAsync(
                new TexturePayload(targetTex),
                new UploadOptions
                {
                    ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    Title = title,
                },
                progress: new Progress<UploadProgress>(p => Debug.Log($"[AndX] 上传 {p.Percent:P0}")));

            Debug.Log($"[AndX] 出码 token={result.Token} 定价(分)={result.Amount} 链接={result.ShareUrl}");

            // 二维码由服务端出图，端侧只下载显示（须在主线程调用）
            DisplayQRCode.texture = await result.LoadQrTextureAsync();
        }
        catch (AndXException e)
        {
            // 统一异常：Code 为稳定错误码（网络错误 / 5xx 已按 MaxRetries 自动重试，4xx 直接抛出）
            Debug.LogError($"[AndX] 上传失败 code={e.Code} status={e.HttpStatus} trace={e.TraceId} msg={e.Message}");
        }
    }

    private void OnDestroy()
    {
        // 场景切换 / 退出时清空配置（多场景复用与测试）
        AndX.Config.Reset();
    }
}
