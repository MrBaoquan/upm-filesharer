using System;
using System.Threading;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class AndXApiClientTests
    {
        private static AndXApiClient Client(FakeTransport transport, int maxRetries = 0)
        {
            return new AndXApiClient(transport, "https://api.example.com", "edge-key", TimeSpan.FromSeconds(5), maxRetries);
        }

        [Fact]
        public async Task Get_maps_success_envelope_data()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"token\":\"t1\",\"amount\":123}"));
            var data = await Client(transport).GetAsync<SampleData>("/api/sample");
            Assert.Equal("t1", data.Token);
            Assert.Equal(123, data.Amount);
        }

        [Fact]
        public async Task Error_envelope_maps_to_andx_exception()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Error(403, AndXContract.ErrorCodes.EntitlementRequired, "需付费"));
            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Client(transport).GetAsync<SampleData>("/api/sample"));
            Assert.Equal(AndXContract.ErrorCodes.EntitlementRequired, ex.Code);
            Assert.Equal(403, ex.HttpStatus);
            Assert.True(ex.Is(AndXContract.ErrorCodes.EntitlementRequired));
        }

        [Fact]
        public async Task Retries_on_5xx_then_succeeds()
        {
            var transport = new FakeTransport((req, i) =>
                i == 0 ? FakeResponse.Json(500, "boom") : FakeResponse.Ok("{\"token\":\"ok\"}"));
            var data = await Client(transport, maxRetries: 2).GetAsync<SampleData>("/api/sample");
            Assert.Equal("ok", data.Token);
            Assert.Equal(2, transport.Calls);
        }

        [Fact]
        public async Task Network_error_maps_to_network_code()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Network());
            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Client(transport).GetAsync<SampleData>("/api/sample"));
            Assert.Equal(AndXContract.SdkErrorCodes.Network, ex.Code);
        }

        [Fact]
        public async Task Timeout_maps_to_timeout_code()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Network(timedOut: true));
            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Client(transport).GetAsync<SampleData>("/api/sample"));
            Assert.Equal(AndXContract.SdkErrorCodes.Timeout, ex.Code);
        }

        [Fact]
        public async Task ResolveUrl_normalizes_path()
        {
            var client = Client(new FakeTransport((req, i) => FakeResponse.Ok("{}")));
            Assert.Equal("https://api.example.com/api/scan/abc", client.ResolveUrl("api/scan/abc"));
            Assert.Equal("https://api.example.com/api/scan/abc", client.ResolveUrl("/api/scan/abc"));
        }

        [Fact]
        public void ResolveUrl_passes_absolute_urls_through()
        {
            var client = Client(new FakeTransport((req, i) => FakeResponse.Ok("{}")));
            Assert.Equal(
                "https://cdn.example.com/api/resources/tok/qrcode.png",
                client.ResolveUrl("https://cdn.example.com/api/resources/tok/qrcode.png"));
            Assert.Equal(
                "http://edge.internal/x",
                client.ResolveUrl("http://edge.internal/x"));
        }
    }
}
