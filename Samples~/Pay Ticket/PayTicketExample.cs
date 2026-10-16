using AndX;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace AndX.Samples
{
    /// <summary>
    /// 示例 4（Pay Ticket）：展项付费票据签发与订单状态查询。
    /// 金额一律由服务端返回，端侧不参与定价；/p/ 下单链路打通前，
    /// 签发阶段 <see cref="PayTicket.OrderNo"/> 通常为 null（见 implementation.md §4.4）。
    /// 场景结构：Canvas &gt; RawImage（出码）+ 两个 Button（onClick 绑定
    /// <see cref="CreateTicket"/> / <see cref="QueryOrder"/>）+ Text（状态）。
    /// 接入方式见 usage.md §15（默认直连 AndX 云服务，可切经本机 AndXEdge）：
    /// 直连时展项由**节点绑定**裁决（写死展项=客户端冒充展项，服务端会拒绝）；仅 Hub 中控才逐次携带。
    /// </summary>
    public class PayTicketExample : MonoBehaviour
    {
        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据（每机一把、可单独吊销），服务端审计到「节点 + 展项」\n"
            + "Edge=经本机 AndXEdge：鉴权 / 展项标识 / 公网地址由 Edge 代持（展项无外网、多展项共用出口时用）\n"
            + "Auto=本机有节点凭据则直连，否则走 Edge")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：Direct=后端基址（留空读环境变量 ANDX_ENDPOINT）；Edge=本机 Edge 基址（留空用 http://127.0.0.1:6699）")]
        public string endpoint = "";

        [Header("示例参数")]
        [Tooltip("展项 ID（可选）：展项机留空——由服务端按节点绑定裁决；只有 role=hub 的中控才必须逐次携带")]
        public string exhibitId = "";

        [Tooltip("可选：显示二维码的 RawImage")]
        public RawImage DisplayQRCode;

        [Tooltip("下单后回填的订单号（签发阶段通常为 null）")]
        public string orderNo = "";

        [Tooltip("可选：状态回显用的 Text")]
        public Text StatusText;

        [Tooltip("Play 后自动跑一次主流程（示例默认开启：导入即可看到付费码）；关掉后只等按钮 / 齿轮菜单触发")]
        public bool autoRunOnStart = true;

        private void Awake()
        {
            // 统一入口：按 accessMode 解析基址与凭据；结果（含为什么这么选）打到 Console，不含任何密钥
            TryConfigure();
        }

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
                // 未指定展项且服务端无兜底：CONFIGURATION；展项不存在：EXHIBIT_NOT_FOUND；
                // 节点无权代表该展项（Hub 不在授权集合 / 展项机带了别的展项）：NODE_EXHIBIT_FORBIDDEN
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