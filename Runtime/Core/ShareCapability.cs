using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX.Share
{
    /// <summary>
    /// 资源分享能力域：签发票据、上传（分片流式续传经 Edge）、扫码解析、下载授权、二维码地址。
    /// 上传对调用方透明：当前统一走 Edge 分片控制面（小文件为单片），后续增量接入一次性透传与直传。
    /// </summary>
    public sealed class ShareCapability : IAndXCapability
    {
        private readonly AndXApiClient _api;
        private readonly long _chunkSize;

        internal ShareCapability(AndXApiClient api, long chunkSize)
        {
            _api = api;
            _chunkSize = chunkSize > 0 ? chunkSize : 8L * 1024 * 1024;
        }

        /// <summary>签发票据（Edge 控制面，X-Edge-Key）。</summary>
        public async Task<IssuedTicket> IssueTicketAsync(string purpose, string exhibitId = null, string mediaId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(purpose))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "purpose 不能为空");
            }
            var data = await _api.PostRawAsync(
                AndXContract.Paths.EdgeTickets,
                new IssueTicketRequest { Purpose = purpose, ExhibitId = exhibitId, MediaId = mediaId },
                cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<IssueTicketResponse>(AndXJson.Serializer);
            return new IssuedTicket
            {
                Token = r.Token,
                Purpose = r.Purpose,
                ShareUrl = r.ShareUrl,
                Amount = r.Amount,
                ExpireAt = r.ExpireAt,
                QrImageUrl = string.IsNullOrEmpty(r.QrcodePath) ? null : _api.ResolveUrl(r.QrcodePath),
            };
        }

        /// <summary>
        /// 上传素材：登记分片会话 → 续传对齐 → 逐片流式透传 → 完成并签发票据。
        /// </summary>
        public async Task<ShareResult> UploadAsync(
            IAndXPayload payload,
            UploadOptions options,
            IProgress<UploadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (payload == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "payload 不能为空");
            }
            options = options ?? new UploadOptions();
            if (string.IsNullOrEmpty(options.ExhibitId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "UploadOptions.ExhibitId 不能为空");
            }
            var total = payload.Length;
            if (total < 0)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "payload.Length 必须已知且非负");
            }

            var mediaType = options.MediaType ?? payload.MediaType;

            // 小文件走 Edge 一次性透传（少两次往返）；服务端上限更小时回退分片
            if (total <= _chunkSize)
            {
                try
                {
                    return await UploadOneShotAsync(payload, options, mediaType, total, progress, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (AndXException ex) when (ex.HttpStatus == 413)
                {
                    // 服务端小文件上限更小：回退分片上传
                }
            }

            var create = await _api.PostAsync<CreateUploadResponse>(
                AndXContract.Paths.EdgeUploads,
                new CreateUploadRequest
                {
                    ExhibitId = options.ExhibitId,
                    MediaType = MediaTypeToWire(mediaType),
                    FileName = payload.FileName,
                    SizeBytes = total,
                    Title = options.Title,
                    Amount = options.Amount,
                },
                cancellationToken).ConfigureAwait(false);

            var chunkSize = create.ChunkSize > 0 ? create.ChunkSize : _chunkSize;
            var uploadId = create.UploadId;
            var uploadPath = AndXContract.Paths.EdgeUploads + "/" + uploadId;

            try
            {
                // 续传探测：以 MinIO 已接收字节为准
                var resume = await _api.GetAsync<UploadProgressResponse>(uploadPath, cancellationToken).ConfigureAwait(false);
                var offset = ChunkPlanner.AlignedResumeOffset(resume.Received, chunkSize);
                progress?.Report(new UploadProgress(offset, total));

                if (offset < total)
                {
                    using (var stream = await payload.OpenReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (offset > 0)
                        {
                            Skip(stream, offset);
                        }
                        while (offset < total)
                        {
                            var length = ChunkPlanner.ChunkLength(total, offset, chunkSize);
                            if (length <= 0)
                            {
                                break;
                            }
                            var buffer = await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
                            if (buffer.Length == 0)
                            {
                                break;
                            }
                            var chunkUrl = _api.ResolveUrl(uploadPath + "?offset=" + offset);
                            var currentOffset = offset;
                            var reporter = progress == null
                                ? null
                                : new Progress<long>(sent => progress.Report(new UploadProgress(currentOffset + sent, total)));
                            await _api.PutChunkAsync(chunkUrl, buffer, reporter, cancellationToken).ConfigureAwait(false);
                            offset += buffer.Length;
                            if (progress != null)
                            {
                                var snapshot = offset;
                                progress.Report(new UploadProgress(snapshot, total));
                            }
                            if (buffer.Length < length)
                            {
                                break; // 流提前结束：交由 complete 校验字节数
                            }
                        }
                    }
                }

                var done = await _api.PostAsync<CompleteUploadResponse>(uploadPath + "/complete", null, cancellationToken).ConfigureAwait(false);
                if (progress != null)
                {
                    progress.Report(new UploadProgress(done.SizeBytes > 0 ? done.SizeBytes : total, total));
                }
                return MapShareResult(done);
            }
            catch
            {
                // 失败不自动中止：保留会话以便续传重试；调用方可在确认放弃时 AbortAsync
                throw;
            }
        }

        /// <summary>中止上传会话（幂等清理）。</summary>
        public Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(uploadId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "uploadId 不能为空");
            }
            return _api.DeleteAsync(AndXContract.Paths.EdgeUploads + "/" + uploadId, cancellationToken);
        }

        /// <summary>扫码解析（公开，可选登录）：返回预览 + 定价 + 是否已购。</summary>
        public Task<ScanResolveResult> ResolveAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "token 不能为空");
            }
            return _api.GetAsync<ScanResolveResult>(AndXContract.Paths.Scan + "/" + token, cancellationToken);
        }

        /// <summary>下载授权（需登录）：返回 presigned 下载地址。</summary>
        public Task<DownloadResult> GetDownloadAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "token 不能为空");
            }
            return _api.GetAsync<DownloadResult>(AndXContract.Paths.ResourceDownload(token), cancellationToken);
        }

        /// <summary>二维码 PNG 绝对地址（服务端出图）。</summary>
        public string QrImageUrl(string token)
        {
            return _api.ResolveUrl(AndXContract.Paths.ResourceQrcode(token));
        }

        /// <summary>下载二维码 PNG 原始字节（服务端出图）。</summary>
        public Task<byte[]> GetQrPngAsync(string token, CancellationToken cancellationToken = default)
        {
            return _api.GetBytesAsync(AndXContract.Paths.ResourceQrcode(token), cancellationToken);
        }

        private static string MediaTypeToWire(MediaType type)
        {
            return type == MediaType.Video ? "video" : "image";
        }

        /// <summary>一次性透传：读全量到内存（≤ chunkSize）→ POST /api/edge/resources（原始二进制 + query）。</summary>
        private async Task<ShareResult> UploadOneShotAsync(
            IAndXPayload payload,
            UploadOptions options,
            MediaType mediaType,
            long total,
            IProgress<UploadProgress> progress,
            CancellationToken cancellationToken)
        {
            byte[] bytes;
            using (var stream = await payload.OpenReadAsync(cancellationToken).ConfigureAwait(false))
            {
                bytes = await ReadAllAsync(stream, total, cancellationToken).ConfigureAwait(false);
            }

            var reporter = progress == null
                ? null
                : new Progress<long>(sent => progress.Report(new UploadProgress(sent, total)));
            var path = AndXContract.Paths.EdgeResources + BuildResourceQuery(options, mediaType, payload.FileName);
            var data = await _api.PostBinaryAsync(path, bytes, reporter, cancellationToken).ConfigureAwait(false);
            var done = data.ToObject<CompleteUploadResponse>(AndXJson.Serializer);
            if (progress != null)
            {
                progress.Report(new UploadProgress(done.SizeBytes > 0 ? done.SizeBytes : total, total));
            }
            return MapShareResult(done);
        }

        private ShareResult MapShareResult(CompleteUploadResponse done)
        {
            return new ShareResult
            {
                ResourceId = done.ResourceId,
                Token = done.Token,
                Purpose = done.Purpose,
                ShareUrl = done.ShareUrl,
                Amount = done.Amount,
                SizeBytes = done.SizeBytes,
                QrImageUrl = string.IsNullOrEmpty(done.QrPngUrl) ? null : _api.ResolveUrl(done.QrPngUrl),
            };
        }

        private static string BuildResourceQuery(UploadOptions options, MediaType mediaType, string fileName)
        {
            var parts = new List<string>
            {
                "exhibitId=" + Uri.EscapeDataString(options.ExhibitId),
                "mediaType=" + MediaTypeToWire(mediaType),
                "fileName=" + Uri.EscapeDataString(fileName ?? "file"),
            };
            if (!string.IsNullOrEmpty(options.Title))
            {
                parts.Add("title=" + Uri.EscapeDataString(options.Title));
            }
            if (options.Amount > 0)
            {
                parts.Add("amount=" + options.Amount);
            }
            return "?" + string.Join("&", parts);
        }

        private static async Task<byte[]> ReadAllAsync(Stream stream, long total, CancellationToken cancellationToken)
        {
            if (total <= 0)
            {
                return Array.Empty<byte>();
            }
            if (total > int.MaxValue)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "一次性上传仅支持小文件；请改用分片路径");
            }
            var buffer = new byte[(int)total];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            if (read == buffer.Length)
            {
                return buffer;
            }
            var trimmed = new byte[read];
            Array.Copy(buffer, trimmed, read);
            return trimmed;
        }

        private static void Skip(Stream stream, long count)
        {
            if (stream.CanSeek)
            {
                stream.Seek(count, SeekOrigin.Begin);
                return;
            }
            var buffer = new byte[81920];
            var remaining = count;
            while (remaining > 0)
            {
                var want = (int)Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, want);
                if (read <= 0)
                {
                    break;
                }
                remaining -= read;
            }
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = await stream.ReadAsync(buffer, read, count - read, cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            if (read == count)
            {
                return buffer;
            }
            var trimmed = new byte[read];
            Array.Copy(buffer, trimmed, read);
            return trimmed;
        }
    }
}
