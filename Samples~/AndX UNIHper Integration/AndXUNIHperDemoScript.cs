using UnityEngine;
using UNIHper;

namespace Parful.AndX.Samples
{
    /// <summary>
    /// UNIHper 按「场景名 + Script」自动发现的场景脚本。
    /// 只负责显示 UI Toolkit 页面；AndX 配置由 UConfig 生命周期自动完成。
    /// </summary>
    public sealed class AndXUNIHperDemoScript : SceneScriptBase
    {
        private void Start()
        {
            var ui = Managements.UI.Show<AndXUNIHperDemoUI>();
            if (ui == null)
            {
                Debug.LogError("[AndX][AndXUNIHperDemo] 无法显示 AndXUNIHperDemoUI，请检查 UIToolkit 初始化。");
            }
        }
    }
}
