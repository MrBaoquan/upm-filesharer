using System.Threading.Tasks;
using UnityEngine;

namespace Parful.AndX.Unity
{
    /// <summary>反向上传二维码与用户图片的纹理加载。</summary>
    public static class ReceiveUploadTextureExtensions
    {
        public static Task<Texture2D> LoadQrTextureAsync(this ReceiveUploadSession session)
        {
            return session == null ? Task.FromResult<Texture2D>(null) : QrTextureLoader.LoadAsync(session.QrImageUrl);
        }

        public static Task<Texture2D> LoadTextureAsync(this ReceiveUploadMedia media)
        {
            return media == null ? Task.FromResult<Texture2D>(null) : QrTextureLoader.LoadAsync(media.DownloadUrl);
        }
    }
}
