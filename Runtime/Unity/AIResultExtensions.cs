using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>
    /// AI 产物的端侧便捷方法：**显示**（纹理）与**留存**（落盘）。
    ///
    /// 三条互不排斥的用法：
    /// - **屏幕上直接显示生成结果**：<c>resultImage.texture = await job.LoadResultTextureAsync();</c>
    /// - **出码让观众扫码带走**：<c>await AndX.Share.IssueResourceTicketAsync(job.MediaId)</c>
    /// - **把原图存到本地**（留存 / 打印 / 二次加工）：<see cref="SaveAsync(AIJob, int, string, string, CancellationToken)"/>
    ///
    /// 结果图地址是 **presigned（10 分钟有效）**：
    /// - 刚拿到结果就显示/保存 → 用带 <see cref="AIJob"/> 的重载（快，直接用任务里的地址）；
    /// - 长期展示、延迟取图、隔一会儿才保存 → 用带 <c>jobNo</c> 的重载（先查任务拿新地址，稳）。
    ///
    /// 所有方法都**必须在主线程调用**（依赖 UnityWebRequest / Texture2D）。
    /// </summary>
    public static class AIResultLoader
    {
        /// <summary>下载第 index 张结果图为纹理（用任务对象里的地址；越界或未出结果时返回 null 并告警）。</summary>
        public static Task<Texture2D> LoadTextureAsync(AIJob job, int index = 0)
        {
            var url = job == null ? null : job.ResultUrlAt(index);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning(
                    "[AndX.AI] 没有第 " + index + " 张结果图（任务 " + (job == null ? "(null)" : job.JobNo + " 状态 " + job.Status)
                    + "；当前共 " + (job == null ? 0 : job.ResultCount) + " 张）");
                return Task.FromResult<Texture2D>(null);
            }
            return QrTextureLoader.LoadAsync(url);
        }

        /// <summary>先查任务（拿新的 presigned 地址）再下载纹理：适合长期展示 / 迟到取图。</summary>
        public static async Task<Texture2D> LoadTextureAsync(string jobNo, int index = 0, CancellationToken cancellationToken = default)
        {
            var bytes = await AndX.AI.GetResultBytesAsync(jobNo, index, cancellationToken).ConfigureAwait(true);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                Debug.LogWarning("[AndX.AI] 结果图解码失败（jobNo=" + jobNo + " index=" + index + "）");
                return null;
            }
            return texture;
        }

        /// <summary>
        /// 把第 index 张结果图保存到本地，返回写入路径。
        /// 默认落在 <c>Application.persistentDataPath/andx/ai/{jobNo}-{index}.png</c>。
        /// </summary>
        public static Task<string> SaveAsync(
            AIJob job,
            int index = 0,
            string fileName = null,
            string directory = null,
            CancellationToken cancellationToken = default)
        {
            var name = string.IsNullOrEmpty(fileName)
                ? (job == null || string.IsNullOrEmpty(job.JobNo) ? "ai-job" : job.JobNo) + "-" + index + ".png"
                : fileName;
            return AndX.AI.SaveResultAsync(job, index, ResolvePath(directory, name), cancellationToken);
        }

        /// <summary>
        /// 同 <see cref="SaveAsync(AIJob, int, string, string, CancellationToken)"/>，但**先用 jobNo 刷新任务**
        /// （结果图地址是 presigned，10 分钟后失效）：隔一段时间才保存时用这个重载。
        /// </summary>
        public static Task<string> SaveAsync(
            string jobNo,
            int index = 0,
            string fileName = null,
            string directory = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }
            var name = string.IsNullOrEmpty(fileName) ? jobNo + "-" + index + ".png" : fileName;
            return AndX.AI.SaveResultAsync(jobNo, index, ResolvePath(directory, name), cancellationToken);
        }

        private static string ResolvePath(string directory, string fileName)
        {
            var dir = string.IsNullOrEmpty(directory)
                ? Path.Combine(Application.persistentDataPath, "andx", "ai")
                : directory;
            return Path.Combine(dir, fileName);
        }
    }

    /// <summary>AI 任务的纹理便捷扩展（与二维码的 <c>LoadQrTextureAsync</c> 同构）。</summary>
    public static class AIResultTextureExtensions
    {
        /// <summary>第 index 张结果图的纹理；<c>resultImage.texture = await job.LoadResultTextureAsync();</c></summary>
        public static Task<Texture2D> LoadResultTextureAsync(this AIJob job, int index = 0)
        {
            return AIResultLoader.LoadTextureAsync(job, index);
        }
    }
}