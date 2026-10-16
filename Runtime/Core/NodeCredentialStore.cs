using System;
using System.IO;

namespace AndX.Core
{
    /// <summary>
    /// 节点凭据落盘（solution §16.2）：文件为 JSON，密钥字段经
    /// <see cref="ICredentialProtector"/> 保护后写入。
    /// </summary>
    public sealed class NodeCredentialStore
    {
        private sealed class StoredCredential
        {
            public string NodeId { get; set; }
            public string SecretProtected { get; set; }
            public string Protector { get; set; }
            public string SavedAt { get; set; }
        }

        private readonly ICredentialProtector _protector;

        public NodeCredentialStore(string filePath, ICredentialProtector protector = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "NodeCredentialStore.filePath 不能为空");
            }
            FilePath = filePath;
            _protector = protector ?? new PassthroughCredentialProtector();
        }

        /// <summary>凭据文件路径。</summary>
        public string FilePath { get; }

        /// <summary>密钥是否以加密形式落盘（false 表示仅 Base64，调用方应告警）。</summary>
        public bool IsEncryptedAtRest
        {
            get { return _protector.IsEncryptedAtRest; }
        }

        public bool Exists
        {
            get { return File.Exists(FilePath); }
        }

        /// <summary>保存凭据（覆盖）；目录不存在时自动创建。</summary>
        public void Save(NodeCredential credential)
        {
            if (credential == null)
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "credential 不能为空");
            }
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new StoredCredential
            {
                NodeId = credential.NodeId,
                SecretProtected = _protector.Protect(credential.Secret),
                Protector = _protector.GetType().Name,
                SavedAt = DateTimeOffset.UtcNow.ToString("o"),
            };
            File.WriteAllText(FilePath, AndXJson.Serialize(payload));
        }

        /// <summary>读取凭据；文件不存在或内容损坏时返回 null（不抛错，便于首启判定）。</summary>
        public NodeCredential Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            try
            {
                var stored = AndXJson.Deserialize<StoredCredential>(File.ReadAllText(FilePath));
                if (stored == null || string.IsNullOrEmpty(stored.NodeId) || string.IsNullOrEmpty(stored.SecretProtected))
                {
                    return null;
                }
                return new NodeCredential(stored.NodeId, _protector.Unprotect(stored.SecretProtected));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>删除凭据文件（存在时）。</summary>
        public void Clear()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }
}