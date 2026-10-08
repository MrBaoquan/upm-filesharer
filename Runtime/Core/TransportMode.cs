namespace AndX
{
    /// <summary>
    /// 共享核实现选择。native 核为后续增量，接入同一 <c>IAndXCore</c> 契约；
    /// 当前仅提供 managed 实现，<see cref="Auto"/> 与 <see cref="Native"/> 均回退到 managed。
    /// </summary>
    public enum TransportMode
    {
        /// <summary>有 native 用 native；WebGL / 无 native 时自动 managed。</summary>
        Auto = 0,

        /// <summary>强制托管实现（C#，WebGL 完整）。</summary>
        Managed = 1,

        /// <summary>强制 native 实现（未就绪时回退 managed）。</summary>
        Native = 2,
    }
}
