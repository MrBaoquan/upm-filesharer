using System.Threading.Tasks;
using AndX;
using AndX.Core;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 示例 2：扫码解析与下载授权。
/// 资源页 / H5 与小程序用的是同一套公开接口：<c>/api/scan/{token}</c>（可选登录）与
/// <c>/api/resources/{token}/download</c>（需登录）。
/// 场景结构：Canvas &gt; RawImage（预览）+ 两个 Button（onClick 绑定 <see cref="Resolve"/> /
/// <see cref="Download"/>）+ Text（状态）。
/// 零配置：token 留空时**自举**——先上传一张运行时生成的示例图换来真实票据，再走完整链路；
/// 导入后直接 Play 即可看到「扫码 → 预览 → 下载授权」全流程。
/// </summary>
public class ScanAndDownloadExample : MonoBehaviour
{
    [Tooltip("票据 token：/r/{token} 或 /p/{token} 里的一段，也可直接填整条链接；留空则自举一张票据")]
    public string token = "";

    [Tooltip("小程序 / H5 传入的 q 参数（优先于 token 解析；ShareLink.ExtractToken 会做兼容还原）")]
    public string q = "";

    [Tooltip("解析得到的预览地址（付费未购为低清 / 水印）")]
    public string previewUrl;

    [Tooltip("可选：显示预览图的 RawImage")]
    public RawImage PreviewImage;

    [Tooltip("可选：状态回显用的 Text")]
    public Text StatusText;

    [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到结果）；关掉后只等按钮 / 齿轮菜单触发")]
    public bool autoRunOnStart = true;

    /// <summary>自举或解析过的 token（Download 复用，避免重复上传）。</summary>
    private string resolvedToken;

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

            ScanResolveResult scan = await AndX.Share.ResolveAsync(resolved);
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

            // 预览图走服务端公开地址；QrTextureLoader 是通用纹理下载（与二维码出图同一条链路），须在主线程赋值
            if (PreviewImage != null && !string.IsNullOrEmpty(previewUrl))
            {
                PreviewImage.texture = await QrTextureLoader.LoadAsync(previewUrl);
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
            // AndX.Config.Init(new AndXOptions { Endpoint = "...", AccessTokenProvider = () => loginToken });
            DownloadResult dl = await AndX.Share.GetDownloadAsync(resolved);
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
        ShareResult share = await AndX.Share.UploadAsync(
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
        AndX.Config.InitLocal();
        return true;
    }
}
