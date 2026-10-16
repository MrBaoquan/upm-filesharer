using System;
using System.Text;

namespace AndX.Core
{
    /// <summary>
    /// 节点密钥的**静态保护**扩展点（solution §16.2）：把明文密钥转成可落盘的形式。
    ///
    /// 包内只提供 <see cref="PassthroughCredentialProtector"/>（仅 Base64，**不是加密**）——
    /// 宿主的 API Compatibility Level 可能是 .NET Standard 2.0，此时 `ProtectedData`(DPAPI)
    /// 类型不可用，包内直接引用会让工程编译不过。生产部署应注入平台实现（见
    /// `Documentation~/usage.md`「节点身份与凭据落盘」一节的 Windows DPAPI / Android Keystore 示例）。
    /// </summary>
    public interface ICredentialProtector
    {
        /// <summary>保护明文密钥（返回值随凭据一起写入磁盘）。</summary>
        string Protect(string plaintext);

        /// <summary>还原明文密钥。</summary>
        string Unprotect(string protectedValue);

        /// <summary>是否为真正的加密保护（false 表示只防误读、不防提取，调用方应告警）。</summary>
        bool IsEncryptedAtRest { get; }
    }

    /// <summary>
    /// 直通保护器：只做 Base64 编码，**不提供任何机密性**。
    /// 仅用于本地联调与「宿主项目尚未接入平台密钥库」的过渡；此时 SDK 会给出告警。
    /// </summary>
    public sealed class PassthroughCredentialProtector : ICredentialProtector
    {
        public string Protect(string plaintext)
        {
            if (plaintext == null)
            {
                throw new ArgumentNullException(nameof(plaintext));
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        }

        public string Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue))
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }

        public bool IsEncryptedAtRest
        {
            get { return false; }
        }
    }
}