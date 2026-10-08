using System.Text.RegularExpressions;

namespace AndX.Core
{
    /// <summary>
    /// 普通链接二维码规则（镜像 <c>andx-sdk-spec/src/links.ts</c>）。
    /// 端侧不得各自拼链接：一律经此处生成 / 解析。
    /// </summary>
    public static class ShareLink
    {
        private static readonly Regex TokenPattern = new Regex("/(?:r|p)/([A-Za-z0-9]{4,24})(?:[/?#]|$)", RegexOptions.Compiled);

        private static readonly Regex BareTokenPattern = new Regex("^[A-Za-z0-9]{4,24}$", RegexOptions.Compiled);

        /// <summary>按票据用途返回路径前缀。</summary>
        public static string PathForPurpose(string purpose)
        {
            return string.Equals(purpose, AndXContract.Purposes.ExhibitPay, System.StringComparison.Ordinal)
                ? AndXContract.Paths.Pay
                : AndXContract.Paths.Share;
        }

        /// <summary>拼接二维码内容：{base}/r|p/{token}（base 不含尾斜杠）。</summary>
        public static string BuildShareUrl(string baseUrl, string purpose, string token)
        {
            var normalized = (baseUrl ?? string.Empty).TrimEnd('/');
            return normalized + PathForPurpose(purpose) + "/" + token;
        }

        /// <summary>
        /// 从微信扫码回调参数还原 token：优先解析 <paramref name="q"/>（需先 decodeURIComponent，
        /// 微信会以已解码或待解码形式传入，两种都兼容），兜底 <paramref name="scene"/> / <paramref name="token"/>。
        /// </summary>
        public static string ExtractToken(string q = null, string scene = null, string token = null)
        {
            if (!string.IsNullOrEmpty(q))
            {
                var decoded = SafeDecode(q);
                var match = TokenPattern.Match(decoded);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            if (!string.IsNullOrEmpty(token) && BareTokenPattern.IsMatch(token))
            {
                return token;
            }
            if (!string.IsNullOrEmpty(scene) && BareTokenPattern.IsMatch(scene))
            {
                return scene;
            }
            return null;
        }

        private static string SafeDecode(string value)
        {
            try
            {
                return System.Uri.UnescapeDataString(value);
            }
            catch
            {
                return value;
            }
        }
    }
}
