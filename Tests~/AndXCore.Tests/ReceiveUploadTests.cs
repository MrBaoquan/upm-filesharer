using System;
using System.Linq;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using Xunit;

namespace Parful.AndX.Tests
{
    public class ReceiveUploadTests
    {
        public ReceiveUploadTests()
        {
            global::AndX.Config.Reset();
        }

        private static void Configure(FakeTransport transport, string exhibitId = "6")
        {
            global::AndX.Config.Init(
                new AndXOptions
                {
                    Endpoint = "https://api.example.com",
                    NodeCredential = new NodeCredential("HUB-1", "test-secret"),
                    ExhibitId = exhibitId,
                },
                transport);
        }

        [Fact]
        public async Task Create_receive_upload_maps_urls_and_sends_image_options()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                @"{""sessionId"":""rus_1"",""status"":""WAITING"",""version"":1,"
                + @"""mediaType"":""image"",""maxBytes"":15728640,""title"":""观众上传"","
                + @"""uploadUrl"":""https://andx.test/u/AbCd1234"","
                + @"""qrPngUrl"":""/api/receive-upload-sessions/AbCd1234/qrcode.png"","
                + @"""expiresAt"":""2026-10-22T10:00:00Z"",""completedAt"":null,"
                + @"""errorCode"":null,""imageSpec"":null,""media"":null}"));
            Configure(transport);

            var session = await global::AndX.Share.CreateReceiveUploadSessionAsync(
                new ReceiveUploadOptions
                {
                    MaxBytes = 10 * 1024 * 1024,
                    TtlSeconds = 120,
                    Title = "观众上传",
                });

            Assert.Equal("rus_1", session.SessionId);
            Assert.Equal(AndXContract.ReceiveUploadStatuses.Waiting, session.Status);
            Assert.Equal("https://andx.test/u/AbCd1234", session.UploadUrl);
            Assert.Equal(
                "https://api.example.com/api/receive-upload-sessions/AbCd1234/qrcode.png",
                session.QrImageUrl);
            var request = transport.Requests.Single();
            Assert.Equal("POST", request.Method);
            Assert.Equal("https://api.example.com/api/edge/receive-upload-sessions", request.Url);
            Assert.Contains(@"""exhibitId"":""6""", request.Body);
            Assert.Contains(@"""mediaType"":""image""", request.Body);
            Assert.Contains(@"""maxBytes"":10485760", request.Body);
            Assert.True(request.Headers.ContainsKey(AndXContract.Headers.DeviceSignature));
        }

