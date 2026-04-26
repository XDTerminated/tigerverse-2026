#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Post-Gradle-export hook that forces eye-tracking from required=true to
    /// required=false in the LAUNCHER manifest, and ensures it actually wins
    /// the manifest-merge against the AAR libraries that pull it in.
    ///
    /// The Meta Quest Support feature (com.unity.xr.openxr 1.13) injects
    ///   &lt;uses-feature android:name="oculus.software.eye_tracking"
    ///                  android:required="true" /&gt;
    /// from a transitive library — it never lands in our source manifest, so
    /// editing src/main/AndroidManifest.xml directly does nothing. Instead we
    /// inject our own &lt;uses-feature ... required="false"
    /// tools:replace="android:required"&gt; into the launcher manifest,
    /// which the Android manifest merger respects and uses to overwrite the
    /// library value during merge. Without this, Quest 3 / 3S (no eye
    /// tracking) silently refuses to install/launch the APK.
    /// </summary>
    public class TigerverseManifestPatch : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        // Features the AAR-merge sets to required="true" but Quest 3/3S
        // can't satisfy. We override each one with required="false" so the
        // app installs and launches on those headsets.
        private static readonly string[] _featuresToRelax =
        {
            "oculus.software.eye_tracking",
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // `path` is the gradle module that triggered the callback (Unity
            // calls once per module). Always patch the launcher manifest,
            // which is one level up from `path/unityLibrary`.
            string launcher = Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"));
            if (!File.Exists(launcher))
            {
                // Fallback: maybe the callback path IS the launcher.
                launcher = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            }
            if (!File.Exists(launcher))
            {
                Debug.LogWarning($"[TigerverseManifestPatch] No launcher AndroidManifest.xml found near '{path}' — Quest 3/3S install may still fail.");
                return;
            }

            string xml = File.ReadAllText(launcher);
            string original = xml;

            // 1) Make sure xmlns:tools is declared on the root <manifest>
            //    so the manifest merger understands tools:replace.
            if (!xml.Contains("xmlns:tools="))
            {
                xml = Regex.Replace(
                    xml,
                    "<manifest\\b",
                    "<manifest xmlns:tools=\"http://schemas.android.com/tools\"",
                    RegexOptions.Multiline);
            }

            // 2) Inject (or update) a uses-feature override for each
            //    feature, just before </manifest>.
            foreach (var feature in _featuresToRelax)
            {
                // If we (or anyone) previously inserted this exact override,
                // skip — idempotent.
                string overridePattern = "android:name=\"" + Regex.Escape(feature) + "\"[^/>]*tools:replace=\"android:required\"";
                if (Regex.IsMatch(xml, overridePattern)) continue;

                string overrideTag = $"  <uses-feature android:name=\"{feature}\" android:required=\"false\" tools:replace=\"android:required\" />\n";
                xml = xml.Replace("</manifest>", overrideTag + "</manifest>");
            }

            if (xml != original)
            {
                File.WriteAllText(launcher, xml);
                Debug.Log($"[TigerverseManifestPatch] Patched {launcher} — eye_tracking required=false (Quest 3 / 3S compat).");
            }
        }
    }
}
#endif
