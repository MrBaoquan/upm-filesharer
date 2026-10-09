using System.IO;
using AndX.Core;
using AndX.Unity;
using NUnit.Framework;
using UnityEngine;

namespace AndX.Tests
{
    /// <summary>
    /// 引擎侧载荷测试：<c>Runtime/Unity</c> 的 <see cref="IAndXPayload"/> 实现依赖 UnityEngine，
    /// 不被 dotnet 单测覆盖（<c>Tests~/AndXCore.Tests</c> 只编译 <c>Runtime/Core</c>）。
    /// </summary>
    public class PayloadTests
    {
        private static byte[] ReadAll(IAndXPayload payload)
        {
            using (var stream = payload.OpenReadAsync().GetAwaiter().GetResult())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        [Test]
        public void TexturePayload_encodes_to_png_and_streams_back()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(new[] { (Color32)Color.red, (Color32)Color.green, (Color32)Color.blue, (Color32)Color.white });
                texture.Apply();
                texture.name = "shot";

                var payload = new TexturePayload(texture);

                Assert.AreEqual("shot.png", payload.FileName);
                Assert.AreEqual(MediaType.Image, payload.MediaType);
                Assert.Greater(payload.Length, 0);

                var bytes = ReadAll(payload);
                Assert.AreEqual(payload.Length, bytes.Length);
                // PNG 魔数：89 50 4E 47
                Assert.AreEqual(0x89, bytes[0]);
                Assert.AreEqual((byte)'P', bytes[1]);
                Assert.AreEqual((byte)'N', bytes[2]);
                Assert.AreEqual((byte)'G', bytes[3]);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TexturePayload_rejects_null_texture()
        {
            var ex = Assert.Throws<AndXException>(() => new TexturePayload(null));
            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Test]
        public void ByteArrayPayload_reports_length_and_same_bytes()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var payload = new ByteArrayPayload(data, "clip.mp4", MediaType.Video);

            Assert.AreEqual(5L, payload.Length);
            Assert.AreEqual("clip.mp4", payload.FileName);
            Assert.AreEqual(MediaType.Video, payload.MediaType);
            CollectionAssert.AreEqual(data, ReadAll(payload));
        }

        [Test]
        public void ByteArrayPayload_defaults_file_name_and_media_type()
        {
            var payload = new ByteArrayPayload(new byte[] { 1 });
            Assert.AreEqual("data.bin", payload.FileName);
            Assert.AreEqual(MediaType.Image, payload.MediaType);
        }

        [Test]
        public void ByteArrayPayload_rejects_null_bytes()
        {
            var ex = Assert.Throws<AndXException>(() => new ByteArrayPayload(null));
            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Test]
        public void FilePathPayload_reads_existing_file_and_rejects_missing()
        {
            var path = Path.Combine(Path.GetTempPath(), "andx-payload-" + System.Guid.NewGuid().ToString("N") + ".bin");
            var data = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(path, data);
            try
            {
                var payload = new FilePathPayload(path, MediaType.Video);
                Assert.AreEqual(data.Length, payload.Length);
                Assert.AreEqual(Path.GetFileName(path), payload.FileName);
                CollectionAssert.AreEqual(data, ReadAll(payload));
            }
            finally
            {
                File.Delete(path);
            }

            var ex = Assert.Throws<AndXException>(() => new FilePathPayload(path + ".missing"));
            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }
    }
}
