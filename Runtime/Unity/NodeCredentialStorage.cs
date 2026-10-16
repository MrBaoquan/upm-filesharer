using System.IO;
using AndX.Core;
using UnityEngine;

namespace AndX.Unity
{
    /// <summary>
    /// Unity 侧凭据落盘位置：<c>Application.persistentDataPath/andx/node-credential.json</c>。
    ///
    /// 保护器（<see cref="ICredentialProtector"/>）默认是**仅 Base64** 的直通实现——它不提供
    /// 机密性，只用于联调。生产请注入平台实现（Windows DPAPI / Android Keystore），
    /// 见 `Documentation~/usage.md`「节点身份与凭据落盘」。
    /// </summary>
    public static class NodeCredentialStorage
    {
        /// <summary>相对 persistentDataPath 的凭据文件路径。</summary>
        public const string RelativePath = "andx/node-credential.json";

        /// <summary>创建基于 persistentDataPath 的凭据存储。</summary>
        public static NodeCredentialStore Create(ICredentialProtector protector = null)
        {
            var path = Path.Combine(Application.persistentDataPath, "andx", "node-credential.json");
            return new NodeCredentialStore(path, protector);
        }
    }
}