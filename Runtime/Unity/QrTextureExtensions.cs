using System.Threading.Tasks;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>二维码纹理便捷方法：<c>qrDisplay.texture = await result.LoadQrTextureAsync()</c>。</summary>
    public static class QrTextureExtensions
    {
        public static Task<Texture2D> LoadQrTextureAsync(this ShareResult result)
        {
            return result == null ? Task.FromResult<Texture2D>(null) : QrTextureLoader.LoadAsync(result.QrImageUrl);
        }

        public static Task<Texture2D> LoadQrTextureAsync(this IssuedTicket ticket)
        {
            return ticket == null ? Task.FromResult<Texture2D>(null) : QrTextureLoader.LoadAsync(ticket.QrImageUrl);
        }

        public static Task<Texture2D> LoadQrTextureAsync(this PayTicket ticket)
        {
            return ticket == null ? Task.FromResult<Texture2D>(null) : QrTextureLoader.LoadAsync(ticket.QrImageUrl);
        }
    }
}
