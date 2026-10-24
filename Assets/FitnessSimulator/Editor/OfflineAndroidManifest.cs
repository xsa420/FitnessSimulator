#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace FitnessSimulator.Editor
{
    // The first release is offline. A launcher-level merge rule also covers dependency manifests.
    public sealed class OfflineAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            const string androidNamespace = "http://schemas.android.com/apk/res/android";
            const string toolsNamespace = "http://schemas.android.com/tools";
            string manifestPath = Path.GetFullPath(Path.Combine(path, "../launcher/src/main/AndroidManifest.xml"));
            var document = new XmlDocument();
            document.Load(manifestPath);
            var root = document.DocumentElement;
            foreach (XmlNode node in root.SelectNodes("uses-permission"))
                if (node is XmlElement element && element.GetAttribute("name", androidNamespace) == "android.permission.INTERNET")
                    root.RemoveChild(node);
            var permission = document.CreateElement("uses-permission");
            permission.SetAttribute("name", androidNamespace, "android.permission.INTERNET");
            permission.SetAttribute("node", toolsNamespace, "remove");
            root.AppendChild(permission);
            document.Save(manifestPath);
            Debug.Log("FITNESS_OFFLINE_MANIFEST: Internet permission removed at final manifest merge.");
        }
    }
}
#endif
