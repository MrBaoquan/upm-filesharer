using System;

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
        public long Amount { get; set; }
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
}
