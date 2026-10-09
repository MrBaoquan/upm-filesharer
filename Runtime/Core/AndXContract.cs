namespace AndX.Core
{
    /// <summary>
    /// AndX 契约常量（镜像 <c>andx-sdk-spec</c>，只读 SSOT）。
    /// 任何取值变更必须先改契约包，再同步此处；一致性由 <c>ContractParityTests</c> 守护。
    /// </summary>
    public static class AndXContract
    {
        /// <summary>契约版本，对应 spec <c>ANDX_CONTRACT_VERSION</c>。1.1 新增 AI 能力域（向后兼容）。</summary>
        public const string Version = "1.1";

        /// <summary>端侧默认值（与 AndXEdge 约定一致；变更须先改契约包）。</summary>
        public static class Defaults
        {
            /// <summary>本机 AndXEdge 默认端口（插件与网关共用）。</summary>
            public const int LocalEdgePort = 6699;

            /// <summary>本机 AndXEdge 默认基址（环回，明文；仅本机可达）。</summary>
            public const string LocalEdgeEndpoint = "http://127.0.0.1:6699";
        }

        /// <summary>路径前缀与接口路径（均不含站点基址）。</summary>
        public static class Paths
        {
            /// <summary>资源类普通链接二维码路径前缀。</summary>
            public const string Share = "/r";

            /// <summary>扫码付费普通链接二维码路径前缀。</summary>
            public const string Pay = "/p";

            public const string Scan = "/api/scan";
            public const string Resources = "/api/resources";
            public const string EdgeTickets = "/api/edge/tickets";
            public const string EdgeUploads = "/api/edge/uploads";
            public const string EdgeResources = "/api/edge/resources";
            public const string PayOrders = "/api/pay/orders";
            public const string ResourceOrders = "/api/pay/resource-orders";

            /// <summary>AI 能力清单接口（经 Edge 转发，X-Edge-Key）。</summary>
            public const string AICapabilitiesPath = "/api/ai/capabilities";

            /// <summary>AI 任务提交接口（经 Edge 转发，X-Edge-Key）。</summary>
            public const string AIJobsPath = "/api/ai/jobs";

            /// <summary>AI 任务详情路径：/api/ai/jobs/{jobNo}</summary>
            public static string AIJobPath(string jobNo)
            {
                return AIJobsPath + "/" + jobNo;
            }

            /// <summary>AI 任务取消路径：/api/ai/jobs/{jobNo}/cancel</summary>
            public static string AIJobCancelPath(string jobNo)
            {
                return AIJobsPath + "/" + jobNo + "/cancel";
            }

            /// <summary>资源二维码 PNG 路径（相对）：/api/resources/{token}/qrcode.png</summary>
            public static string ResourceQrcode(string token)
            {
                return Resources + "/" + token + "/qrcode.png";
            }

            /// <summary>资源下载授权路径（相对）。</summary>
            public static string ResourceDownload(string token)
            {
                return Resources + "/" + token + "/download";
            }
        }

        /// <summary>票据用途。</summary>
        public static class Purposes
        {
            public const string ResourceDownload = "RESOURCE_DOWNLOAD";
            public const string ExhibitPay = "EXHIBIT_PAY";
        }

        /// <summary>AI 能力标识（镜像 spec <c>src/ai.ts</c> 的 <c>AICapability</c>）。</summary>
        public static class AICapabilities
        {
            public const string ImageGenerate = "ai.image.generate";
            public const string VideoGenerate = "ai.video.generate";
        }

        /// <summary>AI 任务状态机（镜像 spec <c>AIJobStatus</c>）。</summary>
        public static class AIJobStatuses
        {
            public const string Pending = "PENDING";
            public const string Running = "RUNNING";
            public const string Succeeded = "SUCCEEDED";
            public const string Failed = "FAILED";
            public const string Canceled = "CANCELED";

            /// <summary>是否终态（不再变化）。</summary>
            public static bool IsTerminal(string status)
            {
                return status == Succeeded || status == Failed || status == Canceled;
            }
        }

        /// <summary>订单业务类型。</summary>
        public static class OrderBizTypes
        {
            public const string ExhibitInteraction = "EXHIBIT_INTERACTION";
            public const string ResourceDownload = "RESOURCE_DOWNLOAD";
        }

        /// <summary>订单状态。</summary>
        public static class OrderStatuses
        {
            public const string Pending = "PENDING";
            public const string Paid = "PAID";
            public const string Closed = "CLOSED";
            public const string Refunding = "REFUNDING";
            public const string Refunded = "REFUNDED";
        }

        /// <summary>请求头。</summary>
        public static class Headers
        {
            public const string Authorization = "Authorization";
            public const string EdgeKey = "x-edge-key";
            public const string DeviceId = "x-device-id";
            public const string DeviceTimestamp = "x-device-timestamp";
            public const string DeviceNonce = "x-device-nonce";
            public const string DeviceSignature = "x-device-signature";
        }

        /// <summary>服务端业务错误码（镜像 spec src/errors.ts 与 server/src/common/error-codes.ts）。</summary>
        public static class ErrorCodes
        {
            public const string InternalError = "INTERNAL_ERROR";
            public const string NotFound = "NOT_FOUND";
            public const string ValidationFailed = "VALIDATION_FAILED";
            public const string Unauthorized = "UNAUTHORIZED";
            public const string Forbidden = "FORBIDDEN";

            public const string TenantRequired = "TENANT_REQUIRED";
            public const string TenantNotFound = "TENANT_NOT_FOUND";

            public const string ExhibitNotFound = "EXHIBIT_NOT_FOUND";
            public const string ExhibitBusy = "EXHIBIT_BUSY";

            public const string PaySessionInvalid = "PAY_SESSION_INVALID";
            public const string PayOrderExists = "PAY_ORDER_EXISTS";
            public const string PayNotifyVerifyFailed = "PAY_NOTIFY_VERIFY_FAILED";
            public const string PayRefundFailed = "PAY_REFUND_FAILED";

            public const string MediaNotFound = "MEDIA_NOT_FOUND";

            public const string TicketNotFound = "TICKET_NOT_FOUND";
            public const string TicketExpired = "TICKET_EXPIRED";
            public const string TicketRevoked = "TICKET_REVOKED";
            public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
            public const string EntitlementRequired = "ENTITLEMENT_REQUIRED";

            public const string EdgeUnauthorized = "EDGE_UNAUTHORIZED";
            public const string EdgeDisabled = "EDGE_DISABLED";
            public const string EdgeUploadNotFound = "EDGE_UPLOAD_NOT_FOUND";
            public const string EdgeUploadIncomplete = "EDGE_UPLOAD_INCOMPLETE";

            public const string DeviceUnauthorized = "DEVICE_UNAUTHORIZED";
            public const string DeviceNotFound = "DEVICE_NOT_FOUND";
            public const string DeviceDisabled = "DEVICE_DISABLED";
            public const string DeviceReplay = "DEVICE_REPLAY";
            public const string DeviceSecretMissing = "DEVICE_SECRET_MISSING";

            public const string PaySessionExpired = "PAY_SESSION_EXPIRED";
            public const string PaySessionNotPayable = "PAY_SESSION_NOT_PAYABLE";

            public const string AIDisabled = "AI_DISABLED";
            public const string AICapabilityUnsupported = "AI_CAPABILITY_UNSUPPORTED";
            public const string AIJobNotFound = "AI_JOB_NOT_FOUND";
            public const string AIProviderError = "AI_PROVIDER_ERROR";
        }

        /// <summary>SDK 侧错误码（网络 / 超时 / 取消 / 配置，非服务端返回）。</summary>
        public static class SdkErrorCodes
        {
            public const string Network = "NETWORK";
            public const string Timeout = "TIMEOUT";
            public const string Canceled = "CANCELED";
            public const string Configuration = "CONFIGURATION";
        }
    }
}
