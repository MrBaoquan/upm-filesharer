using System;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace AndX.Samples
{
    /// <summary>
    /// 示例 3（AI Image）：AI 生图 → **按需取用产物**。
    /// 端侧只传提示词；AI 供应商密钥只在服务端聚合网关，端侧不接触、不参与定价。
    ///
    /// 同一份产物支持三条互不排斥的用法（本示例都有入口）：
    /// 1. **屏幕上直接显示目标图** → <c>ResultImage</c>（`job.LoadResultTextureAsync()`）；
    /// 2. **出码给观众扫码带走** → <c>DisplayQRCode</c>（`Share.IssueResourceTicketAsync(job.MediaId)`）；
    /// 3. **保存原图到本地**（留存 / 打印 / 二次加工）→ `AIResultLoader.SaveAsync()`。
    ///
    /// 场景结构：Canvas &gt; RawImage(结果图) + RawImage(二维码) + 两个 Button
    /// （onClick 绑定 <see cref="GenerateAndShowQr"/> / <see cref="CheckCapabilities"/>）+ Text(状态)。
    /// 另有齿轮菜单：显示结果图 / 上一张 / 下一张 / 保存结果图。
    ///
    /// 接入方式见 usage.md §15（默认直连 AndX 云服务，可切经本机 AndXEdge）。
    /// 零配置：Play 后自动查询能力（免费、不消耗配额）；生图会真实消耗服务端配额，故保留为显式点击。
    /// </summary>
    public class AiImageExample : MonoBehaviour
    {
        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据（每机一把、可单独吊销），服务端审计到「节点 + 展项」\n"
            + "Edge=经本机 AndXEdge：鉴权 / 展项标识 / 公网地址由 Edge 代持（展项无外网、多展项共用出口时用）\n"
            + "Auto=本机有节点凭据则直连，否则走 Edge")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：留空 = 用默认云服务基址（https://andx.andcrane.com）；也可用环境变量 ANDX_ENDPOINT\n"
            + "（本机联调填 http://127.0.0.1 指向 dev 栈；经 Edge 模式留空则用 127.0.0.1:6699）")]
        public string endpoint = "";

        [Header("示例参数")]
        [Tooltip("提示词")]
        public string prompt = "赛博朋克城市夜景，霓虹反光，超广角";

        [Tooltip("尺寸（可选），如 1024x1024；留空由服务端 / 模型决定")]
        public string size = "";

        [Tooltip("生成张数（可选）：1 起；0 = 由服务端 / 模型决定。>1 时用「下一张 / 上一张」切换显示")]
        public int imageCount = 1;

        [Tooltip("展项 ID（可选）：直连时留空——服务端按节点绑定裁决；仅 Hub 中控才必须逐次携带")]
        public string exhibitId = "";

        [Header("场景引用")]
        [Tooltip("显示生成的目标图（RawImage）；留空则只出码")]
        public RawImage ResultImage;

        [Tooltip("显示二维码（RawImage）；留空则只显示结果图")]
        public RawImage DisplayQRCode;

        [Tooltip("可选：状态回显用的 Text")]
        public Text StatusText;

        [Tooltip("当前显示第几张结果图（0 起；Inspector 里改完点齿轮菜单「显示结果图」即可切换）")]
        public int resultIndex;

        [Tooltip("Play 后自动查一次能力（免费）；生图会消耗配额，故不自动触发")]
        public bool autoRunOnStart = true;

        /// <summary>最近一次生图任务（保存 / 切图 / 刷新地址都基于它）。</summary>
        private AIJob lastJob;

        private void Awake()
        {
            // 统一入口：按 accessMode 解析基址与凭据；结果（含为什么这么选）打到 Console，不含任何密钥
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
                    // 聚合网关未配置时不抛错，只回显原因
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

        [ContextMenu("生图并展示（Generate → 结果图 + 出码）")]
        public async void GenerateAndShowQr()
        {
            if (!EnsureConfigured())
            {
                return;
            }

            try
            {
                SetStatus("生图中…（提交后轮询到终态）");
                // 一步生图：提交 + 轮询等待；到终态返回结果，超时抛 TIMEOUT、取消抛 CANCELED
                // 进度用 System.Progress<T> 包一层（IProgress<T> 是接口，不能直接传 lambda）
                AIJob job = await AndX.AI.GenerateImageAsync(
                    new AIImageRequest
                    {
                        Prompt = prompt,
                        Size = string.IsNullOrEmpty(size) ? null : size,
                        Count = imageCount > 0 ? imageCount : (int?)null,
                        ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    },
                    progress: new Progress<AIJob>(j =>
                    {
                        Debug.Log($"[AndX.AI] {j.Status}");
                        SetStatus($"AI {j.Status}");
                    }));

                lastJob = job;
                if (job.Status != AndXContract.AIJobStatuses.Succeeded)
                {
                    Debug.LogError($"[AndX.AI] 任务未成功 status={job.Status} code={job.ErrorCode} msg={job.ErrorMessage}");
                    SetStatus($"生图未成功：{job.Status} {job.ErrorCode}");
                    return;
                }

                var mediaIds = job.MediaIds == null ? "-" : string.Join(",", job.MediaIds);
                var resultUrls = job.ResultUrls == null ? "-" : string.Join(" | ", job.ResultUrls);
                Debug.Log($"[AndX.AI] job={job.JobNo} 结果 {job.ResultCount} 张 mediaIds=[{mediaIds}]");
                Debug.Log($"[AndX.AI] 结果图地址（presigned 10 分钟）={resultUrls}");

                // ① 屏幕显示目标图（不需要二维码的现场只用这一步即可）
                var shown = await ShowResultAsync(0);

                // ② 出码：产物 MediaId 直接复用分享链路（扫码即可预览 / 下载）
                string tokenText = "-";
                if (DisplayQRCode != null)
                {
                    IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
                    tokenText = ticket.Token;
                    Debug.Log($"[AndX.AI] mediaId={job.MediaId} token={ticket.Token} 定价(分)={ticket.Amount} 链接={ticket.ShareUrl}");
                    DisplayQRCode.texture = await ticket.LoadQrTextureAsync();
                }

                SetStatus($"结果图 {job.ResultCount} 张（当前 {resultIndex + 1}）{(shown ? "" : "·显示失败")}，token={tokenText}");
            }
            catch (AndXException e)
            {
                // AI_DISABLED / AI_CAPABILITY_UNSUPPORTED / AI_JOB_NOT_FOUND / AI_PROVIDER_ERROR
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
            if (!EnsureConfigured())
            {
                return;
            }
            if (lastJob == null)
            {
                Debug.LogWarning("[AndX.AI] 还没有结果：请先点「生图并展示」");
                SetStatus("请先点「生图并展示」");
                return;
            }
            var count = Math.Max(lastJob.ResultCount, 1);
            await ShowResultAsync((resultIndex + 1) % count);
        }

        [ContextMenu("上一张结果图（PrevResult）")]
        public async void PrevResult()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            if (lastJob == null)
            {
                Debug.LogWarning("[AndX.AI] 还没有结果：请先点「生图并展示」");
                SetStatus("请先点「生图并展示」");
                return;
            }
            var count = Math.Max(lastJob.ResultCount, 1);
            await ShowResultAsync((resultIndex - 1 + count) % count);
        }

        [ContextMenu("保存结果图到本地（SaveResult）")]
        public async void SaveResult()
        {
            if (!EnsureConfigured())
            {
                return;
            }
            if (lastJob == null)
            {
                Debug.LogWarning("[AndX.AI] 还没有结果：请先点「生图并展示」");
                SetStatus("请先点「生图并展示」");
                return;
            }

            try
            {
                // 默认落在 persistentDataPath/andx/ai/{jobNo}-{index}.png；也可传 directory/fileName 自定义
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

        /// <summary>
        /// 显示第 index 张结果图：先用任务对象里的地址（快路径），失败再用 jobNo 刷新后重试
        /// ——结果图是 **presigned（10 分钟）**，放一会儿再点就会走刷新这条路。
        /// </summary>
        private async Task<bool> ShowResultAsync(int index)
        {
            if (lastJob == null)
            {
                return false;
            }
            if (ResultImage == null)
            {
                Debug.LogWarning("[AndX.AI] 未指定 ResultImage（Inspector 里挂一个 RawImage 即可显示目标图）");
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
                SetStatus($"取结果图失败：{e.Code}");
                return false;
            }
        }

        /// <summary>状态回显：场景里挂了 Status Text 时才有输出。</summary>
        private void SetStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.text = message;
            }
        }

        /// <summary>
        /// 接入配置（示例里只此一处）：按 <see cref="accessMode"/> 解析基址与凭据。
        /// 直连的凭据来自本机凭据文件（<c>persistentDataPath/andx/node-credential.json</c>，由
        /// <c>AndX.Node.EnrollAsync</c> 首启写入），**不写在代码里**。
        /// </summary>
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
            // 场景切换 / 退出时清空配置（多场景复用与测试）
            AndX.Config.Reset();
        }
    }
}