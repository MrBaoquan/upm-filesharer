using System;
using System.Threading;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using UnityEngine;
using UNIHper;

namespace Parful.AndX.Integration.UNIHper
{
    /// <summary>
    /// UNIHper 可选接入桥。核心 AndX 包不引用 UNIHper；只有本程序集在 UNIHper 存在时编译。
    /// </summary>
    public static class AndXUNIHperBridge
    {
        private static AndXUNIHperSettings _settings;

        /// <summary>
        /// 在自动初始化前对 AndXOptions 做宿主注入，例如设置 AccessTokenProvider。
        /// 静态凭据可从 AndX.xml 读取；这里适合覆盖或接入运行时令牌源。任何密钥都不得写日志。
        /// </summary>
        public static Action<AndXOptions> ConfigureOptions { get; set; }

        public static AndXUNIHperSettings Current =>
            _settings ?? Managements.Config.Get<AndXUNIHperSettings>();

        public static bool IsReady => global::AndX.Config.IsConfigured;

        public static Exception LastError { get; private set; }

        public static string ConfigFilePath => Current?.FilePath;

        public static AndXOptions CreateOptions()
        {
            var settings = Current;
            if (settings == null)
            {
                throw new AndXException(
                    AndXContract.SdkErrorCodes.Configuration,
                    "未找到 AndXUNIHperSettings；请确认 UNIHper 已初始化且 AndX.Integration.UNIHper 已注册"
                );
            }
            return settings.CreateOptions();
        }

        /// <summary>使用当前配置执行一次幂等的自动初始化。</summary>
        public static bool Apply()
        {
            return ApplySettings(Current, false);
        }

        /// <summary>重新读取 AndX.xml 并强制应用，适用于外部修改配置后的显式重载。</summary>
        public static bool ReloadAndApply()
        {
            var settings = Managements.Config.Reload<AndXUNIHperSettings>();
            return ApplySettings(settings, true);
        }

        /// <summary>
        /// 首次注册或需要异步刷新凭据时使用。普通启动由 OnLoaded 自动完成。
        /// </summary>
        public static async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            var settings = Current;
            if (settings == null)
            {
                LastError = new InvalidOperationException("未找到 AndXUNIHperSettings");
                Debug.LogWarning("[AndX][UNIHper] 未找到 AndXUNIHperSettings，无法初始化 AndX");
                return false;
            }

            try
            {
                var options = settings.CreateOptions();
                ConfigureOptions?.Invoke(options);
                await global::AndX.Config.InitAutoAsync(options, cancellationToken);
                LastError = null;
                Debug.Log("[AndX][UNIHper] 异步初始化完成: " + global::AndX.Config.Describe());
                return true;
            }
            catch (OperationCanceledException)
            {
                LastError = null;
                Debug.LogWarning("[AndX][UNIHper] 异步初始化已取消");
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex;
                Debug.LogWarning("[AndX][UNIHper] 异步初始化失败: " + ex.Message);
                return false;
            }
        }

        internal static bool ApplySettings(AndXUNIHperSettings settings, bool force)
        {
            _settings = settings;
            if (settings == null)
            {
                LastError = new InvalidOperationException("AndXUNIHperSettings 为空");
                Debug.LogWarning("[AndX][UNIHper] AndXUNIHperSettings 为空，跳过初始化");
                return false;
            }

            if (!settings.AutoInitialize && !force)
                return false;

            if (global::AndX.Config.IsConfigured && !force)
                return true;

            try
            {
                var options = settings.CreateOptions();
                ConfigureOptions?.Invoke(options);

                if (force && global::AndX.Config.IsConfigured)
                    global::AndX.Config.Reset();

                global::AndX.Config.InitAuto(options);
                LastError = null;
                Debug.Log("[AndX][UNIHper] 自动初始化完成: " + global::AndX.Config.Describe());
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                Debug.LogWarning(
                    "[AndX][UNIHper] 自动初始化失败: " + ex.Message
                    + "；配置文件=" + settings.FilePath
                );
                return false;
            }
        }
    }
}