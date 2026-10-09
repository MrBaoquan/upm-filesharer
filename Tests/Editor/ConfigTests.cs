using System.Threading;
using System.Threading.Tasks;
using AndX.Core;
using NUnit.Framework;

namespace AndX.Tests
{
    /// <summary>静态入口与配置语义的引擎侧回归。</summary>
    public class ConfigTests
    {
        private sealed class RecordingTransport : IAndXTransport
        {
            public TransportRequest Last { get; private set; }

            public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
            {
                Last = request;
                return Task.FromResult(new TransportResponse { StatusCode = 200, Text = "{}" });
            }
        }

        [TearDown]
        public void TearDown()
        {
            Config.Reset();
        }

        [Test]
        public void Init_sets_and_Reset_clears_configured()
        {
            Assert.IsFalse(Config.IsConfigured);
            Config.Init(new AndXOptions { Endpoint = "https://edge.test" }, new RecordingTransport());
            Assert.IsTrue(Config.IsConfigured);
            Config.Reset();
            Assert.IsFalse(Config.IsConfigured);
        }

        [Test]
        public void Init_rejects_plain_http_without_opt_in()
        {
            var ex = Assert.Throws<AndXException>(() =>
                Config.Init(new AndXOptions { Endpoint = "http://edge.test" }, new RecordingTransport()));
            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Test]
        public void Init_allows_plain_http_when_explicitly_opted_in()
        {
            Config.Init(
                new AndXOptions { Endpoint = "http://edge.test:8080", AllowInsecureHttp = true },
                new RecordingTransport());
            Assert.IsTrue(Config.IsConfigured);
        }

        [Test]
        public void Init_without_transport_throws_when_factory_absent()
        {
            var saved = AndXTransportProvider.Factory;
            try
            {
                AndXTransportProvider.Factory = null;
                var ex = Assert.Throws<AndXException>(() =>
                    Config.Init(new AndXOptions { Endpoint = "https://edge.test" }));
                Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            }
            finally
            {
                AndXTransportProvider.Factory = saved;
            }
        }

        [Test]
        public void Pay_QrImageUrl_resolves_public_qrcode_endpoint()
        {
            Config.Init(new AndXOptions { Endpoint = "https://edge.test" }, new RecordingTransport());
            Assert.AreEqual("https://edge.test/api/resources/tok123/qrcode.png", Pay.QrImageUrl("tok123"));
        }

        [Test]
        public void Share_rejects_empty_media_id_before_io()
        {
            Config.Init(new AndXOptions { Endpoint = "https://edge.test" }, new RecordingTransport());
            var ex = Assert.ThrowsAsync<AndXException>(() => Share.IssueResourceTicketAsync(string.Empty));
            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }
    }
}
