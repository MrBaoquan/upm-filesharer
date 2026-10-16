using System;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// **手动**端到端：插件直连真实后端（真实 HTTP + 真实节点 HMAC v2 + 真实审计落库）。
    ///
    /// 默认跳过（不设 <c>ANDX_E2E=1</c> 时两个用例都直接返回），避免 CI / 日常 `dotnet test` 依赖外部服务。
    /// 需要以下环境变量（**凭据只经环境变量注入，勿写进仓库**）：
    /// <code>
    /// ANDX_E2E=1
    /// ANDX_E2E_BASE=http://127.0.0.1                     # 站点根（dev 栈 nginx → api 容器）
    /// ANDX_E2E_NODE_ID=UPMDEV-xxx
    /// ANDX_E2E_NODE_SECRET=...                           # 首启注册换来的节点密钥（用例一见）
    /// ANDX_E2E_CREDENTIAL_FILE=.../andx/node-credential.json  # 用例二：从落盘凭据加载
    /// ANDX_E2E_EXHIBIT_ID=1                              # 该节点绑定的展项（期望审计落到它头上）
    /// </code>
    ///
    /// 覆盖：① 直连模式建客户端 → ② 节点自省（我是谁 / 绑定展项）→ ③ 真实上传（走签名 + body 摘要）
    /// → ④ 审计自省：上传那次调用必须记在**该展项**头上且 <c>identity_source=node_hmac</c>；
    /// ⑤ 另一条用例验证「本机凭据文件」这条与 Unity 运行时一致的加载路径。
    /// </summary>
    public class ManualDirectE2ETests
    {
        /// <summary>1×1 PNG：走真实上传链路但素材体积极小。</summary>
        private const string OnePixelPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

        private static bool Enabled
        {
            get { return Environment.GetEnvironmentVariable("ANDX_E2E") == "1"; }
        }

        private static string Required(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("手动 E2E 缺少环境变量：" + name);
            }
            return value.Trim();
        }

        [Fact]
        public async Task Direct_mode_reports_node_identity_and_lands_audit_on_exhibit()
        {
            if (!Enabled)
            {
                return;   // 未开启：视为跳过（需要真实后端与真实节点凭据）
            }

            var endpoint = Required("ANDX_E2E_BASE");
            var nodeId = Required("ANDX_E2E_NODE_ID");
            var secret = Required("ANDX_E2E_NODE_SECRET");
            var expectedExhibit = Required("ANDX_E2E_EXHIBIT_ID");

            var savedFactory = AndXTransportProvider.Factory;
            try
            {
                AndXTransportProvider.Factory = () => new HttpTransport();

                // ① 直连模式：插件持本机节点凭据（示例里凭据来自 persistentDataPath 的凭据文件）
                Config.InitAuto(new AndXOptions
                {
                    AccessMode = AndXAccessMode.Direct,
                    Endpoint = endpoint,
                    NodeCredential = new NodeCredential(nodeId, secret),
                });

                Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
                Assert.Equal(nodeId, Config.ActiveNodeId);
                Assert.DoesNotContain(secret, Config.Describe());

                // ② 节点自省：服务端认的展项 = 期望展项
                NodeIdentity me = await Node.SelfAsync();
                Assert.Equal(nodeId, me.NodeId);
                Assert.Equal(expectedExhibit, me.ExhibitId);
                Assert.True(me.Enabled);

                // ③ 真实上传：流式端点要带 x-device-body-sha256 且纳入签名，能 201 说明签名口径与服务端一致
                ShareResult shared = await Share.UploadAsync(
                    new TestPayload(Convert.FromBase64String(OnePixelPngBase64), "andx-e2e.png"),
                    new UploadOptions { Title = "AndX 插件直连 E2E" });
                Assert.False(string.IsNullOrEmpty(shared.Token));

                // ④ 审计自省：这次上传必须记在「本节点 + 该展项」头上（这就是"审计到精确展项"）
                var entries = await Node.RecentAuditAsync(limit: 50);
                var uploaded = entries.FirstOrDefault(e => e.Path != null && e.Path.Contains("/api/edge/resources"));
                Assert.NotNull(uploaded);
                Assert.Equal(expectedExhibit, uploaded.ExhibitId);
                Assert.Equal("node_hmac", uploaded.IdentitySource);
                Assert.True(uploaded.IsSuccess);
            }
            finally
            {
                Config.Reset();
                AndXTransportProvider.Factory = savedFactory;
            }
        }

        [Fact]
        public async Task Direct_mode_loads_credential_from_file_and_passes_node_hmac()
        {
            if (!Enabled)
            {
                return;   // 未开启：视为跳过
            }

            var endpoint = Required("ANDX_E2E_BASE");
            var credentialFile = Required("ANDX_E2E_CREDENTIAL_FILE");
            var expectedNode = Required("ANDX_E2E_NODE_ID");

            var savedTransport = AndXTransportProvider.Factory;
            var savedStore = AndXCredentialProvider.StoreFactory;
            try
            {
                AndXTransportProvider.Factory = () => new HttpTransport();
                // 与 Unity 运行时同一条路径：平台注册的凭据存储
                // （工程里是 persistentDataPath/andx/node-credential.json）
                AndXCredentialProvider.StoreFactory = () => new NodeCredentialStore(credentialFile);

                Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Direct, Endpoint = endpoint });

                Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
                Assert.Equal(expectedNode, Config.ActiveNodeId);   // 证明「落盘格式 + 保护器」可用
                Assert.Contains("node:" + expectedNode, Config.Describe());

                NodeIdentity me = await Node.SelfAsync();
                Assert.Equal(expectedNode, me.NodeId);
                Assert.True(me.Enabled);
            }
            finally
            {
                Config.Reset();
                AndXTransportProvider.Factory = savedTransport;
                AndXCredentialProvider.StoreFactory = savedStore;
            }
        }
    }
}