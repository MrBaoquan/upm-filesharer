using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AndX.Unity
{
    /// <summary>二维码纹理加载（服务端出图，端侧只下载显示，不内置 QR 编码库）。</summary>
    public static class QrTextureLoader
    {
        public static async Task<Texture2D> LoadAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }
            using (var www = UnityWebRequestTexture.GetTexture(url))
            {
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
#if UNITY_2020_2_OR_NEWER
                if (www.result != UnityWebRequest.Result.Success)
#else
                if (www.isNetworkError || www.isHttpError)
#endif
                {
                    Debug.LogWarning("[AndX] 二维码下载失败: " + www.error + " (" + url + ")");
                    return null;
                }
                return DownloadHandlerTexture.GetContent(www);
            }
        }
    }
}
