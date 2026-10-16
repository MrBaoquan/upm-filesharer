using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AndX.Core
{
    /// <summary>
    /// 节点请求签名（solution §16.3 / implementation §13.3；服务端 `device-crypto.service.ts` 的镜像实现）。
    ///
    /// 签名原文（**v2**，逐字节与服务端一致）：
    /// <code>canonical = [METHOD, PATH?sortedQuery, TIMESTAMP(ms), NONCE, SHA256-HEX(body)].join('\n')</code>
    /// 其中 <c>PATH?sortedQuery</c> 含 <c>/api</c> 前缀，查询串按 key 升序（key 相同按 value 升序）、
    /// <b>不做百分号解码</b>、无参数时省略 <c>?</c>。签名 = HMAC-SHA256(canonical, 节点密钥) 的
    /// **小写 hex**，随四个头下发：<c>x-device-id</c> / <c>x-device-timestamp</c> /
    /// <c>x-device-nonce</c> / <c>x-device-signature</c>。
    ///
    /// v1 → v2：把查询串纳入签名，堵住「中间人改 query 即换展项」（`exhibitId` 等归属字段原不在保护范围内）；
    /// 无 query 时两者结果完全一致。三处同源，必须同时改：本类 / 服务端 / 契约测试向量。
    /// </summary>
    public static class DeviceSigner
    {
        /// <summary>组装签名原文（内部对 PATH 做查询串规范化，故传原样 `path?query` 即可）。</summary>
        public static string BuildCanonical(string method, string path, string timestamp, string nonce, byte[] body)
        {
            return BuildCanonicalFromBodyHash(method, path, timestamp, nonce, Sha256Hex(body));
        }

        /// <summary>
        /// 用**外部给定的 body 哈希**组装签名原文（服务端 `buildCanonicalFromBodyHash` 的镜像）。
        /// 流式端点（octet-stream）服务端不缓冲 body，故客户端在 `x-device-body-sha256` 头下发摘要，
        /// 两端用同一个值参与签名。
        /// </summary>
        public static string BuildCanonicalFromBodyHash(
            string method,
            string path,
            string timestamp,
            string nonce,
            string bodyHashHex)
        {
            return string.Join(
                "\n",
                string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant(),
                // 内部同样做规范化（与服务端 buildCanonical 对称；对已规范化的输入幂等）
                Canonicalize(path),
                timestamp ?? string.Empty,
                nonce ?? string.Empty,
                bodyHashHex ?? string.Empty);
        }

        /// <summary>请求体 SHA-256（小写 hex；空体为固定空串哈希）。</summary>
        public static string Sha256Hex(byte[] body)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(body ?? new byte[0]));
            }
        }

        /// <summary>HMAC-SHA256 签名（小写 hex）。</summary>
        public static string Sign(string secret, string canonical)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty)))
            {
                return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical ?? string.Empty)));
            }
        }

        /// <summary>一次性 nonce（32 位 hex；服务端按节点维度去重，默认 600s 内不可重复）。</summary>
        public static string NewNonce()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>时间戳（Unix 毫秒，十进制字符串；服务端允许 ±5 分钟时钟偏差）。</summary>
        public static string NowMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 从绝对/相对地址取「路径 + 规范化查询串」（**v2**：query 也参与签名）。
        /// 只做纯文本切分，不经 <c>Uri</c> 归一化 —— 保证与服务端看到的 originalUrl 逐字节一致。
        /// </summary>
        public static string BuildSignedPath(string url)
        {
            var value = url ?? string.Empty;
            var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                var pathStart = value.IndexOf('/', schemeIndex + 3);
                value = pathStart < 0 ? string.Empty : value.Substring(pathStart);
            }
            return Canonicalize(value);
        }

        /// <summary>
        /// 规范化查询串：按 key 升序（key 相同按 value 升序，UTF-16 ordinal）、`key=value` 用 `&` 连接、
        /// 不做百分号解码；空值参数渲染为 `key`（不带 `=`）；无参数时只返回路径。
        /// 必须与服务端 `DeviceCryptoService.canonicalizePath` 逐字节一致。
        /// </summary>
        public static string Canonicalize(string pathWithQuery)
        {
            if (string.IsNullOrEmpty(pathWithQuery))
            {
                return string.Empty;
            }
            var separator = pathWithQuery.IndexOf('?');
            if (separator < 0)
            {
                return pathWithQuery;
            }

            var path = pathWithQuery.Substring(0, separator);
            var pairs = new List<string[]>();
            foreach (var segment in pathWithQuery.Substring(separator + 1).Split('&'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }
                var eq = segment.IndexOf('=');
                pairs.Add(eq < 0
                    ? new[] { segment, string.Empty }
                    : new[] { segment.Substring(0, eq), segment.Substring(eq + 1) });
            }
            pairs.Sort((a, b) =>
            {
                var keyCompare = string.CompareOrdinal(a[0], b[0]);
                return keyCompare != 0 ? keyCompare : string.CompareOrdinal(a[1], b[1]);
            });

            var joined = string.Join("&", pairs.Select(pair => pair[1].Length == 0 ? pair[0] : pair[0] + "=" + pair[1]));
            return joined.Length > 0 ? path + "?" + joined : path;
        }

        /// <summary>为一次请求注入节点签名头（**每次重试都必须重新调用**：nonce 一次性，复用会被判重放）。</summary>
        public static void Apply(TransportRequest request, NodeCredential credential)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (credential == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "NodeCredential 不能为空");
            }

            var timestamp = NowMilliseconds();
            var nonce = NewNonce();
            // 流式端点（octet-stream）服务端不缓冲 body：显式下发 body 摘要（已纳入签名），
            // 服务端据此组装 canonical，并在读取字节时校验一致性（防「换 body 重放」）。
            var bodyHash = Sha256Hex(request.Body);
            var canonical = BuildCanonicalFromBodyHash(
                request.Method,
                BuildSignedPath(request.Url),
                timestamp,
                nonce,
                bodyHash);

            request.Headers[AndXContract.Headers.DeviceBodySha256] = bodyHash;
            request.Headers[AndXContract.Headers.DeviceId] = credential.NodeId;
            request.Headers[AndXContract.Headers.DeviceTimestamp] = timestamp;
            request.Headers[AndXContract.Headers.DeviceNonce] = nonce;
            request.Headers[AndXContract.Headers.DeviceSignature] = Sign(credential.Secret, canonical);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}