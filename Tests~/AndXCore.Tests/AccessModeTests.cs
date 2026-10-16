using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// 接入方式解析（直连 / 经 Edge / 自动）与节点自省的回归。
    ///
    /// 关键契约（solution §16）：两种接入形态**只差一个配置入口**——业务调用点完全一致，
    /// 变的只有「凭据从哪来 / 展项身份由谁裁决 / 服务端审计口径」。
    /// </summary>
    public class AccessModeTests : IDisposable
    {
        private static readonly string[] EnvNames =
        {
            AndXEnvironment.EndpointVariable,
            AndXEnvironment.EdgeEndpointVariable,
            AndXEnvironment.EnrollmentTokenVariable,
            AndXEnvironment.NodeIdVariable,
            AndXEnvironment.AllowInsecureHttpVariable,
        };

        private readonly Dictionary<string, string> _envBackup = new Dictionary<string, string>();
        private readonly Func<IAndXTransport> _factoryBackup;
        private readonly Func<NodeCredentialStore> _storeBackup;
        private readonly List<string> _tempFiles = new List<string>();

        public AccessModeTests()
        {
            foreach (var name in EnvNames)
            {
                _envBackup[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
            _factoryBackup = AndXTransportProvider.Factory;
            _storeBackup = AndXCredentialProvider.StoreFactory;
            AndXTransportProvider.Factory = null;
            AndXCredentialProvider.StoreFactory = null;
            Config.Reset();
        }

        public void Dispose()
        {
            Config.Reset();
            AndXTransportProvider.Factory = _factoryBackup;
            AndXCredentialProvider.StoreFactory = _storeBackup;
            foreach (var pair in _envBackup)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // 临时文件清理失败不影响用例结论
                }
            }
        }

        private static string OkTicket()
        {
            return "{\"token\":\"t\",\"purpose\":\"RESOURCE_DOWNLOAD\",\"amount\":0}";
        }

        private static FakeTransport UseTransport(FakeTransport transport)
        {
            AndXTransportProvider.Factory = () => transport;
            return transport;
        }

        /// <summary>注册一个临时文件的凭据存储；<paramref name="nodeId"/> 非空时预置一条凭据。</summary>
        private NodeCredentialStore UseStore(string nodeId = null, string secret = null)
        {
            var path = Path.Combine(Path.GetTempPath(), "andx-node-" + Guid.NewGuid().ToString("N") + ".json");
            _tempFiles.Add(path);
            var store = new NodeCredentialStore(path);
            if (nodeId != null)
            {
                store.Save(new NodeCredential(nodeId, secret));
            }
            AndXCredentialProvider.StoreFactory = () => store;
            return store;
        }

        // ---------- 默认直连 ----------

        [Fact]
        public void Default_mode_is_direct_and_requires_backend_endpoint()
        {
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));
            UseStore();

            var ex = Assert.Throws<AndXException>(() => Config.InitAuto());

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            Assert.Contains(AndXEnvironment.EndpointVariable, ex.Message);
        }

        [Fact]
        public void Direct_mode_from_environment_uses_stored_node_identity()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-001", "secret-1");
            var transport = UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions());

            Assert.True(Config.IsConfigured);
            Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
            Assert.Equal("DEV-001", Config.ActiveNodeId);
            Assert.Contains("node:DEV-001", Config.Describe());
            Assert.DoesNotContain("secret-1", Config.Describe());
            Assert.True(transport.Requests.Count == 0);
        }

        [Fact]
        public async Task Direct_mode_signs_control_plane_requests_with_node_headers()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-001", "secret-1");
            var transport = UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto();

            await Share.IssueResourceTicketAsync("88123");

            var request = Assert.Single(transport.Requests);
            Assert.Equal("DEV-001", request.Headers[AndXContract.Headers.DeviceId]);
            Assert.False(string.IsNullOrEmpty(request.Headers[AndXContract.Headers.DeviceSignature]));
            Assert.False(request.HasEdgeKey);
        }

        [Fact]
        public void Explicit_options_win_over_environment()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-001", "secret-1");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions { Endpoint = "https://api.explicit", ExhibitId = "1024" });

            Assert.StartsWith("https://api.explicit", Pay.QrImageUrl("tok123"));
            Assert.Contains("exhibitId=1024", Config.Describe());
        }

        [Fact]
        public void Direct_mode_without_identity_explains_how_to_enroll()
        {
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));
            UseStore();

            var ex = Assert.Throws<AndXException>(() => Config.InitAuto(new AndXOptions { Endpoint = "https://api.test" }));

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            Assert.Contains("AndX.Node.EnrollAsync", ex.Message);
            Assert.Contains(AndXAccessMode.Edge.ToString(), ex.Message);
        }

        [Fact]
        public void Direct_mode_with_enrollment_token_requires_async_entry()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            Environment.SetEnvironmentVariable(AndXEnvironment.EnrollmentTokenVariable, "tok-once");
            UseStore();
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            var ex = Assert.Throws<AndXException>(() => Config.InitAuto());

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            Assert.Contains("InitAutoAsync", ex.Message);
        }

        [Fact]
        public async Task InitAutoAsync_enrolls_with_token_and_persists_credential()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            Environment.SetEnvironmentVariable(AndXEnvironment.EnrollmentTokenVariable, "tok-once");
            Environment.SetEnvironmentVariable(AndXEnvironment.NodeIdVariable, "DEV-002");
            Environment.SetEnvironmentVariable(AndXEnvironment.AllowInsecureHttpVariable, "1");
            var store = UseStore();
            var transport = UseTransport(new FakeTransport((req, i) =>
                req.Url.EndsWith("/api/edge/enroll")
                    ? FakeResponse.Ok("{\"nodeId\":\"DEV-002\",\"secret\":\"issued-secret\"}")
                    : FakeResponse.Ok(OkTicket())));

            await Config.InitAutoAsync();

            Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
            Assert.Equal("DEV-002", Config.ActiveNodeId);
            Assert.Contains("DEV-002", transport.Requests[0].Body);
            Assert.Contains("tok-once", transport.Requests[0].Body);

            var persisted = store.Load();
            Assert.NotNull(persisted);
            Assert.Equal("issued-secret", persisted.Secret);
        }

        // ---------- 经 Edge ----------

        [Fact]
        public void Edge_mode_falls_back_to_loopback_and_drops_node_credential()
        {
            UseStore("DEV-001", "secret-1");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions
            {
                AccessMode = AndXAccessMode.Edge,
                NodeCredential = new NodeCredential("DEV-999", "explicit-secret"),
            });

            Assert.Equal(AndXAccessMode.Edge, Config.ActiveMode);
            Assert.Null(Config.ActiveNodeId);
            Assert.StartsWith(AndXContract.Defaults.LocalEdgeEndpoint, Pay.QrImageUrl("tok123"));
            Assert.Contains("Edge", Config.ResolutionReason);
        }

        [Fact]
        public void Edge_mode_honors_edge_endpoint_environment()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EdgeEndpointVariable, "http://127.0.0.1:7000");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Edge });

            Assert.StartsWith("http://127.0.0.1:7000", Pay.QrImageUrl("tok123"));
        }

        [Fact]
        public void InitLocal_is_edge_mode()
        {
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitLocal();

            Assert.Equal(AndXAccessMode.Edge, Config.ActiveMode);
            Assert.StartsWith(AndXContract.Defaults.LocalEdgeEndpoint, Pay.QrImageUrl("tok123"));
        }

        // ---------- 自动 ----------

        [Fact]
        public void Auto_prefers_direct_when_machine_has_identity()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-003", "secret-3");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Auto });

            Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
            Assert.Equal("DEV-003", Config.ActiveNodeId);
        }

        [Fact]
        public void Auto_falls_back_to_edge_without_identity()
        {
            UseStore();
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions { AccessMode = AndXAccessMode.Auto });

            Assert.Equal(AndXAccessMode.Edge, Config.ActiveMode);
            Assert.Contains("回退", Config.ResolutionReason);
        }

        // ---------- 内网明文放行 ----------

        [Fact]
        public void Direct_mode_rejects_plain_http_lan_endpoint_without_opt_in()
        {
            UseStore("DEV-001", "secret-1");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            var ex = Assert.Throws<AndXException>(() => Config.InitAuto(new AndXOptions { Endpoint = "http://192.168.1.10:3000" }));

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Direct_mode_allows_plain_http_lan_endpoint_when_environment_opts_in()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.AllowInsecureHttpVariable, "true");
            UseStore("DEV-001", "secret-1");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(OkTicket())));

            Config.InitAuto(new AndXOptions { Endpoint = "http://192.168.1.10:3000" });

            Assert.True(Config.IsConfigured);
            Assert.Equal(AndXAccessMode.Direct, Config.ActiveMode);
        }

        // ---------- 节点自省 ----------

        [Fact]
        public async Task SelfAsync_maps_node_identity()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-004", "secret-4");
            var transport = UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"nodeId\":\"DEV-004\",\"role\":\"hub\",\"name\":\"中控\",\"exhibitId\":null,"
                + "\"authorizedExhibitIds\":[\"1024\",\"2048\"],\"enabled\":true,\"state\":\"IDLE\","
                + "\"secretHint\":\"0004\",\"serverTime\":\"2026-10-16T01:00:00.000Z\"}")));

            Config.InitAuto();
            var identity = await Node.SelfAsync();

            Assert.Equal("DEV-004", identity.NodeId);
            Assert.Equal("hub", identity.Role);
            Assert.True(identity.IsHub);
            Assert.True(identity.Enabled);
            Assert.Null(identity.ExhibitId);
            Assert.Equal(new[] { "1024", "2048" }, identity.AuthorizedExhibitIds);
            Assert.Equal("0004", identity.SecretHint);
            Assert.Equal(AndXContract.Paths.DeviceSelf, new Uri(transport.Requests[0].Url).AbsolutePath);
        }

        [Fact]
        public async Task SelfAsync_exposes_bound_exhibit_for_exhibit_node()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-005", "secret-5");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"nodeId\":\"DEV-005\",\"role\":\"exhibit\",\"exhibitId\":\"1024\",\"enabled\":true}")));

            Config.InitAuto();
            var identity = await Node.SelfAsync();

            Assert.Equal("1024", identity.ExhibitId);
            Assert.False(identity.IsHub);
        }

        [Fact]
        public async Task RecentAuditAsync_clamps_limit_and_maps_rows()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-006", "secret-6");
            var transport = UseTransport(new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"items\":[{\"id\":\"1\",\"exhibitId\":\"1024\",\"deviceId\":\"DEV-006\","
                + "\"identitySource\":\"node_hmac\",\"endpoint\":\"edge\",\"method\":\"POST\","
                + "\"path\":\"/api/edge/tickets\",\"statusCode\":403,\"errorCode\":\"NODE_EXHIBIT_FORBIDDEN\","
                + "\"bytesIn\":0,\"durationMs\":12,\"traceId\":\"t-1\",\"clientIp\":\"10.0.0.9\","
                + "\"createdAt\":\"2026-10-16T01:00:00.000Z\"}],\"hasMore\":false,\"nextBeforeId\":null}")));

            Config.InitAuto();
            var entries = await Node.RecentAuditAsync(limit: 1000);

            var entry = Assert.Single(entries);
            Assert.Equal("1024", entry.ExhibitId);
            Assert.Equal("node_hmac", entry.IdentitySource);
            Assert.Equal("NODE_EXHIBIT_FORBIDDEN", entry.ErrorCode);
            Assert.False(entry.IsSuccess);
            Assert.Equal(12, entry.DurationMs);
            Assert.Contains("limit=100", transport.Requests[0].Url);
        }

        [Fact]
        public async Task RecentAuditAsync_returns_empty_list_when_page_is_empty()
        {
            Environment.SetEnvironmentVariable(AndXEnvironment.EndpointVariable, "https://api.test");
            UseStore("DEV-007", "secret-7");
            UseTransport(new FakeTransport((req, i) => FakeResponse.Ok("{\"items\":[],\"hasMore\":false,\"nextBeforeId\":null}")));

            Config.InitAuto();
            var entries = await Node.RecentAuditAsync();

            Assert.Empty(entries);
        }
    }
}