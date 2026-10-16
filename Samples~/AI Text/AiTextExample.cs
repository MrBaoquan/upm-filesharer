using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AndX;
using AndX.Core;
using AndX.Unity;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace AndX.Samples
{
    /// <summary>示例里的文字场景（藏头诗 / 求签解签 / 祝福赠言）。</summary>
    public enum AITextSceneKind
    {
        /// <summary>藏头诗：给藏头字，出四句诗（服务端逐句校验首字，不靠模型自觉）。</summary>
        Acrostic = 0,

        /// <summary>求签 / 解签：签诗取自服务端签文库，模型只做"结合来意解签"。</summary>
        Fortune = 1,

        /// <summary>祝福 / 赠言：40 字以内；模型输出不合规时服务端退回模板文案，保证一定上屏。</summary>
        Blessing = 2,
    }

    /// <summary>
    /// 示例 5（AI Text）：**单轮文字互动**（藏头诗 / 求签解签 / 祝福赠言）。
    ///
    /// 与「AI Image」示例的关键差别：文字是**同步返回**的——没有 jobNo、没有轮询、没有产物文件，
    /// 一次调用直接把文案拿回来，现场拿到就能上屏。端侧因此只做三件事：
    /// 1. 选场景 + 填槽位（如藏头字），**不用自己拼提示词**（人设与模板在服务端场景声明里）；
    /// 2. 把结果上屏（求签用 <see cref="AIText.ComposeDisplayText"/> 把签诗与解签拼在一起）；
    /// 3. 看日志里的 token / 请求次数 / 兜底标记——"藏头没藏住"这类问题由**服务端**校验并自动重试一次。
    ///
    /// 零成本路径：先点「查询能力」（免费，不消耗额度），确认服务端已配置文字模型，再点「生成文字」
    /// （真实调用，按 token 计费，量级极小）。
    /// </summary>
    public class AITextExample : MonoBehaviour
    {
        [Header("接入方式（默认直连 AndX 云服务）")]
        [Tooltip("Direct=直连 AndX 云服务：插件持本机节点凭据；Edge=经本机 AndXEdge；Auto=有凭据则直连")]
        public AndXAccessMode accessMode = AndXAccessMode.Direct;

        [Tooltip("基址：留空 = 默认云服务基址；也可用环境变量 ANDX_ENDPOINT（本机联调填 http://127.0.0.1）")]
        public string endpoint = "";

        [Tooltip("展项 ID：留空 = 用本机节点绑定 / Edge 配置（现场一般不用填）")]
        public string exhibitId = "";

        [Header("文字场景")]
        [Tooltip("要试哪个场景（藏头诗 / 求签解签 / 祝福赠言）")]
        public AITextSceneKind sceneKind = AITextSceneKind.Acrostic;

        [Tooltip("藏头诗：藏头字（每个字依次作为每句首字），如 安达创展")]
        public string acrosticHead = "安达创展";

        [Tooltip("藏头诗：主题（可选），如 江南")]
        public string acrosticTopic = "";

        [Tooltip("求签：所求之事，如 想换个工作方向")]
        public string fortuneWish = "想换个工作方向";

        [Tooltip("求签：指定签号；0 = 随机抽签（服务端从签文库随机取）")]
        public int fortuneDrawNo = 0;

        [Tooltip("祝福：主题，如 毕业")]
        public string blessingTopic = "毕业";

        [Tooltip("祝福：送给谁（可选），如 同学")]
        public string blessingTo = "";

        [Tooltip("进入 Play 后自动查询一次能力（免费）")]
        public bool autoRunOnStart = true;

        [Header("UI 引用（示例场景已挂好；也可留空，结果看 Console 日志）")]
        [Tooltip("文字结果展示（Text）")]
        public Text OutputText;

        [Tooltip("状态提示（Text，可选）")]
        public Text StatusText;

        [Tooltip("生成按钮（可选：示例场景的按钮已绑定 Generate）")]
        public Button GenerateButton;

        [Tooltip("查询能力按钮（可选：示例场景的按钮已绑定 CheckCapabilities）")]
        public Button CapabilityButton;

        private bool busy;

        private void Start()
        {
            if (autoRunOnStart)
            {
                CheckCapabilities();
            }
            else
            {
                SetStatus("点「查询能力」免费确认服务端已配置文字模型；点「生成文字」真实调用一次（按 token 计费）");
            }
        }

        /// <summary>能力清单（免费，不消耗额度）：确认 <c>ai.text.generate</c> 已列出。</summary>
        [ContextMenu("查询能力（Capabilities）")]
        public async void CheckCapabilities()
        {
            if (!EnsureConfigured() || busy)
            {
                return;
            }
            busy = true;
            try
            {
                var caps = await AI.GetCapabilitiesAsync();
                var lines = new List<string>();
                foreach (var item in caps.Capabilities)
                {
                    lines.Add(item.Capability + " → " + item.Model);
                }
                var summary = caps.Available
                    ? "AI 已就绪：\n" + string.Join("\n", lines)
                    : "AI 未就绪：" + caps.Reason;
                SetStatus(summary);
                Debug.Log("[AI Text] " + summary);
            }
            catch (AndXException error)
            {
                SetStatus("查询失败：" + error.Code + " " + error.Message);
                Debug.LogWarning("[AI Text] 查询能力失败：" + error.Code + " " + error.Message);
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>生成文字（真实调用模型，按 token 计费；量级：一次藏头诗约 100~250 token）。</summary>
        [ContextMenu("生成文字（Generate）")]
        public async void Generate()
        {
            if (!EnsureConfigured() || busy)
            {
                return;
            }
            busy = true;
            SetStatus("生成中…");
            var startedAt = DateTime.UtcNow;
            try
            {
                var result = await GenerateOnceAsync();
                var elapsed = (DateTime.UtcNow - startedAt).TotalMilliseconds;
                var display = AIText.ComposeDisplayText(result);
                if (OutputText != null)
                {
                    OutputText.text = display;
                }
                Debug.Log(
                    "[AI Text] 场景=" + result.Scenario
                    + " 端到端=" + Math.Round(elapsed) + "ms"
                    + " token=" + result.Tokens + "（入" + result.PromptTokens + "/出" + result.CompletionTokens + "）"
                    + " 请求=" + result.Attempts + "次"
                    + " 兜底=" + result.FallbackUsed
                    + " finish=" + result.FinishReason
                    + "\n" + display);
                SetStatus(
                    "完成：" + Math.Round(elapsed) + "ms ｜ token " + result.Tokens
                    + " ｜ 请求 " + result.Attempts + " 次"
                    + (result.FallbackUsed ? "（本轮用了服务端兜底文案）" : string.Empty));
            }
            catch (AndXException error)
            {
                // 两种可操作错误在这里暴露：AI_CORPUS_MISSING=签文库未维护；AI_OUTPUT_INVALID=输出不满足约束
                SetStatus("生成失败：" + error.Code + " " + error.Message);
                Debug.LogWarning("[AI Text] 生成失败：" + error.Code + " " + error.Message);
            }
            finally
            {
                busy = false;
            }
        }

        private Task<AITextResult> GenerateOnceAsync()
        {
            switch (sceneKind)
            {
                case AITextSceneKind.Fortune:
                    return AIText.FortuneAsync(fortuneWish, fortuneDrawNo > 0 ? (int?)fortuneDrawNo : null, exhibitId);
                case AITextSceneKind.Blessing:
                    return AIText.BlessingAsync(blessingTopic, blessingTo, exhibitId);
                default:
                    return AIText.AcrosticAsync(acrosticHead, acrosticTopic, exhibitId);
            }
        }

        /// <summary>接入配置（示例里只此一处）：按 <see cref="accessMode"/> 解析基址与凭据。</summary>
        private bool TryConfigure()
        {
            try
            {
                AndX.Config.InitAuto(new AndXOptions
                {
                    AccessMode = accessMode,
                    Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint,
                    ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId,
                    EdgeKey = EdgeKey.FromEnvironment(),
                });
                Debug.Log("[AndX] 接入方式 = " + AndX.Config.Describe());
                return true;
            }
            catch (AndXException e)
            {
                Debug.LogError("[AndX] 接入配置失败 code=" + e.Code + " msg=" + e.Message);
                return false;
            }
        }

        private bool EnsureConfigured()
        {
            if (AndX.Config.IsConfigured)
            {
                return true;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AndX] 请在 Play 模式下运行示例：SDK 的 Unity 传输在运行时注册。");
                return false;
            }
            return TryConfigure() && AndX.Config.IsConfigured;
        }

        private void SetStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.text = message;
            }
        }
    }
}