        [Fact]
        public async Task Create_receive_upload_sends_image_spec()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                @"{""sessionId"":""rus_1"",""status"":""WAITING"",""version"":1,"
                + @"""mediaType"":""image"",""maxBytes"":15728640,""title"":null,"
                + @"""uploadUrl"":""https://andx.test/u/AbCd1234"",""qrPngUrl"":""/q.png"","
                + @"""expiresAt"":""2026-10-22T10:00:00Z"",""completedAt"":null,"
                + @"""errorCode"":null,""imageSpec"":null,""media"":null}"));
            Configure(transport);

            await global::AndX.Share.CreateReceiveUploadSessionAsync(
                new ReceiveUploadOptions
                {
                    ImageSpec = new ReceiveUploadImageSpec
                    {
                        Aspect = 0.75f,
                        AspectLocked = true,
                        MinEdgePx = 1080,
                    },
                });

            var body = transport.Requests.Single().Body;
            Assert.Contains(@"""imageSpec""", body);
            Assert.Contains(@"""aspect"":0.75", body);
            Assert.Contains(@"""aspectLocked"":true", body);
            Assert.Contains(@"""minEdgePx"":1080", body);
        }

        [Fact]
        public async Task Receive_upload_session_exposes_image_spec()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                @"{""sessionId"":""rus_1"",""status"":""WAITING"",""version"":1,"
                + @"""mediaType"":""image"",""maxBytes"":15728640,""title"":null,"
                + @"""uploadUrl"":""https://andx.test/u/AbCd1234"",""qrPngUrl"":""/q.png"","
                + @"""expiresAt"":""2026-10-22T10:00:00Z"",""completedAt"":null,"
                + @"""errorCode"":null,"
                + @"""imageSpec"":{""aspect"":0.75,""aspectLocked"":true,""minEdgePx"":1080},"
                + @"""media"":null}"));
            Configure(transport);

            var session = await global::AndX.Share.CreateReceiveUploadSessionAsync(
                new ReceiveUploadOptions());

            Assert.NotNull(session.ImageSpec);
            Assert.Equal(0.75f, session.ImageSpec.Aspect);
            Assert.True(session.ImageSpec.AspectLocked);
            Assert.Equal(1080, session.ImageSpec.MinEdgePx);
        }

        [Fact]
        public void Resolve_timeout_covers_edit_window()
        {
            // 默认：覆盖 600s 编辑窗口 + 一次上传。
            var defaults = new ReceiveUploadOptions();
            Assert.Equal(780, defaults.ResolveTimeout().TotalSeconds);

            // 显式更小的 Timeout 也要被抬到默认下限。
            var small = new ReceiveUploadOptions { Timeout = TimeSpan.FromMinutes(3) };
            Assert.Equal(780, small.ResolveTimeout().TotalSeconds);

            // ttlSeconds 更大时按编辑窗口 + ttl 联动。
            var longer = new ReceiveUploadOptions { TtlSeconds = 900 };
            Assert.Equal(1500, longer.ResolveTimeout().TotalSeconds);

            // 更小的 ttlSeconds 不会低于默认下限。
            var shorter = new ReceiveUploadOptions { TtlSeconds = 60 };
            Assert.Equal(780, shorter.ResolveTimeout().TotalSeconds);
        }

        [Fact]
        public async Task Get_receive_upload_maps_completed_media()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                @"{""sessionId"":""rus_1"",""status"":""COMPLETED"",""version"":4,"
                + @"""mediaType"":""image"",""maxBytes"":15728640,""title"":""观众上传"","
                + @"""uploadUrl"":""https://andx.test/u/AbCd1234"",""qrPngUrl"":""/q.png"","
                + @"""expiresAt"":""2026-10-22T10:00:00Z"",""completedAt"":""2026-10-22T09:01:00Z"","
                + @"""errorCode"":null,""media"":{""id"":""22"",""type"":""image"","
                + @"""title"":""观众上传"",""downloadUrl"":""https://media.test/a.jpg"",""expiresIn"":600}}"));
            Configure(transport);

            var session = await global::AndX.Share.GetReceiveUploadSessionAsync("rus_1");

            Assert.True(session.IsCompleted);
            Assert.True(session.IsTerminal);
            Assert.Equal("22", session.Media.Id);
            Assert.Equal("https://media.test/a.jpg", session.Media.DownloadUrl);
            Assert.Equal("GET", transport.Requests.Single().Method);
        }

        [Fact]
        public async Task Cancel_receive_upload_uses_delete()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(@"{""canceled"":true}"));
            Configure(transport);

            await global::AndX.Share.CancelReceiveUploadSessionAsync("rus_1");

            var request = transport.Requests.Single();
            Assert.Equal("DELETE", request.Method);
            Assert.Equal(
                "https://api.example.com/api/edge/receive-upload-sessions/rus_1",
                request.Url);
        }

        [Fact]
        public async Task Invalid_ttl_is_rejected_before_network()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("null"));
            Configure(transport);

            await Assert.ThrowsAsync<AndXException>(() =>
                global::AndX.Share.CreateReceiveUploadSessionAsync(
                    new ReceiveUploadOptions { TtlSeconds = 5 }));
            Assert.Equal(0, transport.Calls);
        }
    }
}
