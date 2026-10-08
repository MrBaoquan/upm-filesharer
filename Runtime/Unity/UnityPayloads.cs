using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>内存字节载荷（WebGL 唯一可用形态）。</summary>
    public sealed class ByteArrayPayload : IAndXPayload
    {
        private readonly byte[] _bytes;

        public ByteArrayPayload(byte[] bytes, string fileName = null, MediaType mediaType = MediaType.Image)
        {
            if (bytes == null)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "bytes 不能为空");
            }
            _bytes = bytes;
            FileName = string.IsNullOrEmpty(fileName) ? "data.bin" : fileName;
            MediaType = mediaType;
        }

        public string FileName { get; }

        public MediaType MediaType { get; }

        public long Length
        {
            get { return _bytes.LongLength; }
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(new MemoryStream(_bytes, false));
        }
    }

    /// <summary>磁盘文件载荷（Windows / Android 可用；WebGL 不支持）。</summary>
    public sealed class FilePathPayload : IAndXPayload
    {
        public FilePathPayload(string path, MediaType mediaType = MediaType.Image)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "文件不存在: " + path);
            }
            FullPath = info.FullName;
            Length = info.Length;
            FileName = info.Name;
            MediaType = mediaType;
        }

        public string FullPath { get; }

        public string FileName { get; }

        public MediaType MediaType { get; }

        public long Length { get; }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "WebGL 不支持按路径读取文件，请改用 ByteArrayPayload 或 TexturePayload");
#else
            return Task.FromResult<Stream>(File.OpenRead(FullPath));
#endif
        }
    }

    /// <summary>Texture2D 载荷：构造时在主线程编码为 PNG 字节（WebGL / 桌面通用）。</summary>
    public sealed class TexturePayload : IAndXPayload
    {
        private readonly byte[] _bytes;

        public TexturePayload(Texture2D texture, string fileName = null)
        {
            if (texture == null)
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "Texture2D 不能为空");
            }
            _bytes = texture.EncodeToPNG();
            var baseName = string.IsNullOrEmpty(fileName)
                ? (string.IsNullOrEmpty(texture.name) ? "texture" : texture.name) + ".png"
                : fileName;
            FileName = baseName;
            MediaType = MediaType.Image;
        }

        public string FileName { get; }

        public MediaType MediaType { get; }

        public long Length
        {
            get { return _bytes.LongLength; }
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(new MemoryStream(_bytes, false));
        }
    }
}
