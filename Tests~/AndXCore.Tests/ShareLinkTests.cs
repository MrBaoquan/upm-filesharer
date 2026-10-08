using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    public class ShareLinkTests
    {
        [Theory]
        [InlineData("RESOURCE_DOWNLOAD", "/r")]
        [InlineData("EXHIBIT_PAY", "/p")]
        [InlineData("SOMETHING_ELSE", "/r")]
        public void PathForPurpose_matches_spec(string purpose, string expected)
        {
            Assert.Equal(expected, ShareLink.PathForPurpose(purpose));
        }

        [Fact]
        public void BuildShareUrl_trims_trailing_slash()
        {
            Assert.Equal("https://a.com/r/abc123", ShareLink.BuildShareUrl("https://a.com/", "RESOURCE_DOWNLOAD", "abc123"));
        }

        [Fact]
        public void BuildShareUrl_uses_pay_path_for_exhibit_pay()
        {
            Assert.Equal("https://a.com/p/abc123", ShareLink.BuildShareUrl("https://a.com", "EXHIBIT_PAY", "abc123"));
        }

        [Theory]
        [InlineData("https://a.com/r/abcDEF12", "abcDEF12")]
        [InlineData("https://a.com/p/xyz789?q=1", "xyz789")]
        [InlineData("prefix /r/abcd#frag", "abcd")]
        public void ExtractToken_from_q(string q, string expected)
        {
            Assert.Equal(expected, ShareLink.ExtractToken(q: q));
        }

        [Fact]
        public void ExtractToken_decodes_encoded_q()
        {
            Assert.Equal("abcDEF12", ShareLink.ExtractToken(q: "https%3A%2F%2Fa.com%2Fr%2FabcDEF12"));
        }

        [Fact]
        public void ExtractToken_falls_back_to_scene()
        {
            Assert.Equal("abcd", ShareLink.ExtractToken(scene: "abcd"));
        }

        [Fact]
        public void ExtractToken_returns_null_for_garbage()
        {
            Assert.Null(ShareLink.ExtractToken(q: "not a share link"));
        }
    }
}
