using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// AI 产物取用（结果图字节 / 落盘 / 多图索引）的回归。
    ///
    /// 关键语义：`AIJob.urls` 是 **presigned（10 分钟）**，所以「用任务对象里的地址」与
    /// 「按 jobNo 先刷新再下载」必须是两条明确路径；越界取图要给可读错误而不是静默 null。
    /// </summary>
    public class AIResultTests : IDisposable
    {
        private static readonly byte[] PngBytes = Encoding.ASCII.GetBytes("fake-png-bytes");
        private readonly List<string> _tempFiles = new List<string>();

        public AIResultTests()
        {
            Config.Reset();
        }

        public void Dispose()
        {
            Config.Reset();
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

        private static TransportResponse BytesResponse(byte[] bytes)
        {
            return new TransportResponse
            {
                StatusCode = 200,
                Bytes = bytes,
                Text = Encoding.ASCII.GetString(bytes),
                ContentType = "image/png",
            };
        }

        private static string JobJson(params string[] urls)
        {
            var quoted = new List<string>();
            foreach (var url in urls)
            {
                quoted.Add("\"" + url + "\"");
            }
            return "{\"jobNo\":\"JOB-1\",\"status\":\"SUCCEEDED\",\"capability\":\"ai.image.generate\","
                + "\"mediaId\":\"88123\",\"mediaIds\":[\"88123\",\"88124\"],\"urls\":[" + string.Join(",", quoted) + "]}";
        }

        private static AIJob SampleJob()
        {
            return new AIJob
            {
                JobNo = "JOB-1",
                Status = AndXContract.AIJobStatuses.Succeeded,
                MediaId = "88123",
                MediaIds = new List<string> { "88123", "88124" },
                ResultUrls = new List<string> { "https://minio.test/a.png?sig=1", "https://minio.test/b.png?sig=2" },
            };
        }

        [Fact]
        public void ResultUrlAt_is_bounds_safe_and_ResultCount_falls_back_to_media_ids()
        {
            var job = SampleJob();

            Assert.Equal(2, job.ResultCount);
            Assert.Equal("https://minio.test/a.png?sig=1", job.ResultUrl);
            Assert.Equal("https://minio.test/b.png?sig=2", job.ResultUrlAt(1));
            Assert.Null(job.ResultUrlAt(2));
            Assert.Null(job.ResultUrlAt(-1));

            var withoutUrls = new AIJob { MediaIds = new List<string> { "1", "2", "3" } };
            Assert.Equal(3, withoutUrls.ResultCount);
            Assert.Null(withoutUrls.ResultUrl);
        }

        [Fact]
        public async Task GetResultBytesAsync_fast_path_uses_url_from_job()
        {
            var transport = new FakeTransport((req, i) => BytesResponse(PngBytes));
            Config.Init(new AndXOptions { Endpoint = "https://api.test" }, transport);

            var bytes = await AI.GetResultBytesAsync(SampleJob(), index: 1);

            Assert.Equal(PngBytes, bytes);
            Assert.Equal("https://minio.test/b.png?sig=2", transport.Requests[0].Url);
        }

        [Fact]
        public async Task GetResultBytesAsync_fresh_path_refreshes_job_before_download()
        {
            var transport = new FakeTransport((req, i) => req.Url.Contains("/api/ai/jobs/")
                ? FakeResponse.Ok(JobJson("https://minio.test/fresh.png?sig=9"))
                : BytesResponse(PngBytes));
            Config.Init(new AndXOptions { Endpoint = "https://api.test" }, transport);

            var bytes = await AI.GetResultBytesAsync("JOB-1");

            Assert.Equal(PngBytes, bytes);
            Assert.Equal(2, transport.Requests.Count);
            Assert.Contains("/api/ai/jobs/JOB-1", transport.Requests[0].Url);
            Assert.Equal("https://minio.test/fresh.png?sig=9", transport.Requests[1].Url);
        }

        [Fact]
        public async Task GetResultBytesAsync_throws_readable_error_when_index_out_of_range()
        {
            Config.Init(new AndXOptions { Endpoint = "https://api.test" }, new FakeTransport((req, i) => BytesResponse(PngBytes)));

            var ex = await Assert.ThrowsAsync<AndXException>(() => AI.GetResultBytesAsync(SampleJob(), index: 5));

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            Assert.Contains("第 5 张", ex.Message);
            Assert.Contains("2 张", ex.Message);
        }

        [Fact]
        public async Task SaveResultAsync_writes_bytes_to_disk_and_creates_directory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "andx-ai-" + Guid.NewGuid().ToString("N"));
            var file = Path.Combine(dir, "nested", "result-0.png");
            _tempFiles.Add(file);
            Config.Init(new AndXOptions { Endpoint = "https://api.test" }, new FakeTransport((req, i) => BytesResponse(PngBytes)));

            var written = await AI.SaveResultAsync(SampleJob(), 0, file);

            Assert.Equal(file, written);
            Assert.True(File.Exists(file));
            Assert.Equal(PngBytes, File.ReadAllBytes(file));
        }

        [Fact]
        public async Task SaveResultAsync_requires_target_path()
        {
            Config.Init(new AndXOptions { Endpoint = "https://api.test" }, new FakeTransport((req, i) => BytesResponse(PngBytes)));

            var ex = await Assert.ThrowsAsync<AndXException>(() => AI.SaveResultAsync(SampleJob(), 0, " "));

            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }
    }
}