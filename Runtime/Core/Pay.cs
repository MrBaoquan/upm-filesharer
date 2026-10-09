using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>
    /// 扫码付费能力（静态入口）：签发展项付费票据、查询订单状态。
    /// 展项付费的 /p/ 下单链路服务端尚未打通（见 implementation.md §4.4），
    /// 因此签发阶段 <see cref="PayTicket.OrderNo"/> 通常为 null。
    /// 金额一律由服务端返回，端侧不参与定价。
    /// </summary>
    public static class Pay
    {
        /// <summary>签发展项付费票据（Edge 控制面）。</summary>
        public static async Task<PayTicket> CreateTicketAsync(PayTicketOptions options = null, CancellationToken cancellationToken = default)
        {
            options = options ?? new PayTicketOptions();
            var exhibitId = Config.ResolveExhibitId(options.ExhibitId);
            if (string.IsNullOrEmpty(exhibitId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "PayTicketOptions.ExhibitId 不能为空");
            }
            var api = Config.Api;
            var data = await api.PostRawAsync(
                AndXContract.Paths.EdgeTickets,
                new IssueTicketRequest { Purpose = AndXContract.Purposes.ExhibitPay, ExhibitId = exhibitId },
                cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<IssueTicketResponse>(AndXJson.Serializer);
            return new PayTicket
            {
                Token = r.Token,
                Purpose = r.Purpose,
                ShareUrl = r.ShareUrl,
                Amount = r.Amount,
                ExpireAt = r.ExpireAt,
                QrImageUrl = string.IsNullOrEmpty(r.QrcodePath) ? null : api.ResolveUrl(r.QrcodePath),
                OrderNo = null,
            };
        }

        /// <summary>查询订单状态（PENDING → PAID）。</summary>
        public static async Task<OrderStatusInfo> QueryOrderAsync(string orderNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(orderNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "orderNo 不能为空");
            }
            var api = Config.Api;
            var data = await api.GetRawAsync(AndXContract.Paths.PayOrders + "/" + orderNo, cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<OrderStatusResponse>(AndXJson.Serializer);
            return new OrderStatusInfo
            {
                OrderNo = r.OrderNo,
                Status = r.Status,
                Amount = r.Amount,
                PayTime = r.PayTime,
            };
        }

        /// <summary>
        /// 付费二维码 PNG 绝对地址（服务端出图）。
        /// 与资源共用公开出图端点（对 EXHIBIT_PAY 同样渲染 {BASE}/p/{token}），无需 edge key 即可显示。
        /// </summary>
        public static string QrImageUrl(string token)
        {
            return Config.Api.ResolveUrl(AndXContract.Paths.ResourceQrcode(token));
        }

        /// <summary>下载付费二维码 PNG 原始字节。</summary>
        public static Task<byte[]> GetQrPngAsync(string token, CancellationToken cancellationToken = default)
        {
            return Config.Api.GetBytesAsync(AndXContract.Paths.ResourceQrcode(token), cancellationToken);
        }
    }
}
