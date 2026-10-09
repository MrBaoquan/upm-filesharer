using System;
using System.Collections.Generic;

namespace AndX.Core
{
    /// <summary>与服务端线格式一一对应的内部 DTO（仅 core 内部使用）。</summary>
    internal sealed class IssueTicketRequest
    {
        public string Purpose { get; set; }
        public string ExhibitId { get; set; }
        public string MediaId { get; set; }
    }

    internal sealed class IssueTicketResponse
    {
        public string Token { get; set; }
        public string Purpose { get; set; }
        public string ShareUrl { get; set; }
        public string QrcodePath { get; set; }
        public long Amount { get; set; }
        public DateTimeOffset? ExpireAt { get; set; }
    }

    internal sealed class CreateUploadRequest
    {
        public string ExhibitId { get; set; }
        public string MediaType { get; set; }
        public string FileName { get; set; }
        public long SizeBytes { get; set; }
        public string Title { get; set; }
    }

    internal sealed class CreateUploadResponse
    {
        public string UploadId { get; set; }
        public long ChunkSize { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    internal sealed class UploadProgressResponse
    {
        public string UploadId { get; set; }
        public long Received { get; set; }
        public long ChunkSize { get; set; }
        public long SizeBytes { get; set; }
    }

    internal sealed class CompleteUploadResponse
    {
        public string ResourceId { get; set; }
        public string Token { get; set; }
        public string ShareUrl { get; set; }
        public string Purpose { get; set; }
        public long Amount { get; set; }
        public long SizeBytes { get; set; }
        public string QrPngUrl { get; set; }
    }

    internal sealed class OrderStatusResponse
    {
        public string OrderNo { get; set; }
        public string Status { get; set; }
        public long Amount { get; set; }
        public DateTimeOffset? PayTime { get; set; }
    }

    /// <summary>提交 AI 生成任务入参（线格式；camelCase 由 AndXJson 统一处理）。</summary>
    internal sealed class CreateAIJobRequest
    {
        public string Capability { get; set; }
        public string Prompt { get; set; }
        public string ExhibitId { get; set; }
        public IDictionary<string, object> Input { get; set; }
        public AIImageOptions Options { get; set; }
        public string IdemKey { get; set; }
    }

    /// <summary>生图参数（仅透传，端侧不定价、不选供应商）。</summary>
    internal sealed class AIImageOptions
    {
        public string Size { get; set; }
        public int? N { get; set; }
    }
}
