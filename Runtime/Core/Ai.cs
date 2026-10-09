using System;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>
    /// AI 能力（静态入口）：异步生成任务提交、状态查询、取消与等待。
    /// 经 AndXEdge 透明转发（服务端以 X-Edge-Key 鉴权）；端侧不接触 AI 供应商密钥、不参与定价。
    /// 成功产物的 <see cref="AiJob.MediaId"/> 可直接复用分享 / 二维码 / 下载 / 付费链路。
    /// </summary>
    public static class Ai
    {
        /// <summary>能力清单与整体可用性（网关未配置时 <c>Available=false</c> 并回显 <c>Reason</c>，不抛错）。</summary>
        public static Task<AiCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            return Config.Api.GetAsync<AiCapabilitiesResult>(AndXContract.Paths.AiCapabilitiesPath, cancellationToken);
        }

        /// <summary>提交生图任务（异步）：返回 jobNo；产物完成后经 <see cref="GetJobAsync"/> 取回 mediaId。</summary>
        public static async Task<AiJob> CreateImageJobAsync(AiImageRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AiImageRequest 不能为空");
            }
            if (string.IsNullOrEmpty(request.Prompt))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AiImageRequest.Prompt 不能为空");
            }

            var exhibitId = Config.ResolveExhibitId(request.ExhibitId);
            var hasOptions = !string.IsNullOrEmpty(request.Size) || request.N.HasValue;
            var dto = new CreateAiJobRequest
            {
                Capability = AndXContract.AiCapabilities.ImageGenerate,
                Prompt = request.Prompt,
                ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                Input = request.Input,
                Options = hasOptions ? new AiImageOptions { Size = request.Size, N = request.N } : null,
                IdemKey = request.IdemKey,
            };
            var data = await Config.Api
                .PostRawAsync(AndXContract.Paths.AiJobsPath, dto, cancellationToken)
                .ConfigureAwait(false);
            var created = data.ToObject<AiJob>(AndXJson.Serializer) ?? new AiJob();
            // 提交响应只含 jobNo/status(/cached)；补全已知能力，其余字段待查询
            created.Capability = string.IsNullOrEmpty(created.Capability) ? dto.Capability : created.Capability;
            return created;
        }

        /// <summary>查询任务状态与结果（SUCCEEDED 时返回 mediaId / urls）。</summary>
        public static Task<AiJob> GetJobAsync(string jobNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }
            return Config.Api.GetAsync<AiJob>(AndXContract.Paths.AiJob(jobNo), cancellationToken);
        }

        /// <summary>取消任务（仅 PENDING 生效；返回取消后的任务视图）。</summary>
        public static Task<AiJob> CancelJobAsync(string jobNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }
            return Config.Api.PostAsync<AiJob>(AndXContract.Paths.AiJobCancel(jobNo), null, cancellationToken);
        }

        /// <summary>
        /// 轮询直到任务进入终态或超时；返回最后一次观测到的任务（超时可能仍为 PENDING/RUNNING）。
        /// </summary>
        public static async Task<AiJob> WaitForJobAsync(
            string jobNo,
            AiWaitOptions waitOptions = null,
            IProgress<AiJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }

            var options = waitOptions ?? new AiWaitOptions();
            var interval = options.Interval > TimeSpan.Zero ? options.Interval : TimeSpan.FromSeconds(2);
            var timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(120);
            var deadline = DateTime.UtcNow + timeout;

            var job = await GetJobAsync(jobNo, cancellationToken).ConfigureAwait(false);
            progress?.Report(job);
            while (!job.IsTerminal && DateTime.UtcNow < deadline)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new AndXException(AndXContract.SdkErrorCodes.Canceled, "请求已取消");
                }
                job = await GetJobAsync(jobNo, cancellationToken).ConfigureAwait(false);
                progress?.Report(job);
            }
            return job;
        }

        /// <summary>
        /// 一步生图：提交 + 轮询等待，返回终态任务。成功时用 <c>job.MediaId</c> / <c>job.Urls</c>。
        /// 参数最小化：仅 <see cref="AiImageRequest.Prompt"/> 必填，其余可省（展项取全局默认）。
        /// </summary>
        public static async Task<AiJob> GenerateImageAsync(
            AiImageRequest request,
            AiWaitOptions waitOptions = null,
            IProgress<AiJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            var created = await CreateImageJobAsync(request, cancellationToken).ConfigureAwait(false);
            if (created.IsTerminal)
            {
                // 同参幂等命中：直接复用的任务可能已 SUCCEEDED/FAILED，回查补全结果
                return await GetJobAsync(created.JobNo, cancellationToken).ConfigureAwait(false);
            }
            return await WaitForJobAsync(created.JobNo, waitOptions, progress, cancellationToken).ConfigureAwait(false);
        }
    }
}
