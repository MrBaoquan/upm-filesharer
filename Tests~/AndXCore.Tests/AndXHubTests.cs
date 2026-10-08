using System;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using AndX.Pay;
using AndX.Share;
using Xunit;

namespace AndX.Tests
{
    public class AndXHubTests
    {
        private static FakeTransport OkTransport()
        {
            return new FakeTransport((req, i) => FakeResponse.Ok("{\"token\":\"t\",\"purpose\":\"EXHIBIT_PAY\",\"amount\":0}"));
        }

        [Fact]
        public void Create_rejects_null_options()
        {
            var ex = Assert.Throws<AndXException>(() => AndXHub.Create(null, OkTransport()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Create_rejects_plain_http_by_default()
        {
            var options = new AndXOptions { Endpoint = "http://internal.local" };
            var ex = Assert.Throws<AndXException>(() => AndXHub.Create(options, OkTransport()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Create_allows_plain_http_when_opted_in()
        {
            var options = new AndXOptions { Endpoint = "http://internal.local", AllowInsecureHttp = true };
            var hub = AndXHub.Create(options, OkTransport());
            Assert.NotNull(hub);
        }

        [Fact]
        public void Accessing_unregistered_capability_throws()
        {
            var hub = AndXHub.Create(new AndXOptions { Endpoint = "https://e.com" }, OkTransport());
            var ex = Assert.Throws<AndXException>(() => hub.Share());
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public void Use_registers_capabilities()
        {
            var hub = AndXHub.Create(new AndXOptions { Endpoint = "https://e.com" }, OkTransport())
                .Use<ShareCapability>()
                .Use<PayCapability>();
            Assert.NotNull(hub.Share());
            Assert.NotNull(hub.Pay());
        }

        [Fact]
        public async Task Edge_key_only_sent_to_edge_paths()
        {
            var transport = OkTransport();
            var hub = AndXHub.Create(
                new AndXOptions { Endpoint = "https://e.com", EdgeKey = "k" }, transport).Use<ShareCapability>();

            await hub.Share().IssueTicketAsync(AndXContract.Purposes.ResourceDownload, mediaId: "1");
            await hub.Share().ResolveAsync("tok");

            Assert.True(transport.Requests[0].HasEdgeKey);
            Assert.False(transport.Requests[1].HasEdgeKey);
        }

        [Fact]
        public async Task Pay_query_order_maps_status()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"orderNo\":\"o1\",\"status\":\"PAID\",\"amount\":100}"));
            var hub = AndXHub.Create(new AndXOptions { Endpoint = "https://e.com" }, transport).Use<PayCapability>();

            var status = await hub.Pay().QueryOrderAsync("o1");

            Assert.Equal("PAID", status.Status);
            Assert.Equal(100, status.Amount);
            Assert.Equal("https://e.com/api/pay/orders/o1", transport.Requests.Single().Url);
        }
    }
}
