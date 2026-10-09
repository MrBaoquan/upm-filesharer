using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// 契约一致性：C# 常量必须与 andx-sdk-spec（只读 SSOT）完全一致。
    /// 任一侧新增/改名而另一侧未同步时，本测试失败。
    /// </summary>
    public class ContractParityTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "andx-sdk-spec")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("未找到 andx-sdk-spec，契约一致性测试需在 AndX 仓库内运行");
        }

        private static string ReadSpec(string relative)
        {
            return File.ReadAllText(Path.Combine(FindRepoRoot(), "andx-sdk-spec", relative));
        }

        private static HashSet<string> ConstStrings(Type type)
        {
            return new HashSet<string>(
                type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                    .Select(f => (string)f.GetRawConstantValue()));
        }

        [Fact]
        public void Contract_version_matches()
        {
            var versionTs = ReadSpec("src/version.ts");
            var match = Regex.Match(versionTs, "ANDX_CONTRACT_VERSION\\s*=\\s*'([^']+)'");
            Assert.True(match.Success, "未解析到 ANDX_CONTRACT_VERSION");
            Assert.Equal(AndXContract.Version, match.Groups[1].Value);
        }

        [Fact]
        public void Share_paths_match()
        {
            var links = ReadSpec("src/links.ts");
            Assert.Contains("resource: '/r'", links);
            Assert.Contains("pay: '/p'", links);
        }

        [Fact]
        public void Business_error_codes_match_spec_exactly()
        {
            var spec = ReadSpec("src/errors.ts");
            var block = Regex.Match(spec, "AndXErrorCodes\\s*=\\s*\\{([\\s\\S]*?)\\}\\s*as const");
            Assert.True(block.Success, "未解析到 AndXErrorCodes 区块");

            var specCodes = new HashSet<string>(
                Regex.Matches(block.Groups[1].Value, "'([A-Z_]+)'").Select(m => m.Groups[1].Value));
            var csharpCodes = ConstStrings(typeof(AndXContract.ErrorCodes));

            var missingInCSharp = specCodes.Except(csharpCodes).ToList();
            var missingInSpec = csharpCodes.Except(specCodes).ToList();

            Assert.True(missingInCSharp.Count == 0, "C# 缺少契约错误码: " + string.Join(", ", missingInCSharp));
            Assert.True(missingInSpec.Count == 0, "spec 缺少 C# 错误码: " + string.Join(", ", missingInSpec));
        }

        [Fact]
        public void Sdk_error_codes_match_spec()
        {
            var spec = ReadSpec("src/errors.ts");
            var block = Regex.Match(spec, "SdkErrorCodes\\s*=\\s*\\{([\\s\\S]*?)\\}\\s*as const");
            Assert.True(block.Success, "未解析到 SdkErrorCodes 区块");

            var specCodes = new HashSet<string>(
                Regex.Matches(block.Groups[1].Value, "'([A-Z_]+)'").Select(m => m.Groups[1].Value));
            var csharpCodes = ConstStrings(typeof(AndXContract.SdkErrorCodes));

            Assert.True(specCodes.SetEquals(csharpCodes),
                "SDK 错误码不一致，spec=[" + string.Join(",", specCodes.OrderBy(x => x))
                + "] csharp=[" + string.Join(",", csharpCodes.OrderBy(x => x)) + "]");
        }

        [Fact]
        public void Headers_match_spec()
        {
            var versionTs = ReadSpec("src/version.ts");
            Assert.Contains("authorization: 'Authorization'", versionTs);
            Assert.Contains("edgeKey: 'x-edge-key'", versionTs);
            Assert.Equal("Authorization", AndXContract.Headers.Authorization);
            Assert.Equal("x-edge-key", AndXContract.Headers.EdgeKey);
        }

        [Fact]
        public void AI_capabilities_statuses_and_paths_match_spec()
        {
            var ai = ReadSpec("src/ai.ts");
            Assert.Contains("IMAGE_GENERATE: 'ai.image.generate'", ai);
            Assert.Contains("VIDEO_GENERATE: 'ai.video.generate'", ai);
            Assert.Contains("capabilities: '/api/ai/capabilities'", ai);
            Assert.Contains("jobs: '/api/ai/jobs'", ai);

            Assert.Equal("ai.image.generate", AndXContract.AICapabilities.ImageGenerate);
            Assert.Equal("ai.video.generate", AndXContract.AICapabilities.VideoGenerate);

            Assert.Equal("/api/ai/capabilities", AndXContract.Paths.AICapabilitiesPath);
            Assert.Equal("/api/ai/jobs", AndXContract.Paths.AIJobsPath);
            Assert.Equal("/api/ai/jobs/aj_1", AndXContract.Paths.AIJobPath("aj_1"));
            Assert.Equal("/api/ai/jobs/aj_1/cancel", AndXContract.Paths.AIJobCancelPath("aj_1"));

            foreach (var status in new[]
            {
                AndXContract.AIJobStatuses.Pending,
                AndXContract.AIJobStatuses.Running,
                AndXContract.AIJobStatuses.Succeeded,
                AndXContract.AIJobStatuses.Failed,
                AndXContract.AIJobStatuses.Canceled,
            })
            {
                Assert.Contains(status + ": '" + status + "'", ai);
            }

            Assert.True(AndXContract.AIJobStatuses.IsTerminal("SUCCEEDED"));
            Assert.True(AndXContract.AIJobStatuses.IsTerminal("FAILED"));
            Assert.True(AndXContract.AIJobStatuses.IsTerminal("CANCELED"));
            Assert.False(AndXContract.AIJobStatuses.IsTerminal("RUNNING"));
            Assert.False(AndXContract.AIJobStatuses.IsTerminal("PENDING"));
        }
    }
}
