using Parful.AndX;

/// <summary>
/// AndX 全局入口。业务代码通过 <c>AndX.Share</c> / <c>AndX.Pay</c> / <c>AndX.AI</c> 调用，
/// 配置由 <c>AndX.Config</c> 统一持有；安装 UNIHper 后由 <c>AndXUNIHperSettings</c> 自动完成初始化。
/// </summary>
public static class AndX
{
    /// <summary>全局配置与初始化入口。</summary>
    public static readonly Config Config = new Config();

    /// <summary>资源上传、扫码解析、下载与二维码服务。</summary>
    public static readonly Parful.AndX.Share Share = new Parful.AndX.Share();

    /// <summary>付费票据与订单查询服务。</summary>
    public static readonly Pay Pay = new Pay();

    /// <summary>AI 生图与文字生成服务。</summary>
    public static readonly Parful.AndX.AI AI = new Parful.AndX.AI();

    /// <summary>节点注册、自省与审计服务。</summary>
    public static readonly Node Node = new Node();

    /// <summary>AIP 展项运行时：命令去重、状态机、安全停止和 state.publish。</summary>
    public static readonly Parful.AndX.Aip.ExhibitRuntime ExhibitRuntime = new Parful.AndX.Aip.ExhibitRuntime();
}
