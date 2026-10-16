using System;
using AndX;
using AndX.Core;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 示例 3：AI 生图 → 出码。
/// 端侧只传提示词；AI 供应商密钥只在服务端聚合网关，端侧不接触、不参与定价。
/// 成功后 <see cref="AIJob.MediaId"/> 直接复用分享 / 二维码 / 下载 / 付费链路。
/// 场景结构：Canvas &gt; RawImage（出码）+ 两个 Button（onClick 绑定
/// <see cref="GenerateAndShowQr"/> / <see cref="CheckCapabilities"/>）+ Text（状态）。
/// 零配置：Play 后自动查询能力（免费、不消耗配额，能立刻看出聚合网关是否可用）；
/// 生图会真实消耗服务端配额，因此保留为显式点击。
/// </summary>
public class AiImageExample : MonoBehaviour
{
    [Tooltip("提示词")]
    public string prompt = "赛博朋克城市夜景，霓虹反光，超广角";

    [Tooltip("尺寸（可选），如 1024x1024；留空由服务端 / 模型决定")]
    public string size = "";

    [Tooltip("可选：显示二维码的 RawImage")]
    public RawImage DisplayQRCode;

    [Tooltip("可选：状态回显用的 Text")]
    public Text StatusText;

    [Tooltip("Play 后自动查一次能力（免费）；生图会消耗配额，故不自动触发")]
    public bool autoRunOnStart = true;

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

    [ContextMenu("生图并出码（Generate → QR）")]
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
                new AIImageRequest { Prompt = prompt, Size = string.IsNullOrEmpty(size) ? null : size },
                progress: new Progress<AIJob>(j =>
                {
                    Debug.Log($"[AndX.AI] {j.Status}");
                    SetStatus($"AI {j.Status}");
                }));

            if (job.Status != AndXContract.AIJobStatuses.Succeeded)
            {
                Debug.LogError($"[AndX.AI] 任务未成功 status={job.Status} code={job.ErrorCode} msg={job.ErrorMessage}");
                SetStatus($"生图未成功：{job.Status} {job.ErrorCode}");
                return;
            }

            // 产物直接复用分享链路：mediaId → 票据 → 二维码（扫码即可预览 / 下载）
            IssuedTicket ticket = await AndX.Share.IssueResourceTicketAsync(job.MediaId);
            Debug.Log($"[AndX.AI] mediaId={job.MediaId} token={ticket.Token} 定价(分)={ticket.Amount} 链接={ticket.ShareUrl}");
            SetStatus($"已出码 token={ticket.Token} 定价(分)={ticket.Amount}");

            if (DisplayQRCode != null)
            {
                DisplayQRCode.texture = await ticket.LoadQrTextureAsync();
            }
        }
        catch (AndXException e)
        {
            // AI_DISABLED / AI_CAPABILITY_UNSUPPORTED / AI_JOB_NOT_FOUND / AI_PROVIDER_ERROR
            Debug.LogError($"[AndX.AI] 生图失败 code={e.Code} msg={e.Message}");
            SetStatus($"生图失败：{e.Code}");
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
        AndX.Config.InitLocal();
        return true;
    }
}
