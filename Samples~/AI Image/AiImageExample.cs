using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using AndX.Unity;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace AndX.Samples
{
    /// <summary>
    /// 示例 3（AI Image）：**图生图（换脸/融合）** + 结果图显示 + 二维码。
    ///
    /// 输入取自 <c>Assets/StreamingAssets/</c> 下两张图（默认「人脸.png」「模板.png」，可在 Inspector 改），
    /// 端侧把它们缩到长边 ≤ <see cref="referenceMaxEdge"/> 并转 JPEG 后**内联 base64** 提交——
    /// 这样参考图不经过对象存储、也不要求厂商能访问内网 MinIO（现场只有内网时这是唯一可行路径）。
    ///
    /// 一次生图的三条产出路径彼此独立（可只用其中一条）：
    /// 1. 屏幕显示目标图 → <c>ResultImage</c>；
    /// 2. 出码扫码带走 → <c>DisplayQRCode</c>（与 1 **并行**，不互相等待）；
    /// 3. 落盘留存 → 齿轮菜单「保存结果图到本地」。
    ///
    /// 日志里会打印**分段耗时**（读图编码 / 提交 / 生成等待 / 结果图显示 / 出码+二维码），
    /// 用于定位"时间花在哪"；服务端还会把生成阶段拆成「厂商出图 vs 产物落库」写进任务结果。
    /// </summary>
    public class AiImageExample : MonoBehaviour
    {
        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据；Edge=经本机 AndXEdge；Auto=有凭据则直连")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：留空 = 默认云服务基址；也可用环境变量 ANDX_ENDPOINT（本机联调填 http://127.0.0.1）")]
        public string endpoint = "";

        [Header("参考图（相对 Assets/StreamingAssets/）")]
        [Tooltip("人脸图（相对 Assets/StreamingAssets/）：**留空=用示例自带的 faceImage**（现场换图无需重编则填这里）")]
        public string faceImagePath = "";

        [Tooltip("模板图（相对 Assets/StreamingAssets/）：**留空=用示例自带的 templateImage**")]
        public string templateImagePath = "";

        [Tooltip("示例自带的人脸候选图（随包分发；每次生成**随机**挑一张）")]
        public Texture2D[] faces;

        [Tooltip("示例自带的模板候选图（随包分发；每次生成**随机**挑一张）")]
        public Texture2D[] templates;

        [Tooltip("是否每次生成随机换一组「人脸 × 模板」（关掉则固定用第 1 组）")]
        public bool randomPairPerRun = true;

        [Tooltip("参考图长边上限（px）：越小上传/生成越快，建议 1024~1536")]
        public int referenceMaxEdge = 1280;

        [Tooltip("参考图 JPEG 质量（1~100）：越小越快，建议 80~90")]
        public int referenceJpegQuality = 85;

        [Header("示例参数")]
        [Tooltip("提示词：默认是「把第 1 张图的人脸融合到第 2 张模板」")]
        public string prompt = "把第 1 张图中人物的面部自然融合到第 2 张模板画面里，保持模板的构图、光线与风格，人脸比例与角度匹配，高清写实";

        [Tooltip("尺寸（可选），如 1024x1024；留空由服务端 / 模型决定")]
        public string size = "";

        [Tooltip("生成张数（可选）：1 起；0 = 由服务端决定。>1 时用「下一张 / 上一张」切换")]
        public int imageCount = 1;

        [Tooltip("展项 ID（可选）：直连时留空——服务端按节点绑定裁决；仅 Hub 中控才必须逐次携带")]
        public string exhibitId = "";

        [Tooltip("轮询间隔（毫秒）：越小结果回得越快（尾部空转越少），但也越频繁；建议 500~1000")]
        public int pollIntervalMs = 700;

        [Header("场景引用")]
        [Tooltip("显示生成的目标图（RawImage）")]
        public RawImage ResultImage;

        [Tooltip("显示二维码（RawImage）；留空则只显示结果图")]
        public RawImage DisplayQRCode;

        [Tooltip("可选：状态回显用的 Text")]
        public Text StatusText;

        [Tooltip("当前显示第几张结果图（0 起）")]
        public int resultIndex;

        [Tooltip("Play 后自动查一次能力（免费）；生图会消耗配额，故不自动触发")]
        public bool autoRunOnStart = true;

        /// <summary>最近一次生图任务（切图 / 保存 / 刷新地址都基于它）。</summary>
        private AIJob lastJob;

        private void Awake()
        {
            TryConfigure();
        }

        private void Start()
        {
            if (autoRunOnStart)
            {
                CheckCapabilities();
            }
        }

        [ContextMenu("查询能力（Capabilities）")]
        public async void CheckCapabilities()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            try
            {
                AICapabilitiesResult caps = await AndX.AI.GetCapabilitiesAsync();
                if (!caps.Available)
                {
                    Debug.LogWarning($"[AndX.AI] 当前不可用：{caps.Reason}");
                    SetStatus($"AI 不可用：{caps.Reason}");
                    return;
                }
                foreach (var cap in caps.Capabilities)
                {
                    Debug.Log($"[AndX.AI] {cap.Capability} model={cap.Model} available={cap.Available}");
                }
                SetStatus($"可用能力 {caps.Capabilities.Count} 项");
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX.AI] 查询失败 code={e.Code} msg={e.Message}");
                SetStatus($"查询失败：{e.Code}");
            }
        }

        [ContextMenu("图生图并展示（换脸 → 结果图 + 出码）")]
        public async void GenerateAndShowQr()
        {
            if (!EnsureConfigured())
            {
                return;
            }

            var totalSw = Stopwatch.StartNew();
            try
            {
                // ① 读参考图（IO 并行）→ 缩放 + JPEG 编码（主线程）→ base64
                SetStatus("读取参考图…");
                var refSw = Stopwatch.StartNew();
                ReferencePick facePick = PickAsset(faceImagePath, faces, "人脸");
                ReferencePick templatePick = PickAsset(templateImagePath, templates, "模板");
                Task<byte[]> faceIo = facePick.Path == null
                    ? Task.FromResult<byte[]>(null)
                    : ReadStreamingAssetAsync(facePick.Path);
                Task<byte[]> templateIo = templatePick.Path == null
                    ? Task.FromResult<byte[]>(null)
                    : ReadStreamingAssetAsync(templatePick.Path);
                byte[][] raw = await Task.WhenAll(faceIo, templateIo);
                string face = EncodeReference(raw[0], facePick.Texture, facePick.Label);
                string template = EncodeReference(raw[1], templatePick.Texture, templatePick.Label);
                refSw.Stop();

                // ② 提交任务（两段式：先拿 jobNo，再轮询——这样才能分别计量「提交」与「等待」）
                SetStatus("提交生图任务…");
                var submitSw = Stopwatch.StartNew();
                AIJob job = await AndX.AI.CreateImageJobAsync(new AIImageRequest
                {
                    Prompt = prompt,
                    Size = string.IsNullOrEmpty(size) ? null : size,
                    Count = imageCount > 0 ? imageCount : (int?)null,
                    ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    Input = new Dictionary<string, object> { ["images"] = new[] { face, template } },
                });
                submitSw.Stop();

                // ③ 轮询到终态（间隔可调，越小尾部空转越少）
                SetStatus("生成中…（轮询等待）");
                var waitSw = Stopwatch.StartNew();
                var waitOptions = new AIWaitOptions
                {
                    Timeout = TimeSpan.FromMinutes(4),
                    Interval = TimeSpan.FromMilliseconds(Mathf.Max(200, pollIntervalMs)),
                };
                AIJob done = await AndX.AI.WaitForJobAsync(
                    job.JobNo,
                    waitOptions: waitOptions,
                    progress: new Progress<AIJob>(j => SetStatus($"AI {j.Status}…")));
                waitSw.Stop();
                lastJob = done;

                if (done.Status != AndXContract.AIJobStatuses.Succeeded)
                {
                    Debug.LogError($"[AndX.AI] 任务未成功 status={done.Status} code={done.ErrorCode} msg={done.ErrorMessage}");
                    SetStatus($"生图未成功：{done.Status} {done.ErrorCode}");
                    return;
                }

                // ④ 结果图与二维码**并行**：互不等待（二维码是独立出图接口，不占生成/取图时间）
                var showSw = Stopwatch.StartNew();
                Task<bool> showTask = ShowResultAsync(0);
                Task qrTask = ShowQrAsync(done);
                await Task.WhenAll(showTask, qrTask);
                showSw.Stop();

                totalSw.Stop();
                Debug.Log(
                    $"[AndX.AI] 完成 job={done.JobNo} 结果 {done.ResultCount} 张 mediaId={done.MediaId}\n"
                    + $"[AndX.AI] 耗时分解：读图+编码 {refSw.ElapsedMilliseconds}ms ｜ 提交 {submitSw.ElapsedMilliseconds}ms ｜ "
                    + $"生成等待 {waitSw.ElapsedMilliseconds}ms ｜ 结果图+二维码 {showSw.ElapsedMilliseconds}ms ｜ 端侧合计 {totalSw.ElapsedMilliseconds}ms");
                SetStatus($"完成：{done.ResultCount} 张（{waitSw.ElapsedMilliseconds / 1000.0:F1}s 生成，端侧合计 {totalSw.ElapsedMilliseconds / 1000.0:F1}s）");
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX.AI] 生图失败 code={e.Code} msg={e.Message}");
                SetStatus($"生图失败：{e.Code}");
            }
        }

        [ContextMenu("显示结果图（ShowResult）")]
        public async void ShowResult()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            await ShowResultAsync(resultIndex);
        }

        [ContextMenu("下一张结果图（NextResult）")]
        public async void NextResult()
        {
            if (!EnsureConfigured() || !RequireJob())
            {
                return;
            }
            var count = Math.Max(lastJob.ResultCount, 1);
            await ShowResultAsync((resultIndex + 1) % count);
        }

        [ContextMenu("上一张结果图（PrevResult）")]
        public async void PrevResult()
        {
            if (!EnsureConfigured() || !RequireJob())
            {
                return;
            }
            var count = Math.Max(lastJob.ResultCount, 1);
            await ShowResultAsync((resultIndex - 1 + count) % count);
        }

        [ContextMenu("保存结果图到本地（SaveResult）")]
        public async void SaveResult()
        {
            if (!EnsureConfigured() || !RequireJob())
            {
                return;
            }
            try
            {
                string path = await AIResultLoader.SaveAsync(lastJob, resultIndex);
                Debug.Log($"[AndX.AI] 结果图已保存：{path}");
                SetStatus($"已保存第 {resultIndex + 1} 张：{path}");
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX.AI] 保存失败 code={e.Code} msg={e.Message}");
                SetStatus($"保存失败：{e.Code}");
            }
        }

        /// <summary>显示第 index 张结果图：先用任务自带地址（快），失效则用 jobNo 刷新后重取。</summary>
        private async Task<bool> ShowResultAsync(int index)
        {
            if (lastJob == null || ResultImage == null)
            {
                if (lastJob != null)
                {
                    Debug.LogWarning("[AndX.AI] 未指定 ResultImage（挂一个 RawImage 即可显示目标图）");
                }
                return false;
            }
            try
            {
                Texture2D texture = await lastJob.LoadResultTextureAsync(index);
                if (texture == null)
                {
                    Debug.Log("[AndX.AI] 结果图地址可能已过期（presigned 10 分钟），改为重新查询任务后取图…");
                    texture = await AIResultLoader.LoadTextureAsync(lastJob.JobNo, index);
                }
                if (texture == null)
                {
                    SetStatus($"结果图显示失败（第 {index + 1} 张）");
                    return false;
                }
                ResultImage.texture = texture;
                resultIndex = index;
                Debug.Log($"[AndX.AI] 已显示第 {index + 1}/{Math.Max(lastJob.ResultCount, 1)} 张（{texture.width}x{texture.height}）");
                return true;
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX.AI] 取结果图失败 code={e.Code} msg={e.Message}");
                return false;
            }
        }

        /// <summary>出码并显示二维码（独立接口，可与结果图并行）。</summary>
        private async Task ShowQrAsync(AIJob job)
        {
            if (DisplayQRCode == null || job == null || string.IsNullOrEmpty(job.MediaId))
            {
                return;
            }
            try
            {
                IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
                DisplayQRCode.texture = await ticket.LoadQrTextureAsync();
                Debug.Log($"[AndX.AI] 已出码 token={ticket.Token} 链接={ticket.ShareUrl}");
            }
            catch (AndXException e)
            {
                Debug.LogWarning($"[AndX.AI] 出码失败 code={e.Code} msg={e.Message}");
            }
        }

        /// <summary>一次参考图选择结果：StreamingAssets 覆盖路径 或 自带纹理 + 可读标签（用于日志/溯源）</summary>
        private readonly struct ReferencePick
        {
            public ReferencePick(string path, Texture2D texture, string label)
            {
                Path = path;
                Texture = texture;
                Label = label;
            }

            /// <summary>StreamingAssets 相对路径（现场换图时用）；为空表示用自带纹理</summary>
            public string Path { get; }

            /// <summary>示例自带纹理</summary>
            public Texture2D Texture { get; }

            /// <summary>可读标签，如「人脸#2」「模板(StreamingAssets)」</summary>
            public string Label { get; }
        }

        /// <summary>
        /// 选一组参考图：填了 StreamingAssets 相对路径就用文件（现场换图，优先级最高）；
        /// 否则从自带候选里**随机**挑一张（<see cref="randomPairPerRun"/> 关闭时固定第 1 张）。
        /// 支持 N 张人脸 × M 张模板的随机组合。
        /// </summary>
        private ReferencePick PickAsset(string overridePath, Texture2D[] pool, string label)
        {
            if (!string.IsNullOrEmpty(overridePath))
            {
                return new ReferencePick(overridePath, null, label + "(StreamingAssets)");
            }
            if (pool == null || pool.Length == 0)
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    $"参考图未配置：请在 Inspector 给「{label}」数组挂至少一张图（或填 StreamingAssets 相对路径）");
            }
            var index = randomPairPerRun && pool.Length > 1 ? UnityEngine.Random.Range(0, pool.Length) : 0;
            return new ReferencePick(null, pool[index], $"{label}#{index + 1}");
        }

        /// <summary>读 StreamingAssets 文件（WebGL 走 UnityWebRequest，其余平台走文件 IO）。</summary>
        private static async Task<byte[]> ReadStreamingAssetAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "参考图路径不能为空");
            }
            var full = Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_WEBGL && !UNITY_EDITOR
            using (var request = UnityWebRequest.Get(full))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
