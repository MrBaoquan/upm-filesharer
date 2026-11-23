using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using Parful.AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Parful.AndX.Samples
{
    [System.Serializable]
    public sealed class OnlineMediaCase
    {
        public string label;
        public string token;
        public bool paid;
    }

    /// <summary>
    /// 示例 2（Scan And Download）：扫码解析与下载授权。
    /// 资源页 / H5 与小程序用的是同一套公开接口：<c>/api/scan/{token}</c>（可选登录）与
    /// <c>/api/resources/{token}/download</c>（需登录）。
    /// 场景结构：Canvas &gt; RawImage（预览）+ 两个 Button（onClick 绑定 <see cref="Resolve"/> /
    /// <see cref="Download"/>）+ Text（状态）。
    /// 接入方式见 usage.md §15（默认直连 AndX 云服务，可切经本机 AndXEdge）。
    /// 零配置：token 留空时**自举**——先上传一张运行时生成的示例图换来真实票据，再走完整链路；
    /// 导入后直接 Play 即可看到「扫码 → 预览 → 下载授权」全流程。
    /// </summary>
    public class ScanAndDownloadExample : MonoBehaviour
    {
        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据（每机一把、可单独吊销），服务端审计到「节点 + 展项」\n"
            + "Edge=经本机 AndXEdge：鉴权 / 展项标识 / 公网地址由 Edge 代持（展项无外网、多展项共用出口时用）\n"
            + "Auto=本机有节点凭据则直连，否则走 Edge")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：默认线上 https://andx.andcrane.com；联调时可覆盖。Edge 模式默认使用 http://127.0.0.1:6699")]
        public string endpoint = "https://andx.andcrane.com";

        [Header("示例参数")]
        [Tooltip("票据 token：/r/{token} 或 /p/{token} 里的一段，也可直接填整条链接；留空则自举一张票据")]
        public string token = "";

        [Tooltip("小程序 / H5 传入的 q 参数（优先于 token 解析；ShareLink.ExtractToken 会做兼容还原）")]
        public string q = "";

        [Tooltip("解析得到的预览地址（付费未购为低清 / 水印）")]
        public string previewUrl;

        [Tooltip("可选：显示预览图的 RawImage")]
        public RawImage PreviewImage;

        [Tooltip("可选：显示当前票据二维码的 RawImage；付费未购也能出码，扫码打开后可继续支付")]
        public RawImage DisplayQRCode;

        [Tooltip("可选：状态回显用的 Text")]
        public Text StatusText;

        [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到结果）；关掉后只等按钮 / 齿轮菜单触发")]
        public bool autoRunOnStart = true;

        [Header("线上专项测试（AUTOTEST）")]
        [Tooltip("启用后在 Game 视图直接切换免费/付费图片与视频用例")]
        public bool useOnlineTestCases = true;

        [Tooltip("线上长期测试票据；Inspector 可覆盖")]
        public OnlineMediaCase[] onlineTestCases = new OnlineMediaCase[0];

        [Tooltip("仅付费资源下载测试用：临时填入小程序登录态，或设置环境变量 ANDX_E2E_USER_TOKEN；不要提交到仓库")]
        public string userAccessToken = "";

        /// <summary>自举或解析过的 token（Download 复用，避免重复上传）。</summary>
        private string resolvedToken;
        private int currentOnlineCaseIndex = -1;

        private void Awake()
        {
            ApplyDefaultOnlineCases();
            // 统一入口：按 accessMode 解析基址与凭据；结果（含为什么这么选）打到 Console，不含任何密钥
            TryConfigure();
        }

        private void Start()
        {
            if (autoRunOnStart)
            {
                Resolve();
            }
        }

        [ContextMenu("解析票据（Resolve）")]
        public async void Resolve()
        {
            if (!EnsureConfigured())
            {
                return;
            }

            try
            {
                var resolved = await EnsureTokenAsync();
                if (resolved == null)
                {
                    return;
                }

                ScanResolveResult scan = await global::AndX.Share.ResolveAsync(resolved);
                previewUrl = scan.PreviewUrl;

                var exhibit = scan.Exhibit;
                var media = scan.Media;
                Debug.Log(
                    $"[AndX] purpose={scan.Purpose}"
                    + $" 展项={(exhibit == null ? "-" : exhibit.Name)}"
                    + $" 素材={(media == null ? "-" : media.Title)}"
                    + $" 时长={(media == null ? null : media.Duration)}"
                    + $" 定价(分)={(media == null ? 0 : media.Amount)}"
                    + $" 已购={scan.Entitled}"
                    + $" 预览={scan.PreviewUrl}");
                SetStatus($"解析成功：token={resolved} 已购={scan.Entitled} 定价(分)={(media == null ? 0 : media.Amount)}");

                // 无论图片/视频、免费/付费，都展示可扫描的票据二维码。
                // 付费未购时预览地址为空是正常现象，二维码才是继续支付/打开的入口。
                if (DisplayQRCode != null)
                {
                    DisplayQRCode.texture = await QrTextureLoader.LoadAsync(global::AndX.Share.QrImageUrl(resolved));
                }

                // 仅图片预览走纹理；付费未购或视频时清空旧图，避免切换用例后残留上一个案例的画面
                if (media != null && media.Type == "video")
                {
                    SetStatus(string.IsNullOrEmpty(previewUrl)
                        ? "已生成票据二维码；付费视频需扫码支付后下载"
                        : "已生成票据二维码；视频预览地址已获取");
                }
                if (PreviewImage != null)
                {
                    PreviewImage.texture = media != null && media.Type == "image" && !string.IsNullOrEmpty(previewUrl)
                        ? await QrTextureLoader.LoadAsync(previewUrl)
                        : null;
                }
            }
            catch (AndXException e)
            {
                // TICKET_NOT_FOUND / TICKET_EXPIRED / TICKET_REVOKED
                Debug.LogError($"[AndX] 解析失败 code={e.Code} msg={e.Message}");
                SetStatus($"解析失败：{e.Code}");
            }
        }

        [ContextMenu("下载授权（Download）")]
        public async void Download()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            var resolved = ResolveCachedOrConfiguredToken();
            if (resolved == null)
            {
                Debug.LogWarning("[AndX] 请先点「解析票据」（示例会自举一张票据），或在 Inspector 填 token");
                SetStatus("请先点「解析票据」");
                return;
            }

            try
            {
                // 需登录：配置令牌后 SDK 自动注入 Authorization: Bearer，例如
                // AndX.Config.InitAuto(new AndXOptions { AccessTokenProvider = () => loginToken });
                DownloadResult dl = await global::AndX.Share.GetDownloadAsync(resolved);
                Debug.Log($"[AndX] 下载授权 {dl.ExpiresIn}s type={dl.Type} title={dl.Title} url={dl.Url}");
                SetStatus($"下载授权：{dl.Type}（{dl.ExpiresIn}s）已在浏览器打开");
                Application.OpenURL(dl.Url);
            }
            catch (AndXException e)
            {
                // 未登录：UNAUTHORIZED；付费未购：ENTITLEMENT_REQUIRED
                Debug.LogWarning($"[AndX] 下载未授权 code={e.Code} msg={e.Message}");
                SetStatus($"下载未授权：{e.Code}（需登录 / 需先购买）");
            }
        }

        private void ApplyDefaultOnlineCases()
        {
            if (onlineTestCases == null || onlineTestCases.Length == 0)
            {
                onlineTestCases = new[]
                {
                    new OnlineMediaCase { label = "免费图片", token = "tAIRmB9HEReXlGAZ", paid = false },
                    new OnlineMediaCase { label = "付费图片", token = "mExZaChG8JbnMPwC", paid = true },
                    new OnlineMediaCase { label = "免费视频", token = "TJHY7mWFAv3Zw4La", paid = false },
                    new OnlineMediaCase { label = "付费视频", token = "zG2RumjkGVU4CXzi", paid = true },
                };
            }
            if (useOnlineTestCases && string.IsNullOrEmpty(token) && onlineTestCases.Length > 0)
            {
                token = onlineTestCases[0].token;
            }
        }

        private string ResolveAccessToken()
        {
            if (!string.IsNullOrEmpty(userAccessToken))
            {
                return userAccessToken;
            }
            return System.Environment.GetEnvironmentVariable("ANDX_E2E_USER_TOKEN");
        }

        private void RunOnlineCase(int index)
        {
            if (onlineTestCases == null || index < 0 || index >= onlineTestCases.Length)
            {
                return;
            }
            currentOnlineCaseIndex = index;
            token = onlineTestCases[index].token;
            q = string.Empty;
            resolvedToken = null;
            Resolve();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !useOnlineTestCases || onlineTestCases == null)
            {
                return;
            }
            GUILayout.BeginArea(new Rect(16, 16, 360, 56 + onlineTestCases.Length * 34), GUI.skin.box);
            GUILayout.Label("AndX 线上专项测试");
            for (var i = 0; i < onlineTestCases.Length; i++)
            {
                var item = onlineTestCases[i];
                var mark = i == currentOnlineCaseIndex ? "▶ " : string.Empty;
                if (GUILayout.Button($"{mark}{item.label} · {(item.paid ? "付费" : "免费")}"))
                {
                    RunOnlineCase(i);
                }
            }
            GUILayout.EndArea();
        }

        private string ResolveCachedOrConfiguredToken()
        {
            if (!string.IsNullOrEmpty(resolvedToken))
            {
                return resolvedToken;
            }
            var configured = ShareLink.ExtractToken(q, scene: null, token: token);
            return string.IsNullOrEmpty(configured) ? null : configured;
        }

        /// <summary>
        /// 取一个可解析的 token：优先用 Inspector 配置的 token / q；都没填时自举——
        /// 上传一张运行时生成的示例图换来真实票据（与「上传 → 出码 → 扫码」同一条服务端链路）。
        /// </summary>
        private async Task<string> EnsureTokenAsync()
        {
            var configured = ResolveCachedOrConfiguredToken();
            if (configured != null)
            {
                resolvedToken = configured;
                return resolvedToken;
            }

            SetStatus("未配置 token，正在自举：上传示例图 → 签发票据…");
            ShareResult share = await global::AndX.Share.UploadAsync(
                new TexturePayload(BuildSampleTexture()),
                new UploadOptions { Title = "AndX 示例（扫码自举）" });
            resolvedToken = share.Token;
            previewUrl = share.ShareUrl;
            SetStatus($"自举成功 token={resolvedToken}，正在解析…");
            return resolvedToken;
        }

        /// <summary>运行时生成一张示例图（渐变 + 网格），示例因此不依赖任何外部图片资源。</summary>
        private static Texture2D BuildSampleTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    int grid = (x % 32 < 2 || y % 32 < 2) ? 40 : 0;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(x - grid, 0, 255),
                        (byte)Mathf.Clamp(y - grid, 0, 255),
                        (byte)Mathf.Clamp(255 - x - grid, 0, 255),
                        255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>状态回显：场景里挂了 Status Text 时才有输出。</summary>
        private void SetStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.text = message;
            }
        }

        /// <summary>
        /// 接入配置（示例里只此一处）：按 <see cref="accessMode"/> 解析基址与凭据。
        /// 直连的凭据来自本机凭据文件（<c>persistentDataPath/andx/node-credential.json</c>，由
        /// <c>AndX.Node.EnrollAsync</c> 首启写入），**不写在代码里**。
        /// </summary>
        private bool TryConfigure()
        {
            try
            {
                global::AndX.Config.InitAuto(new AndXOptions
                {
                    AccessMode = accessMode,
                    Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint,
                    EdgeKey = EdgeKey.FromEnvironment(),
                    AccessTokenProvider = ResolveAccessToken,
                });
                Debug.Log($"[AndX] 接入方式 = {global::AndX.Config.Describe()}");
                return true;
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX] 接入配置失败 code={e.Code} msg={e.Message}");
                return false;
            }
        }

        private bool EnsureConfigured()
        {
            if (global::AndX.Config.IsConfigured)
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
            global::AndX.Config.Reset();
        }
    }
}
