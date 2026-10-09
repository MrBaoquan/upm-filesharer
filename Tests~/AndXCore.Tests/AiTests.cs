using System;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class AiTests
    {
        public AiTests()
        {
            Config.Reset();
        }

        private static void Configure(FakeTransport transport, string exhibitId = null)
        {
            Config.Init(
                new AndXOptions { Endpoint = "https://edge.example.com", EdgeKey = "edge-key", ExhibitId = exhibitId },
                transport);
        }

        [Fact]
        public async Task GetCapabilities_hits_ai_path_with_edge_key()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"available\":true,\"reason\":null,\"capabilities\":"
                + "[{\"capability\":\"ai.image.generate\",\"model\":\"ai-image\",\"available\":true}]}"));
            Configure(transport);

            var caps = await Ai.GetCapabilitiesAsync();

            Assert.True(caps.Available);
            Assert.Single(caps.Capabilities);
            Assert.Equal(AndXContract.AiCapabilities.ImageGenerate, caps.Capabilities[0].Capability);
            Assert.Equal("ai-image", caps.Capabilities[0].Model);

            var r = transport.Requests.Single();
            Assert.Equal("GET", r.Method);
            Assert.Equal("https://edge.example.com/api/ai/capabilities", r.Url);
            Assert.True(r.HasEdgeKey); // AI 控制面与 /api/edge/* 同样要求 X-Edge-Key
        }

        [Fact]
        public async Task CreateImageJob_posts_capability_prompt_and_options()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_0123456789abcdef01234567\",\"status\":\"PENDING\",\"cached\":false}"));
            Configure(transport);

            var job = await Ai.CreateImageJobAsync(new AiImageRequest
            {
                Prompt = "熊猫",
                ExhibitId = "1024",
                Size = "1024x1024",
                N = 1,
                IdemKey = "k1",
            });

            Assert.Equal("aj_0123456789abcdef01234567", job.JobNo);
            Assert.Equal(AndXContract.AiJobStatuses.Pending, job.Status);
            Assert.False(job.Cached);
            Assert.Equal(AndXContract.AiCapabilities.ImageGenerate, job.Capability);

            var r = transport.Requests.Single();
            Assert.Equal("POST", r.Method);
            Assert.Equal("https://edge.example.com/api/ai/jobs", r.Url);
            Assert.True(r.HasEdgeKey);
            Assert.Contains("\"capability\":\"ai.image.generate\"", r.Body);
            Assert.Contains("\"prompt\":\"熊猫\"", r.Body);
            Assert.Contains("\"exhibitId\":\"1024\"", r.Body);
            Assert.Contains("\"size\":\"1024x1024\"", r.Body);
            Assert.Contains("\"idemKey\":\"k1\"", r.Body);
        }

        [Fact]
        public async Task CreateImageJob_defaults_exhibit_from_global_options()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_1\",\"status\":\"PENDING\",\"cached\":false}"));
            Configure(transport, exhibitId: "777");

            await Ai.CreateImageJobAsync(new AiImageRequest { Prompt = "x" });

            Assert.Contains("\"exhibitId\":\"777\"", transport.Requests.Single().Body);
        }

        [Fact]
        public async Task CreateImageJob_omits_exhibit_when_unset()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_1\",\"status\":\"PENDING\",\"cached\":false}"));
            Configure(transport);

            await Ai.CreateImageJobAsync(new AiImageRequest { Prompt = "x" });

            Assert.DoesNotContain("exhibitId", transport.Requests.Single().Body);
        }

        [Fact]
        public async Task CreateImageJob_rejects_empty_prompt_without_network()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{}"));
            Configure(transport);

            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Ai.CreateImageJobAsync(new AiImageRequest { Prompt = "" }));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            Assert.Empty(transport.Requests);
        }

        [Fact]
        public async Task GetJob_maps_status_media_and_urls()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_1\",\"status\":\"SUCCEEDED\",\"capability\":\"ai.image.generate\","
                + "\"provider\":\"litellm\",\"model\":\"ai-image\",\"mediaId\":\"88123\",\"mediaIds\":[\"88123\"],"
                + "\"urls\":[\"https://minio/x?sig=1\"],\"errorCode\":null,\"errorMessage\":null,"
                + "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:01Z\"}"));
            Configure(transport);

            var job = await Ai.GetJobAsync("aj_1");

            Assert.Equal(AndXContract.AiJobStatuses.Succeeded, job.Status);
            Assert.Equal("88123", job.MediaId);
            Assert.Equal(new[] { "88123" }, job.MediaIds);
            Assert.Equal("https://minio/x?sig=1", job.Urls.Single());
            Assert.True(job.IsTerminal);

            var r = transport.Requests.Single();
            Assert.Equal("https://edge.example.com/api/ai/jobs/aj_1", r.Url);
            Assert.True(r.HasEdgeKey);
        }

        [Fact]
        public async Task CancelJob_posts_cancel_path()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_1\",\"status\":\"CANCELED\"}"));
            Configure(transport);

            var job = await Ai.CancelJobAsync("aj_1");

            Assert.Equal(AndXContract.AiJobStatuses.Canceled, job.Status);
            Assert.True(job.IsTerminal);
            var r = transport.Requests.Single();
            Assert.Equal("POST", r.Method);
            Assert.Equal("https://edge.example.com/api/ai/jobs/aj_1/cancel", r.Url);
        }

        [Fact]
        public async Task GenerateImage_submits_then_polls_until_terminal()
        {
            var polls = 0;
            var transport = new FakeTransport((req, i) =>
            {
                if (req.Method == "POST" && req.Url.EndsWith("/api/ai/jobs"))
                {
                    return FakeResponse.Ok("{\"jobNo\":\"aj_1\",\"status\":\"PENDING\",\"cached\":false}");
                }
                polls++;
                if (polls == 1)
                {
                    return FakeResponse.Ok("{\"jobNo\":\"aj_1\",\"status\":\"RUNNING\"}");
                }
                return FakeResponse.Ok("{\"jobNo\":\"aj_1\",\"status\":\"SUCCEEDED\",\"mediaId\":\"9\",\"mediaIds\":[\"9\"],\"urls\":[]}");
            });
            Configure(transport);

            var job = await Ai.GenerateImageAsync(
                new AiImageRequest { Prompt = "p" },
                new AiWaitOptions { Interval = TimeSpan.FromMilliseconds(1) });

            Assert.Equal(AndXContract.AiJobStatuses.Succeeded, job.Status);
            Assert.Equal("9", job.MediaId);
            Assert.Equal(2, polls);       // 两次查询：RUNNING → SUCCEEDED
            Assert.Equal(3, transport.Calls); // 提交 + 两次查询
        }

        [Fact]
        public async Task WaitForJob_times_out_returning_last_state()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(
                "{\"jobNo\":\"aj_1\",\"status\":\"RUNNING\"}"));
            Configure(transport);

            var job = await Ai.WaitForJobAsync(
                "aj_1",
                new AiWaitOptions { Timeout = TimeSpan.FromMilliseconds(5), Interval = TimeSpan.FromMilliseconds(1) });

            Assert.Equal(AndXContract.AiJobStatuses.Running, job.Status);
            Assert.False(job.IsTerminal);
        }

        [Fact]
        public async Task GetCapabilities_before_init_throws_configuration()
        {
            var ex = await Assert.ThrowsAsync<AndXException>(() => Ai.GetCapabilitiesAsync());
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }
    }
}
