using System;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class FacadeTests
    {
        private static FakeTransport OkTransport()
        {
            return new FakeTransport((req, i) => FakeResponse.Ok("{\"token\":\"t\",\"purpose\":\"EXHIBIT_PAY\",\"amount\":0}"));
        }

        public FacadeTests()
        {
            Config.Reset();
        }

        [Fact]
        public void Init_rejects_null_options()
        {
            var ex = Assert.Throws<AndXException>(() => Config.Init(null, OkTransport()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Init_rejects_plain_http_by_default()
        {
            var options = new AndXOptions { Endpoint = "http://internal.local" };
            var ex = Assert.Throws<AndXException>(() => Config.Init(options, OkTransport()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Init_allows_plain_http_when_opted_in()
        {
            Config.Init(new AndXOptions { Endpoint = "http://internal.local", AllowInsecureHttp = true }, OkTransport());
            Assert.True(Config.IsConfigured);
        }

        [Fact]
        public void Init_allows_plain_http_on_loopback_without_opt_in()
        {
            Config.Init(new AndXOptions { Endpoint = "http://127.0.0.1:6699" }, OkTransport());
            Assert.True(Config.IsConfigured);
        }

        [Fact]
        public async Task InitLocal_points_to_loopback_edge()
        {
            var transport = OkTransport();
            var saved = AndXTransportProvider.Factory;
            AndXTransportProvider.Factory = () => transport;
            try
            {
                Config.InitLocal();
                Assert.True(Config.IsConfigured);

                await Share.ResolveAsync("tok");

                Assert.StartsWith("http://127.0.0.1:6699/api/scan/tok", transport.Requests.Single().Url);
            }
            finally
            {
                AndXTransportProvider.Factory = saved;
                Config.Reset();
            }
        }

        [Fact]
        public void Access_before_init_throws_configuration()
        {
            var ex = Assert.Throws<AndXException>(() => Share.QrImageUrl("tok"));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Reset_clears_configuration()
        {
            Config.Init(new AndXOptions { Endpoint = "https://e.com" }, OkTransport());
            Config.Reset();
            Assert.False(Config.IsConfigured);
        }

        [Fact]
        public async Task Edge_key_only_sent_to_edge_paths()
        {
            var transport = OkTransport();
            Config.Init(new AndXOptions { Endpoint = "https://e.com", EdgeKey = "k" }, transport);

            await Share.IssueResourceTicketAsync("1");
            await Share.ResolveAsync("tok");

            Assert.True(transport.Requests[0].HasEdgeKey);
            Assert.False(transport.Requests[1].HasEdgeKey);
        }

        [Fact]
        public async Task Access_token_is_sent_as_bearer_on_all_requests()
        {
            var transport = OkTransport();
            Config.Init(new AndXOptions { Endpoint = "https://e.com", AccessToken = "jwt-1" }, transport);

            await Share.ResolveAsync("tok");

            Assert.Equal("Bearer jwt-1", transport.Requests[0].Headers[AndXContract.Headers.Authorization]);
        }

        [Fact]
        public async Task Access_token_provider_reflects_latest_value()
        {
            var transport = OkTransport();
            var token = "jwt-old";
            Config.Init(new AndXOptions { Endpoint = "https://e.com", AccessTokenProvider = () => token }, transport);

            await Share.ResolveAsync("tok");
            token = "jwt-new";
            await Share.ResolveAsync("tok");

            Assert.Equal("Bearer jwt-old", transport.Requests[0].Headers[AndXContract.Headers.Authorization]);
            Assert.Equal("Bearer jwt-new", transport.Requests[1].Headers[AndXContract.Headers.Authorization]);
        }

        [Fact]
        public async Task No_authorization_header_when_token_absent()
        {
            var transport = OkTransport();
            Config.Init(new AndXOptions { Endpoint = "https://e.com" }, transport);

            await Share.ResolveAsync("tok");

            Assert.False(transport.Requests[0].Headers.ContainsKey(AndXContract.Headers.Authorization));
        }

        [Fact]
        public async Task Pay_query_order_maps_status()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"orderNo\":\"o1\",\"status\":\"PAID\",\"amount\":100}"));
            Config.Init(new AndXOptions { Endpoint = "https://e.com" }, transport);

            var status = await Pay.QueryOrderAsync("o1");

            Assert.Equal("PAID", status.Status);
            Assert.Equal(100, status.Amount);
            Assert.Equal("https://e.com/api/pay/orders/o1", transport.Requests.Single().Url);
        }
    }
}
