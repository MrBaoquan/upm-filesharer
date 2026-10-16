using AndX;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 示例 4：展项付费票据签发与订单状态查询。
/// 金额一律由服务端返回，端侧不参与定价；/p/ 下单链路打通前，
/// 签发阶段 <see cref="PayTicket.OrderNo"/> 通常为 null（见 implementation.md §4.4）。
/// 场景结构：Canvas &gt; RawImage（出码）+ 两个 Button（onClick 绑定
/// <see cref="CreateTicket"/> / <see cref="QueryOrder"/>）+ Text（状态）。
/// 零配置：展项 ID 留空即用 Edge / 服务端默认展项，导入后直接 Play 即出付费码。
/// </summary>
public class PayTicketExample : MonoBehaviour
{
    [Tooltip("展项 ID（可选）：本机 Edge 已绑定展项时留空，示例即零配置可跑")]
    public string exhibitId = "";

    [Tooltip("可选：显示二维码的 RawImage")]
    public RawImage DisplayQRCode;

    [Tooltip("下单后回填的订单号（签发阶段通常为 null）")]
    public string orderNo = "";

    [Tooltip("可选：状态回显用的 Text")]
    public Text StatusText;

    [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到付费码）；关掉后只等按钮 / 齿轮菜单触发")]
    public bool autoRunOnStart = true;

    private void Start()
    {
        if (autoRunOnStart)
        {
            CreateTicket();
        }
    }

    [ContextMenu("签发票据（CreateTicket）")]
    public async void CreateTicket()
    {
        if (!EnsureConfigured())
        {
            return;
        }

        try
        {
            PayTicket ticket = await AndX.Pay.CreateTicketAsync(new PayTicketOptions
            {
                ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
            });
            orderNo = ticket.OrderNo;
            Debug.Log($"[AndX.Pay] token={ticket.Token} 定价(分)={ticket.Amount} 订单={ticket.OrderNo} 链接={ticket.ShareUrl}");
            SetStatus($"已签票 token={ticket.Token} 定价(分)={ticket.Amount} 订单={ticket.OrderNo ?? "-"}");

            if (DisplayQRCode != null)
            {
                // 与资源共用公开出图端点：对 EXHIBIT_PAY 渲染 {BASE}/p/{token}
                DisplayQRCode.texture = await ticket.LoadQrTextureAsync();
            }
        }
        catch (AndXException e)
        {
            // 未指定展项且 Edge / 服务端无兜底：CONFIGURATION；展项不存在：EXHIBIT_NOT_FOUND
            Debug.LogError($"[AndX.Pay] 签票失败 code={e.Code} msg={e.Message}");
            SetStatus($"签票失败：{e.Code}");
        }
    }

    [ContextMenu("查询订单（QueryOrder）")]
    public async void QueryOrder()
    {
        if (!EnsureConfigured())
        {
            return;
        }
        if (string.IsNullOrEmpty(orderNo))
        {
            Debug.LogWarning("[AndX.Pay] 请先签票并回填 orderNo（/p/ 下单链路打通后才会返回订单号）");
            SetStatus("还没有订单号：请先点「签发票据」");
            return;
        }

        try
        {
            OrderStatusInfo status = await AndX.Pay.QueryOrderAsync(orderNo);
            Debug.Log($"[AndX.Pay] 订单={status.OrderNo} 状态={status.Status} 金额(分)={status.Amount} 支付时间={status.PayTime}");
            SetStatus($"订单={status.OrderNo} 状态={status.Status} 金额(分)={status.Amount}");
        }
        catch (AndXException e)
        {
            Debug.LogError($"[AndX.Pay] 查询失败 code={e.Code} msg={e.Message}");
            SetStatus($"查询失败：{e.Code}");
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
