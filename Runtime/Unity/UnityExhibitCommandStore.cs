using System.IO;
using System.Text;
using Parful.AndX.Aip;
using UnityEngine;

namespace Parful.AndX.Unity
{
    /// <summary>Unity 侧命令去重库位置：Application.persistentDataPath/andx/exhibit-runtime。</summary>
    public static class UnityExhibitCommandStore
    {
        public static FileExhibitCommandStore Create(string exhibitId = null)
        {
            var fileName = Sanitize(string.IsNullOrWhiteSpace(exhibitId) ? "exhibit" : exhibitId.Trim()) + "-commands.json";
            var path = Path.Combine(Application.persistentDataPath, "andx", "exhibit-runtime", fileName);
            return new FileExhibitCommandStore(path);
        }

        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '_');
            }
            return builder.ToString();
        }
    }
}
