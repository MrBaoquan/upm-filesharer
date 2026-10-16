using System;
using System.Threading.Tasks;
using AndX;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace AndX.Samples
{
    /// <summary>
    /// 示例 1（Share And QR）：上传素材 → 服务端出码 → 显示二维码。
    ///
    /// 场景结构：Canvas &gt; RawImage（显示二维码）+ Button（onClick 绑定 <see cref="ShareFile"/>）。
    /// 接入方式由 Inspector 的 <c>accessMode</c> 选择，**默认直连 AndX 云服务**（插件持本机节点凭据，
    /// 服务端审计到「节点 + 展项」）；也可切到经本机 AndXEdge。两种方式业务调用完全一样，见 usage.md §15。
    /// 导入后直接 Play 即自动上传场景里已挂好的示例图并出码。
    /// </summary>
    public class ShareAndQrExample : MonoBehaviour
    {
        [Header("场景引用")]
        [Tooltip("用于显示二维码的 RawImage")]
        public RawImage DisplayQRCode;

        [Tooltip("待上传的图片资源")]
        public Texture2D targetTex;

        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据（每机一把、可单独吊销），服务端审计到「节点 + 展项」\n"
            + "Edge=经本机 AndXEdge：鉴权 / 展项标识 / 公网地址由 Edge 代持（展项无外网、多展项共用出口时用）\n"
            + "Auto=本机有节点凭据则直连，否则走 Edge")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：Direct=后端基址（留空读环境变量 ANDX_ENDPOINT）；Edge=本机 Edge 基址（留空用 http://127.0.0.1:6699）")]
        public string endpoint = "";

        [Tooltip("展项 ID（可选）：直连时留空——服务端按节点绑定裁决，客户端无法冒充展项；仅 Hub 中控才必须逐次携带")]
        public string exhibitId = "";

        [Tooltip("素材标题（可选）")]
        public string title = "展项截图";

        [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到结果）；关掉后只等按钮 / 齿轮菜单触发")]
        public bool autoRunOnStart = true;

        private void Awake()
        {
            // 统一入口：按 accessMode 解析基址与凭据；结果（含为什么这么选）打到 Console，不含任何密钥
            TryConfigure();
        }

        private async void Start()
        {
            if (!autoRunOnStart || !AndX.Config.IsConfigured)
            {
                return;
            }

            // 直连模式：先做一次节点自省，把「我是谁、代表哪个展项」打出来
            // ——与审计落库用的是同一个来源，现场排障时这就是证据
            await LogNodeIdentityAsync();
            ShareFile();
        }

        /// <summary>Button.onClick 绑定：上传素材 → 服务端出码 → 显示二维码。</summary>
        public async void ShareFile()
        {
            if (targetTex == null || DisplayQRCode == null)
            {
                Debug.LogError("[AndX] 请在 Inspector 指定 targetTex 与 DisplayQRCode");
                return;
            }

            if (!EnsureConfigured())
            {
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

        /// <summary>齿轮菜单：打印本机节点身份（直连模式；经 Edge 时身份由 Edge 代持，会返回 EDGE_UNAUTHORIZED）。</summary>
        [ContextMenu("节点自省（我是谁）")]
        public async void LogNodeIdentity()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            await LogNodeIdentityAsync();
        }

        private async Task LogNodeIdentityAsync()
        {
            if (AndX.Config.ActiveMode != AndXAccessMode.Direct)
            {
                return;
            }
            try
            {
                NodeIdentity me = await AndX.Node.SelfAsync();
                var authorized = me.AuthorizedExhibitIds == null
                    ? "-"
                    : string.Join(",", me.AuthorizedExhibitIds);
                Debug.Log(
                    $"[AndX] 节点自省：node={me.NodeId} role={me.Role} 绑定展项={me.ExhibitId ?? "-"}"
                    + $" 授权展项={authorized} enabled={me.Enabled} 密钥提示位={me.SecretHint}");
            }
            catch (AndXException e)
            {
                Debug.LogWarning($"[AndX] 节点自省失败 code={e.Code} msg={e.Message}（服务端未部署 S7 自省接口时会这样）");
            }
        }

        /// <summary>
        /// 接入配置（示例里只此一处）：按 <see cref="accessMode"/> 解析基址与凭据。
        /// 直连的凭据来自本机凭据文件（<c>persistentDataPath/andx/node-credential.json</c>，由
        /// <c>AndX.Node.EnrollAsync</c> 首启写入），**不写在代码里**；内网明文基址用环境变量
        /// <c>ANDX_ALLOW_INSECURE_HTTP=true</c> 放行（仅联调）。
        /// </summary>
        private bool TryConfigure()
        {
            try
            {
                AndX.Config.InitAuto(new AndXOptions
                {
                    AccessMode = accessMode,
                    Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint,
                    ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    // 旧轨（过渡期）：仅「用共享密钥直连后端」才需要；只从环境变量取，不入包、不入库
                    EdgeKey = EdgeKey.FromEnvironment(),
                });
                Debug.Log($"[AndX] 接入方式 = {AndX.Config.Describe()}");
                return true;
            }
            catch (AndXException e)
            {
                // 直连但本机还没有节点身份时会走到这里：消息里给了注册方式，或把 accessMode 改成 Edge / Auto
                Debug.LogError($"[AndX] 接入配置失败 code={e.Code} msg={e.Message}");
                return false;
            }
        }

        private bool EnsureConfigured()
        {
            if (AndX.Config.IsConfigured)
            {
                return true;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AndX] 请在 Play 模式下运行示例：SDK 的 Unity 传输在运行时注册。");
                return false;
            }
            return TryConfigure();
        }

        private void OnDestroy()
        {
            // 场景切换 / 退出时清空配置（多场景复用与测试）
            AndX.Config.Reset();
        }
    }
}