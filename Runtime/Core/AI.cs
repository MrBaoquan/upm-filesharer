using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX
{
    /// <summary>
    /// AI 能力（静态入口）：异步生成任务提交、状态查询、取消与等待。
    /// 经 AndXEdge 透明转发（服务端以 X-Edge-Key 鉴权）；端侧不接触 AI 供应商密钥、不参与定价。
    /// 成功产物的 <see cref="AIJob.MediaId"/> 可直接复用分享 / 二维码 / 下载 / 付费链路。
    /// </summary>
    public static class AI
    {
        /// <summary>能力清单与整体可用性（网关未配置时 <c>Available=false</c> 并回显 <c>Reason</c>，不抛错）。</summary>
        public static Task<AICapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            return Config.Api.GetAsync<AICapabilitiesResult>(AndXContract.Paths.AICapabilitiesPath, cancellationToken);
        }

        /// <summary>提交生图任务（最小参数）：仅需提示词，其余取默认。</summary>
        public static Task<AIJob> CreateImageJobAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return CreateImageJobAsync(new AIImageRequest { Prompt = prompt }, cancellationToken);
        }

        /// <summary>提交生图任务（异步）：返回 jobNo；产物完成后经 <see cref="GetJobAsync"/> 取回 mediaId。</summary>
        public static async Task<AIJob> CreateImageJobAsync(AIImageRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AIImageRequest 不能为空");
            }
            if (string.IsNullOrEmpty(request.Prompt))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AIImageRequest.Prompt 不能为空");
            }

            var exhibitId = Config.ResolveExhibitId(request.ExhibitId);
            var hasOptions = !string.IsNullOrEmpty(request.Size) || request.Count.HasValue;
            var dto = new CreateAIJobRequest
            {
                Capability = AndXContract.AICapabilities.ImageGenerate,
                Prompt = request.Prompt,
                ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                Input = request.Input,
                Options = hasOptions ? new AIImageOptions { Size = request.Size, Count = request.Count } : null,
                IdemKey = request.IdemKey,
            };
            var data = await Config.Api
                .PostRawAsync(AndXContract.Paths.AIJobsPath, dto, cancellationToken)
                .ConfigureAwait(false);
            var created = data.ToObject<AIJob>(AndXJson.Serializer) ?? new AIJob();
            // 提交响应只含 jobNo/status(/cached)；补全已知能力，其余字段待查询
            created.Capability = string.IsNullOrEmpty(created.Capability) ? dto.Capability : created.Capability;
            return created;
        }

        /// <summary>
        /// **单轮文字生成**（藏头诗 / 解签 / 祝福）：**同步返回**，不落任务队列、无需轮询、无产物文件。
        ///
        /// 与生图的差别：文字秒级返回，所以没有 jobNo、也不登记 media；但同样走
        /// 「能力门禁 + 限流 + 配额 + token 计量」，并且**输出校验由服务端做**
        /// （藏头诗会逐句校验首字，不合格自动带纠正提示重试一次；仍不合格按场景声明兜底或报错）。
        /// </summary>
        public static async Task<AITextResult> GenerateTextAsync(
            AITextRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "AITextRequest 不能为空");
            }
            if (string.IsNullOrEmpty(request.Scenario) && string.IsNullOrEmpty(request.Prompt))
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "缺少 Scenario 或 Prompt（场景模式给 Scenario + Params；原始接口模式直接给 Prompt）");
            }

            var exhibitId = Config.ResolveExhibitId(request.ExhibitId);
            var dto = new CreateAITextRequest
            {
                Capability = AndXContract.AICapabilities.TextGenerate,
                Scenario = string.IsNullOrEmpty(request.Scenario) ? null : request.Scenario,
                Params = request.Params,
                Prompt = string.IsNullOrEmpty(request.Prompt) ? null : request.Prompt,
                ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
            };
            var data = await Config.Api
                .PostRawAsync(AndXContract.Paths.AITextPath, dto, cancellationToken)
                .ConfigureAwait(false);
            return data.ToObject<AITextResult>(AndXJson.Serializer) ?? new AITextResult();
        }

        /// <summary>单轮文字生成（便捷入口）：给场景 + 槽位即可；也可不传场景、只给 prompt。</summary>
        public static Task<AITextResult> GenerateTextAsync(
            string scenario,
            System.Collections.Generic.IDictionary<string, object> parameters = null,
            string prompt = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            return GenerateTextAsync(
                new AITextRequest
                {
                    Scenario = scenario,
                    Params = parameters,
                    Prompt = prompt,
                    ExhibitId = exhibitId,
                },
                cancellationToken);
        }

        /// <summary>查询任务状态与结果（SUCCEEDED 时返回 mediaId / urls）。</summary>
        public static Task<AIJob> GetJobAsync(string jobNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }
            return Config.Api.GetAsync<AIJob>(AndXContract.Paths.AIJobPath(jobNo), cancellationToken);
        }

        /// <summary>取消任务（仅 PENDING 生效；返回取消后的任务视图）。</summary>
        public static Task<AIJob> CancelJobAsync(string jobNo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }
            return Config.Api.PostAsync<AIJob>(AndXContract.Paths.AIJobCancelPath(jobNo), null, cancellationToken);
        }

        /// <summary>
        /// 轮询直到任务进入终态；到终态返回结果，超时抛 <c>TIMEOUT</c>，取消抛 <c>CANCELED</c>。
        /// </summary>
        public static async Task<AIJob> WaitForJobAsync(
            string jobNo,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobNo))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "jobNo 不能为空");
            }

            var options = waitOptions ?? new AIWaitOptions();
            var interval = options.Interval > TimeSpan.Zero ? options.Interval : TimeSpan.FromSeconds(2);
            var timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(120);
            var deadline = DateTime.UtcNow + timeout;

            var job = await GetJobAsync(jobNo, cancellationToken).ConfigureAwait(false);
            progress?.Report(job);
            while (!job.IsTerminal)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new AndXException(
                        AndXContract.SdkErrorCodes.Timeout,
                        $"AI 任务 {jobNo} 等待超时（当前状态 {job.Status}）");
                }
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
        /// **场景化一步生图**：`scenario`（t2i / style / face_swap）+ `inputs`（如 images 的 data URL 数组）。
        ///
        /// 与原始接口的关系：底层仍是同一个 `ai.image.generate`，只是由服务端按场景补齐/校验
        /// 提示词、出图尺寸与比例（例如换脸按模板比例出图，避免变形）。**不传 scenario 时行为与原来完全一致。**
        /// </summary>
        public static Task<AIJob> GenerateScenarioAsync(
            string scenario,
            System.Collections.Generic.IDictionary<string, object> inputs = null,
            System.Collections.Generic.IDictionary<string, object> parameters = null,
            string exhibitId = null,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(scenario))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "scenario 不能为空");
            }
            return GenerateImageAsync(
                new AIImageRequest
                {
                    Scenario = scenario,
                    Input = inputs,
                    Params = parameters,
                    ExhibitId = exhibitId,
                },
                waitOptions,
                progress,
                cancellationToken);
        }

        /// <summary>一步生图（最小参数）：仅需提示词。</summary>
        public static Task<AIJob> GenerateImageAsync(
            string prompt,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
            CancellationToken cancellationToken = default)
        {
            return GenerateImageAsync(new AIImageRequest { Prompt = prompt }, waitOptions, progress, cancellationToken);
        }

        /// <summary>
        /// 一步生图：提交 + 轮询等待，返回终态任务。成功时用 <c>job.MediaId</c> / <c>job.ResultUrls</c>。
        /// 参数最小化：仅 <see cref="AIImageRequest.Prompt"/> 必填，其余可省（展项取全局默认）。
        /// </summary>
        public static async Task<AIJob> GenerateImageAsync(
            AIImageRequest request,
            AIWaitOptions waitOptions = null,
            IProgress<AIJob> progress = null,
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
        /// <summary>
        /// 取第 index 张结果图的**原始字节**（快路径：直接用任务对象上已有的 presigned 地址）。
        /// presigned 链接有效期 10 分钟；不确定新鲜度或已过期时，用 <see cref="GetResultBytesAsync(string, int, CancellationToken)"/>。
        /// </summary>
        public static Task<byte[]> GetResultBytesAsync(AIJob job, int index = 0, CancellationToken cancellationToken = default)
        {
            var url = job == null ? null : job.ResultUrlAt(index);
            if (string.IsNullOrEmpty(url))
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "没有第 " + index + " 张结果图：任务 " + (job == null ? "(null)" : job.JobNo + " 状态 " + job.Status)
                    + "（未 SUCCEEDED 或 index 越界；当前共 " + (job == null ? 0 : job.ResultCount) + " 张）");
            }
            return Config.Api.GetBytesAsync(url, cancellationToken);
        }

        /// <summary>
        /// 取第 index 张结果图的原始字节（**新鲜路径**：先查任务拿新的 presigned 地址，再下载）。
        /// 屏幕长期展示、迟到下载、打印前都应走这个重载。
        /// </summary>
        public static async Task<byte[]> GetResultBytesAsync(string jobNo, int index = 0, CancellationToken cancellationToken = default)
        {
            var job = await GetJobAsync(jobNo, cancellationToken).ConfigureAwait(false);
            return await GetResultBytesAsync(job, index, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 把第 index 张结果图写成本地文件（目录不存在时自动创建），返回写入路径。
        /// 展项现场据此留存原图 / 打印 / 二次加工；目录由调用方给（Unity 侧默认见
        /// <c>AndX.Unity.AIResultLoader.SaveAsync</c>）。
        /// </summary>
        public static async Task<string> SaveResultAsync(AIJob job, int index, string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "filePath 不能为空（Unity 侧可传 Application.persistentDataPath 下的路径）");
            }
            var bytes = await GetResultBytesAsync(job, index, cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(filePath, bytes);
            return filePath;
        }

        /// <summary>同 <see cref="SaveResultAsync(AIJob, int, string, CancellationToken)"/>，但用 jobNo 先刷新任务再下载。</summary>
        public static async Task<string> SaveResultAsync(string jobNo, int index, string filePath, CancellationToken cancellationToken = default)
        {
            var job = await GetJobAsync(jobNo, cancellationToken).ConfigureAwait(false);
            return await SaveResultAsync(job, index, filePath, cancellationToken).ConfigureAwait(false);
        }
    }
}
