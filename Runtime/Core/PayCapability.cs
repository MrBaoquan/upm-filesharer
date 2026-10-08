using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX.Pay
{
    /// <summary>
    /// 扫码付费能力域：签发展项付费票据、查询订单状态。
    /// 展项付费的 /p/ 下单链路服务端尚未打通（见 implementation.md §4.4），
    /// 因此签发阶段 <see cref="PayTicket.OrderNo"/> 通常为 null。
    /// </summary>
    public sealed class PayCapability : IAndXCapability
    {
        private readonly AndXApiClient _api;

        internal PayCapability(AndXApiClient api)
        {
            _api = api;
        }

        /// <summary>签发展项付费票据（Edge 控制面）。</summary>
        public async Task<PayTicket> CreateTicketAsync(PayTicketOptions options = null, CancellationToken cancellationToken = default)
        {
            options = options ?? new PayTicketOptions();
            if (string.IsNullOrEmpty(options.ExhibitId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "PayTicketOptions.ExhibitId 不能为空");
            }
            var data = await _api.PostRawAsync(
                AndXContract.Paths.EdgeTickets,
                new IssueTicketRequest { Purpose = AndXContract.Purposes.ExhibitPay, ExhibitId = options.ExhibitId },
                cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<IssueTicketResponse>(AndXJson.Serializer);
            return new PayTicket
            {
                Token = r.Token,
                Purpose = r.Purpose,
                ShareUrl = r.ShareUrl,
                Amount = r.Amount,
                ExpireAt = r.ExpireAt,
                QrImageUrl = string.IsNullOrEmpty(r.QrcodePath) ? null : _api.ResolveUrl(r.QrcodePath),
                OrderNo = null,
            };
        }

        /// <summary>查询订单状态（PENDING → PAID）。</summary>
        public async Task<OrderStatusInfo> QueryOrderAsync(string orderNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(orderNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "orderNo 不能为空");
            }
            var data = await _api.GetRawAsync(AndXContract.Paths.PayOrders + "/" + orderNo, cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<OrderStatusResponse>(AndXJson.Serializer);
            return new OrderStatusInfo
            {
                OrderNo = r.OrderNo,
                Status = r.Status,
                Amount = r.Amount,
                PayTime = r.PayTime,
            };
        }

        /// <summary>二维码 PNG 绝对地址（服务端出图）。</summary>
        public string QrImageUrl(string token)
        {
            return _api.ResolveUrl(AndXContract.Paths.ResourceQrcode(token));
        }

        /// <summary>下载二维码 PNG 原始字节。</summary>
        public Task<byte[]> GetQrPngAsync(string token, CancellationToken cancellationToken = default)
        {
            return _api.GetBytesAsync(AndXContract.Paths.ResourceQrcode(token), cancellationToken);
        }
    }
}
