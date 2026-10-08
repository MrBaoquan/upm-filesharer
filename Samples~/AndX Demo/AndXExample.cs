using System.Threading.Tasks;
using AndX;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>AndX SDK 示例：一次配置 → 上传素材 → 显示二维码。</summary>
public class AndXExample : MonoBehaviour
{
    [Tooltip("用于显示二维码的 RawImage")]
    public RawImage DisplayQRCode;

    [Tooltip("待上传的图片资源")]
    public Texture2D targetTex;

    [Tooltip("AndX 服务端 / 边缘网关基址")]
    public string endpoint = "http://127.0.0.1:8080";

    [Tooltip("展项 ID")]
    public string exhibitId = "1024";

    private void Awake()
    {
        // 一次配置；密钥不入包，走环境变量（ANDX_EDGE_KEY）
        AndX.Config.Init(new AndXOptions
        {
            Endpoint = endpoint,
            EdgeKey = EdgeKey.FromEnvironment(),
            AllowInsecureHttp = true, // 仅本地联调；生产移除
        });
    }

    public async void ShareFile()
    {
        // 上传素材（SDK 内部自动分流：小文件一次性透传 / 大文件分片续传）
        ShareResult result = await AndX.Share.UploadAsync(
            new TexturePayload(targetTex),
            new UploadOptions { ExhibitId = exhibitId, Title = "展项截图" },
            p => Debug.Log($"[AndX] {p.Percent:P0}"));

        // 服务端出图 → 纹理
        DisplayQRCode.texture = await result.LoadQrTextureAsync();
    }

    private void OnDestroy()
    {
        AndX.Config.Reset();
    }
}
