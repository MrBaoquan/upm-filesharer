using UnityEngine;
using UnityEngine.UIElements;
using UNIHper;
using UNIHper.UI;

namespace Parful.AndX.Samples
{
    /// <summary>
    /// 让示例场景可以从任意入口直接 Play：若工程没有常驻 UNIHperEntry，
    /// 则实例化工程 Resources 中的 UNIHper 启动预制体。正式工程仍建议从
    /// 自己的 UNIHper 启动场景进入，示例会自动复用已有 UNIHperEntry。
    /// </summary>
    public sealed class AndXUNIHperDemoBootstrap : MonoBehaviour
    {
        private static bool resourcesRegistered;

        private void Awake()
        {
            EnsureMainCamera();
#if UNITY_2023_1_OR_NEWER
            var existing = FindFirstObjectByType<UNIHperEntry>();
#else
            var existing = FindObjectOfType<UNIHperEntry>();
#endif
            if (existing == null)
            {
                var prefab = Resources.Load<GameObject>("UNIHper/Prefabs/UNIHper");
                if (prefab == null)
                {
                    Debug.LogError(
                        "[AndX][AndXUNIHperDemo] 未找到 Resources/UNIHper/Prefabs/UNIHper。"
                        + "请先执行 UNIHper 初始化，或从工程自己的 UNIHper 启动场景进入。"
                    );
                    return;
                }

                var instance = Instantiate(prefab);
                instance.name = "__UNIHper";
            }

            var panelSettings = Resources.Load<PanelSettings>("UIToolkit/AndXUNIHperDemoPanelSettings");
            if (panelSettings != null)
            {
                UIToolkitConfig.DefaultPanelSettings = panelSettings;
            }

            if (!resourcesRegistered)
            {
                resourcesRegistered = true;
                Managements.Resource.AddConfig("UNIHper/AndXUNIHperDemoResources");
            }
        }
        private static void EnsureMainCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.08f, 1f);
            camera.cullingMask = 0;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
        }

    }
}
