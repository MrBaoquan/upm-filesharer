using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// 节点身份（solution §16.2 / §16.3，implementation §13.6）：签名规范、请求注入、
    /// 自助注册与凭据落盘。
    ///
    /// 关键点：签名原文与 HMAC **必须与服务端逐字节一致**，故此处固化了用服务端同款算法
    /// （node: crypto.createHmac('sha256')）算出的测试向量——任何一侧改动都会让本用例变红。
    /// 覆盖无 query（v1 与 v2 结果一致）与含 query（v2 特有）两类输入。
    /// </summary>
    public class NodeIdentityTests
    {
        private const string Secret = "test-node-secret-0123456789";
        private const string Timestamp = "1735689600000";
        private const string Nonce = "0123456789abcdef0123456789abcdef";
        private const string TicketBody = "{\"purpose\":\"EXHIBIT_PAY\",\"exhibitId\":\"1024\"}";

        public NodeIdentityTests()
        {
            Config.Reset();
        }

        private static NodeCredential Credential(string nodeId = "DEV-1")
        {
            return new NodeCredential(nodeId, Secret);
        }

        private static void Configure(FakeTransport transport, NodeCredential credential, string exhibitId = null)
        {
            Config.Init(
                new AndXOptions
                {
                    Endpoint = "https://api.example.com",
                    NodeCredential = credential,
                    ExhibitId = exhibitId,
                },
                transport);
        }

        // ===== 签名规范（跨端测试向量） =====

        [Fact]
        public void Canonical_and_hmac_match_server_test_vector()
        {
            var body = Encoding.UTF8.GetBytes(TicketBody);

            Assert.Equal(
                "6a1144e259763bdb5d2f7c7e5e527205475475d23717d9d05947d882c123c74c",
                DeviceSigner.Sha256Hex(body));

            var canonical = DeviceSigner.BuildCanonical("POST", "/api/edge/tickets", Timestamp, Nonce, body);
            Assert.Equal(
                "POST\n/api/edge/tickets\n" + Timestamp + "\n" + Nonce
                + "\n6a1144e259763bdb5d2f7c7e5e527205475475d23717d9d05947d882c123c74c",
                canonical);
            Assert.Equal("7e569fb70b43c99897aaf83ccde9f50fb178b2d02f83439dd91840af2b1ead01",
                DeviceSigner.Sign(Secret, canonical));

            // 空体（GET）向量
            Assert.Equal(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                DeviceSigner.Sha256Hex(new byte[0]));
            var getCanonical = DeviceSigner.BuildCanonical("GET", "/api/ai/capabilities", Timestamp, Nonce, new byte[0]);
            Assert.Equal("dc9e96f6ec953d56ddb63c00f6e46f8b90a0399a0c6087c42264865fcf3a73c5",
                DeviceSigner.Sign(Secret, getCanonical));
        }

        [Fact]
        public void Signed_path_includes_normalized_query()
        {
            // v2：查询串参与签名，且按 key/value 升序规范化（与服务端 canonicalizePath 逐字节一致）
            Assert.Equal(
                "/api/edge/resources?exhibitId=1024&fileName=a%20b.png&mediaType=image",
                DeviceSigner.BuildSignedPath(
                    "https://api.example.com/api/edge/resources?mediaType=image&fileName=a%20b.png&exhibitId=1024"));
            Assert.Equal("/x?j=1&k=a&k=b", DeviceSigner.BuildSignedPath("/x?k=b&k=a&j=1"));
            Assert.Equal("/x?flag&z=1", DeviceSigner.BuildSignedPath("/x?flag=&z=1"));
            Assert.Equal("/api/edge/tickets", DeviceSigner.BuildSignedPath("https://api.example.com/api/edge/tickets"));
        }

        [Fact]
        public void Canonical_v2_matches_server_test_vectors()
        {
            var empty = new byte[0];

            var unsorted = DeviceSigner.BuildCanonical(
                "GET", "/api/edge/resources?mediaType=image&fileName=a%20b.png&exhibitId=1024",
                Timestamp, Nonce, empty);
            Assert.Equal(
                "GET\n/api/edge/resources?exhibitId=1024&fileName=a%20b.png&mediaType=image\n"
                + Timestamp + "\n" + Nonce
                + "\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                unsorted);
            Assert.Equal("348912f77a1bdfa3a3f0201c4744eeee09caa883e38e171e985539b1a3c4523f",
                DeviceSigner.Sign(Secret, unsorted));

            // 同名参数（value 参与排序）与空值参数（渲染为 key）
            Assert.Equal("f2ff2e433f37e6e72e2be6968643119758a5d155f7c44e21937fac320c6b4f88",
                DeviceSigner.Sign(Secret, DeviceSigner.BuildCanonical("GET", "/x?k=b&k=a&j=1", Timestamp, Nonce, empty)));
            Assert.Equal("13099d50caa6b0b7a68cbc9819d776974e1cfc62b5da6ee299048f7157e79125",
                DeviceSigner.Sign(Secret, DeviceSigner.BuildCanonical("GET", "/x?flag=&z=1", Timestamp, Nonce, empty)));
        }

        [Fact]
        public void Credential_masks_secret_in_to_string()
        {
            var text = Credential().ToString();
            Assert.Contains("DEV-1", text);
            Assert.DoesNotContain(Secret, text);
        }

        // ===== 请求注入 =====

        [Fact]
        public async Task Control_plane_requests_carry_verifiable_node_signature()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"available\":true,\"reason\":null,\"capabilities\":[]}"));
            Configure(transport, Credential());

            await AI.GetCapabilitiesAsync();

            var request = transport.Requests.Single();
            Assert.False(request.HasEdgeKey); // 节点身份优先，不再下发共享密钥
            var nodeId = request.Headers[AndXContract.Headers.DeviceId];
            var timestamp = request.Headers[AndXContract.Headers.DeviceTimestamp];
            var nonce = request.Headers[AndXContract.Headers.DeviceNonce];
            var signature = request.Headers[AndXContract.Headers.DeviceSignature];
            Assert.Equal("DEV-1", nodeId);
            Assert.Equal(32, nonce.Length);
            Assert.True(long.Parse(timestamp) > 0);

            // 用请求里的四要素重算签名，必须与下发的一致（等价于服务端校验）
            var canonical = DeviceSigner.BuildCanonical(request.Method, DeviceSigner.BuildSignedPath(request.Url),
                timestamp, nonce, null);
            Assert.Equal(DeviceSigner.Sign(Secret, canonical), signature);
        }

        [Fact]
        public async Task Control_plane_requests_declare_body_sha256_header()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"available\":true,\"reason\":null,\"capabilities\":[]}"));
            Configure(transport, Credential());

            await AI.GetCapabilitiesAsync();

            var request = transport.Requests.Single();
            // 流式端点服务端不缓冲 body：摘要头必须下发，且等于 canonical 里用的那个哈希
            var declared = request.Headers[AndXContract.Headers.DeviceBodySha256];
            Assert.Equal(DeviceSigner.Sha256Hex(null), declared);

            var canonical = DeviceSigner.BuildCanonicalFromBodyHash(
                request.Method,
                DeviceSigner.BuildSignedPath(request.Url),
                request.Headers[AndXContract.Headers.DeviceTimestamp],
                request.Headers[AndXContract.Headers.DeviceNonce],
                declared);
            Assert.Equal(DeviceSigner.Sign(Secret, canonical), request.Headers[AndXContract.Headers.DeviceSignature]);
        }

        [Fact]
        public void Body_hash_header_does_not_change_canonical_for_same_body()
        {
            var body = Encoding.UTF8.GetBytes(TicketBody);
            var fromBody = DeviceSigner.BuildCanonical("POST", "/api/edge/resources?b=2&a=1", Timestamp, Nonce, body);
            var fromHash = DeviceSigner.BuildCanonicalFromBodyHash(
                "POST",
                "/api/edge/resources?a=1&b=2",
                Timestamp,
                Nonce,
                DeviceSigner.Sha256Hex(body));

            Assert.Equal(fromBody, fromHash);
        }

        [Fact]
        public async Task Public_requests_are_not_signed()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{}"));
            Configure(transport, Credential());

            await Share.ResolveAsync("tok123");

            var request = transport.Requests.Single();
            Assert.Equal("https://api.example.com/api/scan/tok123", request.Url);
            Assert.False(request.Headers.ContainsKey(AndXContract.Headers.DeviceSignature));
            Assert.False(request.Headers.ContainsKey(AndXContract.Headers.DeviceId));
        }

        [Fact]
        public async Task Retries_re_sign_with_fresh_nonce()
        {
            var transport = new FakeTransport((req, i) => i < 2
                ? FakeResponse.Error(500, "INTERNAL_ERROR", "boom")
                : FakeResponse.Ok("{\"available\":true,\"reason\":null,\"capabilities\":[]}"));
            Config.Init(
                new AndXOptions { Endpoint = "https://api.example.com", NodeCredential = Credential(), MaxRetries = 2 },
                transport);

            await AI.GetCapabilitiesAsync();

            Assert.Equal(3, transport.Calls);
            var nonces = transport.Requests.Select(r => r.Headers[AndXContract.Headers.DeviceNonce]).ToList();
            Assert.Equal(3, nonces.Distinct().Count()); // nonce 一次性：重试必须重新签名
        }

        // ===== 自助注册 =====

        [Fact]
        public async Task Enroll_posts_token_and_persists_credential()
        {
            var storePath = Path.Combine(Path.GetTempPath(), "andx-node-" + Guid.NewGuid().ToString("N"), "credential.json");
            var store = new NodeCredentialStore(storePath);
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"nodeId\":\"DEV-1\",\"role\":\"exhibit\",\"exhibitId\":\"10\",\"secret\":\"" + Secret + "\"}"));

            var credential = await Node.EnrollAsync(
                "https://api.example.com", "enroll-token-1", "DEV-1", name: "1 号机", store: store, transport: transport);

            Assert.Equal("DEV-1", credential.NodeId);
            Assert.Equal(Secret, credential.Secret);
            var request = transport.Requests.Single();
            Assert.Equal("https://api.example.com/api/edge/enroll", request.Url);
            Assert.Contains("\"token\":\"enroll-token-1\"", request.Body);
            Assert.Contains("\"nodeId\":\"DEV-1\"", request.Body);
            Assert.Contains("\"name\":\"1 号机\"", request.Body);
            Assert.False(request.HasEdgeKey);
            Assert.False(request.Headers.ContainsKey(AndXContract.Headers.DeviceSignature)); // 注册本身免鉴权

            Assert.True(File.Exists(storePath));
            var loaded = store.Load();
            Assert.Equal("DEV-1", loaded.NodeId);
            Assert.Equal(Secret, loaded.Secret);
            store.Clear();
            Assert.Null(store.Load());
        }

        [Fact]
        public async Task Enroll_rejects_incomplete_input_without_sending()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{}"));

            await Assert.ThrowsAsync<AndXException>(() =>
                Node.EnrollAsync("https://api.example.com", "", "DEV-1", transport: transport));
            await Assert.ThrowsAsync<AndXException>(() =>
                Node.EnrollAsync("", "token", "DEV-1", transport: transport));
            Assert.Equal(0, transport.Calls);
        }

        // ===== 凭据落盘 =====

        /// <summary>可逆的假保护器（Base64+反转）：用于验证「明文密钥不落盘」这条契约。</summary>
        private sealed class FakeProtector : ICredentialProtector
        {
            public string Protect(string plaintext)
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(Reverse(plaintext)));
            }

            public string Unprotect(string protectedValue)
            {
                return Reverse(Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)));
            }

            public bool IsEncryptedAtRest
            {
                get { return true; }
            }

            private static string Reverse(string value)
            {
                return new string(value.Reverse().ToArray());
            }
        }

        [Fact]
        public void Credential_store_protects_secret_at_rest_and_round_trips()
        {
            var path = Path.Combine(Path.GetTempPath(), "andx-node-" + Guid.NewGuid().ToString("N"), "credential.json");
            var store = new NodeCredentialStore(path, new FakeProtector());

            store.Save(Credential());

            Assert.True(store.IsEncryptedAtRest);
            var onDisk = File.ReadAllText(path);
            Assert.DoesNotContain(Secret, onDisk); // 密钥不以明文落盘
            Assert.Contains("secretProtected", onDisk);

            var loaded = store.Load();
            Assert.Equal("DEV-1", loaded.NodeId);
            Assert.Equal(Secret, loaded.Secret);

            store.Clear();
            Assert.Null(store.Load());
        }

        [Fact]
        public void Credential_store_defaults_to_passthrough_and_reports_it()
        {
            var store = new NodeCredentialStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            Assert.False(store.IsEncryptedAtRest);
            Assert.Null(store.Load());
        }

        // ===== 付费：客户端不抢答展项归属 =====

        [Fact]
        public async Task Pay_allows_omitting_exhibit_id()
        {
            var transport = new FakeTransport((req, i) =>
                FakeResponse.Ok("{\"token\":\"t\",\"purpose\":\"EXHIBIT_PAY\",\"amount\":100}"));
            Configure(transport, Credential()); // 不传 ExhibitId：由服务端按节点绑定裁决

            var ticket = await Pay.CreateTicketAsync(new PayTicketOptions());

            Assert.Equal("t", ticket.Token);
            var request = transport.Requests.Single();
            Assert.Equal("https://api.example.com/api/edge/tickets", request.Url);
            Assert.DoesNotContain("exhibitId", request.Body);
            Assert.True(request.Headers.ContainsKey(AndXContract.Headers.DeviceSignature));
        }
    }
}