using System.Threading.Tasks;
using AndX;
using AndX.Share;
using AndX.Pay;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>AndX SDK 示例：创建门面 → 上传素材 → 显示二维码纹理。</summary>
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

    private AndXHub _hub;

    private void Awake()
    {
        _hub = AndXHub.Create(new AndXOptions
        {
            Endpoint = endpoint,
            EdgeKey = EdgeKey.FromEnvironment(), // 密钥不入包，走环境变量
            Transport = TransportMode.Auto,
        })
        .Use<ShareCapability>()
        .Use<PayCapability>();
    }

    public async void ShareFile()
    {
        // 上传素材（SDK 内部自动分流：当前统一走 Edge 分片控制面）
        ShareResult result = await _hub.Share().UploadAsync(
            new TexturePayload(targetTex),
            new UploadOptions { ExhibitId = exhibitId, Title = "展项截图", Amount = 0 },
            p => Debug.Log($"[AndX] {p.Percent:P0}"));

        // 服务端出图 → 纹理
        DisplayQRCode.texture = await result.LoadQrTextureAsync();
    }

    private void OnDestroy()
    {
        _hub = null;
    }
}
