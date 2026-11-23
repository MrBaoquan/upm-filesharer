using System;
using UnityEditor;
using UnityEngine;
using UNIHper;

namespace Parful.AndX.Integration.UNIHper.Editor
{
    [InitializeOnLoad]
    internal static class AndXUNIHperAssemblyRegistrar
    {
        internal const string AssemblyName = "AndX.Integration.UNIHper";

        static AndXUNIHperAssemblyRegistrar()
        {
            EditorApplication.delayCall += EnsureRegistered;
        }

        private static void EnsureRegistered()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                UNIHperSettings.AddAssemblyToSettingsIfNotExists(AssemblyName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AndX][UNIHper] 自动注册程序集失败: " + ex.Message);
            }
        }
    }
}