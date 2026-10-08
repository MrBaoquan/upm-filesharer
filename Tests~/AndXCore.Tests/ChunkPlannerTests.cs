using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class ChunkPlannerTests
    {
        [Theory]
        [InlineData(0, 8, 0)]
        [InlineData(8, 8, 8)]
        [InlineData(10, 8, 8)]
        [InlineData(16, 8, 16)]
        [InlineData(23, 8, 16)]
        public void AlignedResumeOffset_discards_partial_chunk(long received, long chunkSize, long expected)
        {
            Assert.Equal(expected, ChunkPlanner.AlignedResumeOffset(received, chunkSize));
        }

        [Theory]
        [InlineData(100, 0, 8, 8)]
        [InlineData(100, 96, 8, 4)]
        [InlineData(100, 100, 8, 0)]
        [InlineData(100, 104, 8, 0)]
        public void ChunkLength_clamps_to_remaining(long total, long offset, long chunkSize, int expected)
        {
            Assert.Equal(expected, ChunkPlanner.ChunkLength(total, offset, chunkSize));
        }

        [Theory]
        [InlineData(0, 8, 0)]
        [InlineData(1, 8, 1)]
        [InlineData(8, 8, 1)]
        [InlineData(9, 8, 2)]
        public void PartCount(long total, long chunkSize, long expected)
        {
            Assert.Equal(expected, ChunkPlanner.PartCount(total, chunkSize));
        }
    }
}
