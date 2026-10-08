using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>
    /// 资源分享能力（静态入口）：上传素材、签发票据、扫码解析、下载授权、二维码。
    /// 上传对调用方透明：小文件一次性透传，大文件分片流式续传（均由 SDK 自动分流）。
    /// 定价不由端侧决定，由服务端/后台配置。
    /// </summary>
    public static class Share
    {
        /// <summary>签发资源下载票据（Edge 控制面，X-Edge-Key）；返回票据与二维码。</summary>
        public static async Task<IssuedTicket> IssueResourceTicketAsync(string mediaId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(mediaId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "mediaId 不能为空");
            }
            var api = Config.Api;
            var data = await api.PostRawAsync(
                AndXContract.Paths.EdgeTickets,
                new IssueTicketRequest { Purpose = AndXContract.Purposes.ResourceDownload, MediaId = mediaId },
                cancellationToken).ConfigureAwait(false);
            var r = data.ToObject<IssueTicketResponse>(AndXJson.Serializer);
            return new IssuedTicket
            {
                Token = r.Token,
                Purpose = r.Purpose,
                ShareUrl = r.ShareUrl,
                Amount = r.Amount,
                ExpireAt = r.ExpireAt,
                QrImageUrl = string.IsNullOrEmpty(r.QrcodePath) ? null : api.ResolveUrl(r.QrcodePath),
            };
        }

        /// <summary>
        /// 上传素材：小文件一次性透传、大文件分片流式续传与续传对齐，完成即签发票据。
        /// </summary>
        public static async Task<ShareResult> UploadAsync(
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
            if (total <= 0)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "payload.Length 必须大于 0（不支持空文件）");
            }

            var api = Config.Api;
            var chunkSize = Config.ChunkSize;
            var mediaType = options.MediaType ?? payload.MediaType;

            // 小文件走 Edge 一次性透传（少两次往返）；服务端上限更小时回退分片
            if (total <= chunkSize)
            {
                try
                {
                    return await UploadOneShotAsync(api, payload, options, mediaType, total, progress, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (AndXException ex) when (ex.HttpStatus == 413)
                {
                    // 服务端小文件上限更小：回退分片上传
                }
            }

            var create = await api.PostAsync<CreateUploadResponse>(
                AndXContract.Paths.EdgeUploads,
                new CreateUploadRequest
                {
                    ExhibitId = options.ExhibitId,
                    MediaType = MediaTypeToWire(mediaType),
                    FileName = payload.FileName,
                    SizeBytes = total,
                    Title = options.Title,
                },
                cancellationToken).ConfigureAwait(false);

            var serverChunkSize = create.ChunkSize > 0 ? create.ChunkSize : chunkSize;
            var uploadId = create.UploadId;
            var uploadPath = AndXContract.Paths.EdgeUploads + "/" + uploadId;

            // 续传探测：以 MinIO 已接收字节为准
            var resume = await api.GetAsync<UploadProgressResponse>(uploadPath, cancellationToken).ConfigureAwait(false);
            var offset = ChunkPlanner.AlignedResumeOffset(resume.Received, serverChunkSize);
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
                        var length = ChunkPlanner.ChunkLength(total, offset, serverChunkSize);
                        if (length <= 0)
                        {
                            break;
                        }
                        var buffer = await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
                        if (buffer.Length == 0)
                        {
                            break;
                        }
                        var chunkUrl = api.ResolveUrl(uploadPath + "?offset=" + offset);
                        var currentOffset = offset;
                        var reporter = progress == null
                            ? null
                            : new Progress<long>(sent => progress.Report(new UploadProgress(currentOffset + sent, total)));
                        await api.PutChunkAsync(chunkUrl, buffer, reporter, cancellationToken).ConfigureAwait(false);
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

            // 失败不自动中止：保留会话以便续传重试；调用方可在确认放弃时 AbortAsync
            var done = await api.PostAsync<CompleteUploadResponse>(uploadPath + "/complete", null, cancellationToken).ConfigureAwait(false);
            if (progress != null)
            {
                progress.Report(new UploadProgress(done.SizeBytes > 0 ? done.SizeBytes : total, total));
            }
            return MapShareResult(api, done);
        }

        /// <summary>中止上传会话（幂等清理）。</summary>
        public static Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(uploadId))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "uploadId 不能为空");
            }
            return Config.Api.DeleteAsync(AndXContract.Paths.EdgeUploads + "/" + uploadId, cancellationToken);
        }

        /// <summary>扫码解析（公开，可选登录）：返回预览 + 定价 + 是否已购。</summary>
        public static Task<ScanResolveResult> ResolveAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "token 不能为空");
            }
            return Config.Api.GetAsync<ScanResolveResult>(AndXContract.Paths.Scan + "/" + token, cancellationToken);
        }

        /// <summary>下载授权（需登录）：返回 presigned 下载地址。</summary>
        public static Task<DownloadResult> GetDownloadAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "token 不能为空");
            }
            return Config.Api.GetAsync<DownloadResult>(AndXContract.Paths.ResourceDownload(token), cancellationToken);
        }

        /// <summary>二维码 PNG 绝对地址（服务端出图）。</summary>
        public static string QrImageUrl(string token)
        {
            return Config.Api.ResolveUrl(AndXContract.Paths.ResourceQrcode(token));
        }

        /// <summary>下载二维码 PNG 原始字节（服务端出图）。</summary>
        public static Task<byte[]> GetQrPngAsync(string token, CancellationToken cancellationToken = default)
        {
            return Config.Api.GetBytesAsync(AndXContract.Paths.ResourceQrcode(token), cancellationToken);
        }

        private static string MediaTypeToWire(MediaType type)
        {
            return type == MediaType.Video ? "video" : "image";
        }

        /// <summary>一次性透传：读全量到内存（≤ chunkSize）→ POST /api/edge/resources（原始二进制 + query）。</summary>
        private static async Task<ShareResult> UploadOneShotAsync(
            AndXApiClient api,
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
            var data = await api.PostBinaryAsync(path, bytes, reporter, cancellationToken).ConfigureAwait(false);
            var done = data.ToObject<CompleteUploadResponse>(AndXJson.Serializer);
            if (progress != null)
            {
                progress.Report(new UploadProgress(done.SizeBytes > 0 ? done.SizeBytes : total, total));
            }
            return MapShareResult(api, done);
        }

        private static ShareResult MapShareResult(AndXApiClient api, CompleteUploadResponse done)
        {
            return new ShareResult
            {
                ResourceId = done.ResourceId,
                Token = done.Token,
                Purpose = done.Purpose,
                ShareUrl = done.ShareUrl,
                Amount = done.Amount,
                SizeBytes = done.SizeBytes,
                QrImageUrl = string.IsNullOrEmpty(done.QrPngUrl) ? null : api.ResolveUrl(done.QrPngUrl),
            };
        }

        private static string BuildResourceQuery(UploadOptions options, MediaType mediaType, string fileName)
        {
            return "?exhibitId=" + Uri.EscapeDataString(options.ExhibitId)
                + "&mediaType=" + MediaTypeToWire(mediaType)
                + "&fileName=" + Uri.EscapeDataString(fileName ?? "file")
                + (string.IsNullOrEmpty(options.Title) ? string.Empty : "&title=" + Uri.EscapeDataString(options.Title));
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
