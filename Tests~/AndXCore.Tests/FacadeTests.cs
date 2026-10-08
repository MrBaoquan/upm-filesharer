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

            await Share.IssueTicketAsync(AndXContract.Purposes.ResourceDownload, mediaId: "1");
            await Share.ResolveAsync("tok");

            Assert.True(transport.Requests[0].HasEdgeKey);
            Assert.False(transport.Requests[1].HasEdgeKey);
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