#if UNITY_2020_2_OR_NEWER
                if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    throw new AndXException(AndXContract.SdkErrorCodes.Configuration, $"参考图读取失败：{relativePath}（{request.error}）");
                }
                return request.downloadHandler.data;
            }
#else
            if (!File.Exists(full))
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    $"参考图不存在：{full}（把「人脸.png」「模板.png」放到 Assets/StreamingAssets/ 下，或改示例的路径字段）");
            }
            return await Task.Run(() => File.ReadAllBytes(full));
#endif
        }

        /// <summary>
        /// 缩放（长边 ≤ referenceMaxEdge）→ JPEG → **带 data: 前缀的 base64**（主线程：纹理操作）。
        ///
        /// 来源二选一：给了 StreamingAssets 相对路径就用文件（现场可换图），否则用示例自带的 <see cref="Texture2D"/>。
        /// 服务端同时兼容「裸 base64」，但带上前缀更规范（也便于将来直传厂商）。
        /// </summary>
        private string EncodeReference(byte[] raw, Texture2D builtIn, string label)
        {
            Texture2D source;
            string sizeText;
            if (raw != null && raw.Length > 0)
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(raw))
                {
                    throw new AndXException(AndXContract.SdkErrorCodes.Configuration, $"参考图解码失败：{label}");
                }
                sizeText = $"{raw.Length / 1024}KB";
            }
            else
            {
                if (builtIn == null)
                {
                    throw new AndXException(
                        AndXContract.SdkErrorCodes.Configuration,
                        $"参考图未配置：请挂「{label}」纹理，或在 Inspector 填 StreamingAssets 相对路径");
                }
                source = builtIn;
                sizeText = "示例自带";
            }
            var scaled = Downscale(source, Mathf.Max(256, referenceMaxEdge));
            var jpeg = scaled.EncodeToJPG(Mathf.Clamp(referenceJpegQuality, 1, 100));
            Debug.Log($"[AndX.AI] 参考图 {label}：{source.width}x{source.height}（{sizeText}）→ "
                + $"{scaled.width}x{scaled.height} JPEG {jpeg.Length / 1024}KB（base64 后约 {jpeg.Length * 4 / 3 / 1024}KB）");
            return "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
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

        private bool RequireJob()
        {
            if (lastJob != null)
            {
                return true;
            }
            Debug.LogWarning("[AndX.AI] 还没有结果：请先点「图生图并展示」");
            SetStatus("请先点「图生图并展示」");
            return false;
        }

        private void SetStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.text = message;
            }
        }

        /// <summary>接入配置（示例里只此一处）：按 <see cref="accessMode"/> 解析基址与凭据。</summary>
        private bool TryConfigure()
        {
            try
            {
                AndX.Config.InitAuto(new AndXOptions
                {
                    AccessMode = accessMode,
                    Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint,
                    ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    EdgeKey = EdgeKey.FromEnvironment(),
                });
                Debug.Log($"[AndX] 接入方式 = {AndX.Config.Describe()}");
                return true;
            }
            catch (AndXException e)
            {
                Debug.LogError($"[AndX] 接入配置失败 code={e.Code} msg={e.Message}");
                return false;
            }
        }

        private bool EnsureConfigured()
        {
            if (AndX.Config.IsConfigured)
            {
                return true;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AndX] 请在 Play 模式下运行示例：SDK 的 Unity 传输在运行时注册。");
                return false;
            }
            return TryConfigure();
        }

        private void OnDestroy()
        {
            AndX.Config.Reset();
        }
    }
}