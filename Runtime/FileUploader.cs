using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace FileSharer
{
    public class FileUploader
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<Texture2D> ShareLocalFile(string url, string filePath)
        {
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            return await shareFile(url, fileBytes, System.IO.Path.GetFileName(filePath));
        }

        public static async Task<Texture2D> ShareTexture2D(string url, Texture2D texture)
        {
            // 将Texture2D转换为字节数组
            byte[] textureBytes = texture.EncodeToPNG();
            return await shareFile(url, textureBytes, texture.name + ".png");
        }

        private static async Task<Texture2D> shareFile(
            string url,
            byte[] fileBytes,
            string fileName
        )
        {
            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    ByteArrayContent byteContent = new ByteArrayContent(fileBytes);

                    // Set the content headers
                    byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                        "multipart/form-data"
                    );
                    content.Add(byteContent, "file", fileName);

                    // Make the request
                    HttpResponseMessage response = await client.PostAsync(url, content);

                    // Ensure success status code
                    response.EnsureSuccessStatusCode();

                    // Read response content (if needed)
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var _response = JObject.Parse(responseContent);
                    var _qrCodeUrl = _response["data"]["qrcode_url"];

                    Debug.Log("File uploaded successfully. Response: " + responseContent);
                    return await DownloadImageAsync(_qrCodeUrl.Value<string>());
                }
            }
            catch (Exception e)
            {
                Debug.LogError("File upload failed: " + e.Message);
                return null;
            }
        }

        private static async Task<Texture2D> DownloadImageAsync(string url)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
            {
                var operation = uwr.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Failed to download image: " + uwr.error);
                    return null;
                }
                else
                {
                    return DownloadHandlerTexture.GetContent(uwr);
                }
            }
        }
    }
}
