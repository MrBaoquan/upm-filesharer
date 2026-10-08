using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class ShareTests
    {
        public ShareTests()
        {
            Config.Reset();
        }

        /// <summary>阈值设为 8 字节，便于用小样本覆盖分流与分片。</summary>
        private static void Configure(FakeTransport transport)
        {
            Config.Init(new AndXOptions { Endpoint = "https://edge.example.com", EdgeKey = "edge-key", ChunkSize = 8 }, transport);
        }

        private static FakeTransport Router(long chunkSize, long received, long total, List<string> putUrls, bool rejectOneShot = false)
        {
            return new FakeTransport((req, i) =>
            {
                if (req.Method == "POST" && req.Url.Contains("/api/edge/resources"))
                {
                    return rejectOneShot
                        ? FakeResponse.Error(413, AndXContract.ErrorCodes.ValidationFailed, "文件超过单次上传上限")
                        : CompleteResponse(total);
                }
                if (req.Method == "POST" && req.Url.EndsWith("/api/edge/uploads"))
                {
                    return FakeResponse.Ok("{\"uploadId\":\"u_1\",\"chunkSize\":" + chunkSize + ",\"expiresAt\":null}");
                }
                if (req.Method == "GET" && req.Url.Contains("/api/edge/uploads/u_1"))
                {
                    return FakeResponse.Ok(ProgressJson(chunkSize, received, total));
                }
                if (req.Method == "PUT")
                {
                    putUrls.Add(req.Url);
                    return FakeResponse.Ok(ProgressJson(chunkSize, 0, total));
                }
                if (req.Method == "POST" && req.Url.EndsWith("/complete"))
                {
                    return CompleteResponse(total);
                }
                return FakeResponse.Error(404, AndXContract.ErrorCodes.NotFound, "no route");
            });
        }

        [Fact]
        public async Task Upload_small_file_uses_one_shot()
        {
            var putUrls = new List<string>();
            var transport = Router(8, 0, 8, putUrls);
            Configure(transport);

            var result = await Share.UploadAsync(
                new TestPayload(new byte[8]), new UploadOptions { ExhibitId = "1024", Title = "截图" });

            Assert.Equal("tok", result.Token);
            Assert.Equal("88", result.ResourceId);
            Assert.Equal("https://edge.example.com/api/resources/tok/qrcode.png", result.QrImageUrl);
            Assert.Empty(putUrls);

            var request = transport.Requests.Single();
            Assert.Equal("POST", request.Method);
            Assert.StartsWith("https://edge.example.com/api/edge/resources?", request.Url);
            Assert.Contains("exhibitId=1024", request.Url);
            Assert.Contains("mediaType=image", request.Url);
            Assert.DoesNotContain("amount", request.Url); // 定价不由端侧决定
            Assert.True(request.HasEdgeKey);
            Assert.Equal("application/octet-stream", request.ContentType);
        }

        [Fact]
        public async Task Upload_large_file_sends_all_chunks_in_order()
        {
            var putUrls = new List<string>();
            var transport = Router(8, 0, 20, putUrls);
            Configure(transport);

            var result = await Share.UploadAsync(
                new TestPayload(new byte[20]), new UploadOptions { ExhibitId = "1024" });

            Assert.Equal("tok", result.Token);
            Assert.Equal(20, result.SizeBytes);
            Assert.Equal(new[] { 0L, 8L, 16L }, putUrls.Select(OffsetOf).ToArray());
        }

        [Fact]
        public async Task Upload_resumes_from_aligned_offset()
        {
            var putUrls = new List<string>();
            // received=10 → 丢弃半片，从 offset=8 续传
            var transport = Router(8, 10, 20, putUrls);
            Configure(transport);

            await Share.UploadAsync(new TestPayload(new byte[20]), new UploadOptions { ExhibitId = "1024" });

            Assert.Equal(new[] { 8L, 16L }, putUrls.Select(OffsetOf).ToArray());
        }

        [Fact]
        public async Task Upload_chunks_carry_edge_key_and_binary_content_type()
        {
            var putUrls = new List<string>();
            var transport = Router(8, 0, 16, putUrls);
            Configure(transport);

            await Share.UploadAsync(new TestPayload(new byte[16]), new UploadOptions { ExhibitId = "1024" });

            Assert.All(transport.Requests, r => Assert.True(r.HasEdgeKey));
            var puts = transport.Requests.Where(r => r.Method == "PUT").ToList();
            Assert.Equal(2, puts.Count);
            Assert.All(puts, r => Assert.Equal("application/octet-stream", r.ContentType));
        }

        [Fact]
        public async Task Upload_falls_back_to_chunked_when_one_shot_rejected()
        {
            var putUrls = new List<string>();
            var transport = Router(8, 0, 8, putUrls, rejectOneShot: true);
            Configure(transport);

            var result = await Share.UploadAsync(new TestPayload(new byte[8]), new UploadOptions { ExhibitId = "1024" });

            Assert.Equal("tok", result.Token);
            Assert.Equal(new[] { 0L }, putUrls.Select(OffsetOf).ToArray());
        }

        [Fact]
        public async Task Upload_requires_exhibit_id()
        {
            Configure(new FakeTransport((req, i) => FakeResponse.Ok("{}")));
            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Share.UploadAsync(new TestPayload(new byte[4]), new UploadOptions()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public async Task Upload_before_init_throws_configuration()
        {
            var ex = await Assert.ThrowsAsync<AndXException>(
                () => Share.UploadAsync(new TestPayload(new byte[4]), new UploadOptions { ExhibitId = "1" }));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, ex.Code);
        }

        [Fact]
        public async Task Abort_issues_delete()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok("{\"aborted\":true}"));
            Configure(transport);

            await Share.AbortAsync("u_9");

            Assert.Equal("DELETE", transport.Requests.Single().Method);
            Assert.EndsWith("/api/edge/uploads/u_9", transport.Requests.Single().Url);
        }

        [Fact]
        public async Task Resolve_and_download_hit_public_paths_without_edge_key()
        {
            var transport = new FakeTransport((req, i) =>
                req.Url.Contains("/download")
                    ? FakeResponse.Ok("{\"url\":\"https://minio/x\",\"expiresIn\":600,\"type\":\"image\",\"title\":\"t\"}")
                    : FakeResponse.Ok("{\"purpose\":\"RESOURCE_DOWNLOAD\",\"token\":\"tok\",\"entitled\":true}"));
            Configure(transport);

            var scan = await Share.ResolveAsync("tok");
            var download = await Share.GetDownloadAsync("tok");

            Assert.True(scan.Entitled);
            Assert.Equal("https://minio/x", download.Url);
            Assert.All(transport.Requests, r => Assert.False(r.HasEdgeKey));
        }

        private static string ProgressJson(long chunkSize, long received, long total)
        {
            return "{\"uploadId\":\"u_1\",\"received\":" + received + ",\"chunkSize\":" + chunkSize
                + ",\"sizeBytes\":" + total + "}";
        }

        private static TransportResponse CompleteResponse(long total)
        {
            return FakeResponse.Ok("{\"resourceId\":\"88\",\"token\":\"tok\",\"shareUrl\":\"https://edge.example.com/r/tok\","
                + "\"purpose\":\"RESOURCE_DOWNLOAD\",\"amount\":0,\"sizeBytes\":" + total
                + ",\"qrPngUrl\":\"/api/resources/tok/qrcode.png\"}");
        }

        private static long OffsetOf(string url)
        {
            const string marker = "offset=";
            var at = url.IndexOf(marker, StringComparison.Ordinal);
            return long.Parse(url.Substring(at + marker.Length));
        }
    }
}
