using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AndX.Core;

namespace AndX.Unity
{
    /// <summary>
    /// **文字类互动**（藏头诗 / 求签解签 / 祝福赠言）：同步返回，**无需轮询**。
    ///
    /// 与 <see cref="AIScenarios"/>（生图，异步任务）的关键差别：这里没有 jobNo、没有产物文件，
    /// 一次调用直接把文案拿回来。服务端仍会做三件事：
    /// 1. **拼提示词**（场景声明的人设 + 槽位，端侧不用自己拼）；
    /// 2. **校验输出**（藏头诗逐句校验首字，不合格自动带纠正提示重试一次）；
    /// 3. **计量**（按 token 计入该展项的用量台账）。
    ///
    /// 典型用法：
    /// <code>
    /// AITextResult poem = await AndX.Unity.AIText.AcrosticAsync("春风得意", "江南");
    /// outputText.text = poem.Text;
    ///
    /// AITextResult draw = await AndX.Unity.AIText.FortuneAsync("想换个工作方向");
    /// outputText.text = AIText.ComposeDisplayText(draw);   // 签诗 + 解签一起上屏
    /// </code>
    /// </summary>
    public static class AIText
    {
        /// <summary>藏头诗：<paramref name="head"/> 每个字依次作为每句首字（服务端会逐句校验）。</summary>
        public static Task<AITextResult> AcrosticAsync(
            string head,
            string topic = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(head))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "藏头字不能为空");
            }
            var parameters = new Dictionary<string, object> { ["head"] = head };
            if (!string.IsNullOrEmpty(topic))
            {
                parameters["topic"] = topic;
            }
            return AndX.AI.GenerateTextAsync(
                AndXContract.AITextScenarios.Acrostic,
                parameters,
                exhibitId: exhibitId,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 求签 / 解签：签诗由服务端从**签文库**取（不由模型现编），模型只做"结合来意解签"。
        /// <paramref name="drawNo"/> 留空 = 随机抽签；签文库未维护时服务端返回可操作错误（AI_CORPUS_MISSING）。
        /// </summary>
        public static Task<AITextResult> FortuneAsync(
            string wish,
            int? drawNo = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(wish))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "所求之事不能为空");
            }
            var parameters = new Dictionary<string, object> { ["wish"] = wish };
            if (drawNo.HasValue && drawNo.Value > 0)
            {
                parameters["drawNo"] = drawNo.Value;
            }
            return AndX.AI.GenerateTextAsync(
                AndXContract.AITextScenarios.Fortune,
                parameters,
                exhibitId: exhibitId,
                cancellationToken: cancellationToken);
        }

        /// <summary>祝福 / 赠言：40 字以内；模型输出不合规时服务端会退回模板文案（保证一定上屏）。</summary>
        public static Task<AITextResult> BlessingAsync(
            string topic,
            string to = null,
            string exhibitId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(topic))
            {
                throw new AndXException(AndXContract.SdkErrorCodes.Configuration, "主题不能为空");
            }
            var parameters = new Dictionary<string, object> { ["topic"] = topic };
            if (!string.IsNullOrEmpty(to))
            {
                parameters["to"] = to;
            }
            return AndX.AI.GenerateTextAsync(
                AndXContract.AITextScenarios.Blessing,
                parameters,
                exhibitId: exhibitId,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 组装"上屏文本"：求签时把**签诗**解签放在一起（签诗来自语料池，模型不得改写）；
        /// 其它场景直接返回模型文案。<paramref name="includeNote"/> 为 true 时附带签文库掌故。
        /// </summary>
        public static string ComposeDisplayText(AITextResult result, bool includeNote = false)
        {
            if (result == null)
            {
                return string.Empty;
            }
            var entry = result.CorpusEntry;
            if (entry == null || string.IsNullOrEmpty(entry.Poem))
            {
                return result.Text ?? string.Empty;
            }
            var title = string.IsNullOrEmpty(entry.Level)
                ? "第 " + entry.No + " 签"
                : "第 " + entry.No + " 签（" + entry.Level + "）";
            var parts = new List<string> { title, entry.Poem };
            if (includeNote && !string.IsNullOrEmpty(entry.Note))
            {
                parts.Add(entry.Note);
            }
            if (!string.IsNullOrEmpty(result.Text))
            {
                parts.Add(result.Text);
            }
            return string.Join("\n", parts);
        }
    }
}