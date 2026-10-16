namespace AndX
{
    /// <summary>
    /// 接入方式（solution §16）：插件有两种接入形态，**业务调用完全一致**
    /// （同一套 <c>AndX.Share.*</c> / <c>AndX.Pay.*</c> / <c>AndX.AI.*</c>），
    /// 差异只在「凭据归谁 / 展项身份从哪来 / 服务端审计口径」。
    ///
    /// | 模式 | 凭据 | 展项身份 | 服务端审计 |
    /// |------|------|----------|-----------|
    /// | <see cref="Direct"/> | 插件持本机节点凭据（HMAC，每机一把、可单独吊销） | 服务端按节点绑定裁决 | 精确到「节点 + 展项」 |
    /// | <see cref="Edge"/> | 由本机 AndXEdge 代持，插件不持密钥 | Edge 按角色（local/relay/hub）注入 | 「边缘节点 + 展项」 |
    /// </summary>
    public enum AndXAccessMode
    {
        /// <summary>
        /// **直连 AndX 云服务（默认）**：插件直接访问后端基址，持本机节点凭据（<see cref="NodeCredential"/>），
        /// 服务端据此判定租户/展项并审计到「节点 + 展项」。适用于展项机能直达后端（同内网，或能出外网）的现场。
        /// </summary>
        Direct = 0,

        /// <summary>
        /// **经本机 AndXEdge**：插件只访问环回地址，鉴权 / 展项标识 / 公网地址由 Edge 代持。
        /// 适用于展项无外网但局域网内有出口（Edge <c>Relay</c>）、或多个展项共用一个出口（Edge <c>Hub</c>）的现场。
        /// </summary>
        Edge = 1,

        /// <summary>
        /// **自动选择**：本机已有节点凭据（或提供了注册令牌）→ <see cref="Direct"/>；否则 <see cref="Edge"/>。
        /// 适合同一份包在「有凭据的展项机」与「无凭据的联调机」上都能直接跑起来。
        /// </summary>
        Auto = 2,
    }
}
