using System;

namespace AndX.Core
{
    /// <summary>分片上传的偏移与分片计算（纯函数，供 core 与测试复用）。</summary>
    public static class ChunkPlanner
    {
        /// <summary>
        /// 计算续传起始偏移：丢弃可能存在的「半片」，从其所在分片起点重传，
        /// 以满足服务端 offset 必须按分片大小对齐的约束（同分片重传天然幂等覆盖）。
        /// </summary>
        public static long AlignedResumeOffset(long received, long chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize 必须为正数");
            }
            if (received <= 0)
            {
                return 0;
            }
            return (received / chunkSize) * chunkSize;
        }

        /// <summary>当前分片应发送的字节数（末片可小于 chunkSize；越界返回 0）。</summary>
        public static int ChunkLength(long total, long offset, long chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize 必须为正数");
            }
            var remaining = total - offset;
            if (remaining <= 0)
            {
                return 0;
            }
            return (int)Math.Min(chunkSize, remaining);
        }

        /// <summary>总分片数。</summary>
        public static long PartCount(long total, long chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize 必须为正数");
            }
            if (total <= 0)
            {
                return 0;
            }
            return (total + chunkSize - 1) / chunkSize;
        }
    }
}
