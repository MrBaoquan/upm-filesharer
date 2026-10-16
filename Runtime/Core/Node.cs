using System;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>
    /// 节点身份（solution §16.2 / §16.3）：首启**自助注册**，换取本机唯一的节点密钥。
    ///
    /// 直连部署下插件持本机节点凭据（每节点一把、可单独吊销与轮换），服务端据此判定
    /// 租户/展项并审计到展项；经 AndXEdge 部署（Local/Relay/Hub）时**不需要**本类。
    ///
    /// 典型用法（首启一次）：
    /// <code>
    /// var store = AndX.Unity.NodeCredentialStorage.Create(/* 平台保护器，见 usage.md */);
    /// var credential = store.Load() ?? await AndX.Node.EnrollAsync(endpoint, token, nodeId, store: store);
    /// AndX.Config.Init(new AndXOptions { Endpoint = endpoint, NodeCredential = credential });
    /// </code>
    /// </summary>
    public static class Node
    {
        /// <summary>注册请求超时（自助注册是短动作）。</summary>
        private static readonly TimeSpan EnrollTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 用一次性注册令牌换取节点凭据（POST /api/edge/enroll，唯一免鉴权端点）。
        ///
        /// - <b>不做自动重试</b>：令牌一次性，网络重试可能落在「服务端已消费」之后，
        ///   徒增 <c>ENROLLMENT_TOKEN_EXHAUSTED</c> 与半注册状态；失败请让运维重发令牌。
        /// - 成功后立即保存到 <paramref name="store"/>（本地加密落盘），**密钥只此一次返回**。
        /// </summary>
        public static async Task<NodeCredential> EnrollAsync(
            string endpoint,
            string enrollmentToken,
            string nodeId,
            string name = null,
            NodeCredentialStore store = null,
            IAndXTransport transport = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "EnrollAsync.endpoint 不能为空（后端或本机 Edge 基址）");
            }
            if (string.IsNullOrWhiteSpace(enrollmentToken))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "EnrollAsync.enrollmentToken 不能为空");
            }
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "EnrollAsync.nodeId 不能为空（展馆内唯一的硬件编号）");
            }

            var resolved = transport ?? ResolveDefaultTransport();
            if (resolved == null)
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "未注册传输实现：Unity 侧请确保 AndX.Unity 已初始化，或显式传入 IAndXTransport");
            }

            var api = new AndXApiClient(resolved, endpoint, null, EnrollTimeout, 0);
            var response = await api.PostAsync<EnrollResponse>(
                AndXContract.Paths.EdgeEnroll,
                new EnrollRequest
                {
                    Token = enrollmentToken.Trim(),
                    NodeId = nodeId.Trim(),
                    Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                },
                cancellationToken).ConfigureAwait(false);

            if (response == null || string.IsNullOrEmpty(response.Secret))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "注册响应缺少节点密钥（secret）");
            }

            var credential = new NodeCredential(
                string.IsNullOrEmpty(response.NodeId) ? nodeId.Trim() : response.NodeId,
                response.Secret);

            if (store != null)
            {
                store.Save(credential);
            }
            return credential;
        }

        private static IAndXTransport ResolveDefaultTransport()
        {
            var factory = AndXTransportProvider.Factory;
            return factory == null ? null : factory();
        }
    }
}