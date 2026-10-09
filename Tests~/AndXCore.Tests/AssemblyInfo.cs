using Xunit;

// AndX.Config 为进程级全局单例，测试用例会 Init/Reset 它；
// 并行执行会互相覆盖配置，故本测试程序集内串行执行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
