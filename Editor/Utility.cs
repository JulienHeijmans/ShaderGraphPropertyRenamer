using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShaderGraphPropertyRenamer
{
    public class Utility
    {
        // /////////////////////
        // Utility functions
        // /////////////////////
        public static string GetScriptPath(string typeName, string fileName="")
        {
            if (fileName == "")
                fileName = typeName + ".cs";
        
            var scriptPaths = AssetDatabase.FindAssets($"{typeName} t:Script")
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p) == fileName).ToArray();
            if (scriptPaths.Length != 1)
            {
                Debug.LogError($"There should only be one script in project of type: {typeName}");
                return "";
            }
            var path = scriptPaths[0].Replace(fileName, "").Replace("\\", "/");
            if(path.IndexOf("Assets/", System.StringComparison.InvariantCulture)>0)
                path = path.Substring(path.IndexOf("Assets/", System.StringComparison.InvariantCulture));
            if(path.IndexOf("Packages/", System.StringComparison.InvariantCulture)>0)
                path = path.Substring(path.IndexOf("Assets/", System.StringComparison.InvariantCulture));
            return path;
        }
    }
}