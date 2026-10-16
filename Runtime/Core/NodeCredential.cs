using System;

namespace AndX
{
    /// <summary>
    /// 节点身份凭据（solution §16.2）：节点 ID + 节点密钥（HMAC）。
    ///
    /// 每台物理机一条、**可单独吊销**；服务端据此判定租户与展项，并把调用与产物
    /// 审计到「节点 + 展项」（`audit_request` / `media.device_id` / `ai_job.device_id`）。
    /// 凭据的落盘与保护由 <see cref="Core.NodeCredentialStore"/> 负责，本类型只持有内存态。
    /// </summary>
    public sealed class NodeCredential
    {
        public NodeCredential(string nodeId, string secret)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "NodeCredential.NodeId 不能为空");
            }
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new AndXException(Core.AndXContract.SdkErrorCodes.Configuration, "NodeCredential.Secret 不能为空");
            }
            NodeId = nodeId.Trim();
            Secret = secret;
        }

        /// <summary>节点标识（展馆内硬件编号，全局唯一）。</summary>
        public string NodeId { get; }

        /// <summary>节点密钥（HMAC）。**严禁写入日志、异常消息或上传**。</summary>
        public string Secret { get; }

        /// <summary>密钥末 4 位：仅用于人工核对（与服务端 `secretHint` 同口径）。</summary>
        public string SecretHint
        {
            get { return Secret.Length <= 4 ? Secret : Secret.Substring(Secret.Length - 4); }
        }

        /// <summary>脱敏输出：密钥一律 ****（规范 R9.2：密钥不入日志）。</summary>
        public override string ToString()
        {
            return "NodeCredential(node=" + NodeId + ", hint=****" + SecretHint + ")";
        }
    }
}