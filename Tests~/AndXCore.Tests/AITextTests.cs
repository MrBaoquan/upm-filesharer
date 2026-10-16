using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using Xunit;

namespace AndX.Tests
{
    /// <summary>
    /// 单轮文字能力（藏头诗 / 解签 / 祝福）的契约测试：路径 / 鉴权 / 请求体形状 / 结果映射 / 错误透出。
    /// 这些断言保证"端侧只要给场景与槽位"，提示词拼装与输出校验都在服务端。
    /// </summary>
    public class AITextTests
    {
        public AITextTests()
        {
            Config.Reset();
        }

        private static void Configure(FakeTransport transport, string exhibitId = null)
        {
            Config.Init(
                new AndXOptions { Endpoint = "https://edge.example.com", EdgeKey = "edge-key", ExhibitId = exhibitId },
                transport);
        }

        private const string OkBody =
            "{\"capability\":\"ai.text.generate\",\"scenario\":\"acrostic\",\"model\":\"deepseek-v4-1-flash-260910\","
            + "\"text\":\"春波绿涨画船摇，\\n风拂垂杨过石桥。\\n得雨杏花香满袖，\\n意随燕子入吴谣。\","
            + "\"tokens\":107,\"promptTokens\":79,\"completionTokens\":28,\"attempts\":1,\"fallbackUsed\":false,"
            + "\"violations\":[],\"corpusEntry\":null,\"finishReason\":\"stop\",\"metered\":false}";

        [Fact]
        public async Task GenerateText_posts_scenario_and_params_to_text_path_with_edge_key()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(OkBody));
            Configure(transport);

            var result = await AI.GenerateTextAsync(
                "acrostic",
                new Dictionary<string, object> { ["head"] = "春风得意", ["topic"] = "江南" });

            Assert.Equal("春波绿涨画船摇，\n风拂垂杨过石桥。\n得雨杏花香满袖。\n意随燕子入吴谣。".Replace("。\n意", "，\n意"), result.Text);
            Assert.Equal(107, result.Tokens);
            Assert.Equal(28, result.CompletionTokens);
            Assert.Equal(1, result.Attempts);
            Assert.False(result.FallbackUsed);
            Assert.Equal(AndXContract.AITextScenarios.Acrostic, result.Scenario);

            var r = transport.Requests.Single();
            Assert.Equal("POST", r.Method);
            Assert.Equal("https://edge.example.com/api/ai/text", r.Url); // 同步接口，不是 /api/ai/jobs
            Assert.True(r.HasEdgeKey);
            Assert.Contains("\"capability\":\"ai.text.generate\"", r.Body);
            Assert.Contains("\"scenario\":\"acrostic\"", r.Body);
            Assert.Contains("春风得意", r.Body);
        }

        [Fact]
        public async Task GenerateText_maps_corpus_entry_for_fortune()
        {
            var body =
                "{\"capability\":\"ai.text.generate\",\"scenario\":\"fortune\",\"model\":\"m\",\"text\":\"此签中吉。\","
                + "\"tokens\":221,\"promptTokens\":162,\"completionTokens\":59,\"attempts\":1,\"fallbackUsed\":false,"
                + "\"violations\":[],\"finishReason\":\"stop\",\"metered\":true,"
                + "\"corpusEntry\":{\"no\":3,\"level\":\"中吉\",\"poem\":\"松间月照石上泉\",\"note\":\"静中生慧之象\"}}";
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(body));
            Configure(transport);

            var result = await AI.GenerateTextAsync(
                new AITextRequest
                {
                    Scenario = AndXContract.AITextScenarios.Fortune,
                    Params = new Dictionary<string, object> { ["wish"] = "想换个工作方向", ["drawNo"] = 3 },
                });

            Assert.NotNull(result.CorpusEntry);
            Assert.Equal("3", result.CorpusEntry.No);
            Assert.Equal("中吉", result.CorpusEntry.Level);
            Assert.Equal("松间月照石上泉", result.CorpusEntry.Poem);
            Assert.True(result.Metered);

            var r = transport.Requests.Single();
            Assert.Contains("\"wish\":\"想换个工作方向\"", r.Body);
            Assert.Contains("\"drawNo\":3", r.Body);
        }

        [Fact]
        public async Task GenerateText_requires_scenario_or_prompt_locally()
        {
            var transport = new FakeTransport((req, i) => FakeResponse.Ok(OkBody));
            Configure(transport);

            var error = await Assert.ThrowsAsync<AndXException>(
                () => AI.GenerateTextAsync(new AITextRequest()));
            Assert.Equal(AndXContract.SdkErrorCodes.Configuration, error.Code);
            Assert.Empty(transport.Requests); // 本地就拦下，不发请求
        }

        [Fact]
        public async Task GenerateText_surfaces_server_error_code_and_message()
        {
            var transport = new FakeTransport(
                (req, i) =>
                    FakeResponse.Error(503, "AI_CORPUS_MISSING", "场景「求签 / 解签」需要先维护语料池：ai.corpus.fortune"));
            Configure(transport);

            var error = await Assert.ThrowsAsync<AndXException>(
                () => AI.GenerateTextAsync(AndXContract.AITextScenarios.Fortune, new Dictionary<string, object> { ["wish"] = "问学业" }));

            Assert.Equal("AI_CORPUS_MISSING", error.Code);
            Assert.Contains("ai.corpus.fortune", error.Message);
        }
    }
}