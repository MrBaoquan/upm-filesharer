using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>
    /// **场景化 AI 生图**（与 <c>AndX.AI</c> 原始接口并存）：端侧只给素材 + 少量参数，
    /// 提示词模板、出图尺寸与**比例保护**由服务端按场景决定——例如换脸按模板比例出图，不会拉伸变形。
    ///
    /// 典型用法：
    /// <code>
    /// AIJob job = await AndX.Unity.AIScenarios.FaceSwapAsync(faceTexture, templateTexture);
    /// resultImage.texture = await job.LoadResultTextureAsync();
    /// </code>
    /// 参考图编码（缩图 + JPEG + data URL）在 <see cref="EncodeReference"/> 内统一处理，
    /// 现场只需调 <c>maxEdge</c> / <c>jpegQuality</c> 控制体积与耗时。
    /// </summary>
    public static class AIScenarios
    {
        /// <summary>把纹理编码成 `data:image/jpeg;base64,…`（长边 ≤ maxEdge；须在主线程调用）</summary>
        public static string EncodeReference(Texture2D texture, int maxEdge = 1280, int jpegQuality = 85)
        {
            if (texture == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "参考图纹理不能为空");
            }
            var scaled = Downscale(texture, Mathf.Max(256, maxEdge));
            var jpeg = scaled.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
            return "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
        }

        /// <summary>
        /// 换脸 / 融合：`face` 的人脸换到 `template` 上。
        /// **出图尺寸由服务端按模板原始比例换算**（含面积下限/长边上限），不会变形。
        /// </summary>
        public static Task<AIJob> FaceSwapAsync(
            Texture2D face,
            Texture2D template,
            int maxEdge = 1280,
            int jpegQuality = 85,
            string exhibitId = null,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = new Dictionary<string, object>
            {
                ["images"] = new[] { EncodeReference(face, maxEdge, jpegQuality), EncodeReference(template, maxEdge, jpegQuality) },
            };
            return AndX.AI.GenerateScenarioAsync(
                AndXContract.AIScenarios.FaceSwap,
                inputs,
                exhibitId: exhibitId,
                waitOptions: waitOptions,
                progress: progress,
                cancellationToken: cancellationToken);
        }

        /// <summary>风格化：把照片转成指定风格（动漫 / 油画 / 国潮…）</summary>
        public static Task<AIJob> StyleTransferAsync(
            Texture2D photo,
            string style,
            int maxEdge = 1280,
            int jpegQuality = 85,
            string exhibitId = null,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = new Dictionary<string, object>
            {
                ["images"] = new[] { EncodeReference(photo, maxEdge, jpegQuality) },
            };
            var parameters = new Dictionary<string, object> { ["style"] = style ?? string.Empty };
            return AndX.AI.GenerateScenarioAsync(
                AndXContract.AIScenarios.Style,
                inputs,
                parameters,
                exhibitId: exhibitId,
                waitOptions: waitOptions,
                progress: progress,
                cancellationToken: cancellationToken);
        }

        /// <summary>文生图：只给提示词（无参考图）</summary>
        public static Task<AIJob> TextToImageAsync(
            string prompt,
            string size = null,
            string exhibitId = null,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            var parameters = string.IsNullOrEmpty(size)
                ? null
                : new Dictionary<string, object> { ["size"] = size };
            return AndX.AI.GenerateImageAsync(
                new AIImageRequest
                {
                    Scenario = AndXContract.AIScenarios.TextToImage,
                    Prompt = prompt,
                    Params = parameters,
                    ExhibitId = exhibitId,
                },
                waitOptions,
                progress,
                cancellationToken);
        }

        private static Texture2D Downscale(Texture2D source, int maxEdge)
        {
            var longest = Mathf.Max(source.width, source.height);
            if (longest <= maxEdge)
            {
                return source;
            }
            var scale = (float)maxEdge / longest;
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}