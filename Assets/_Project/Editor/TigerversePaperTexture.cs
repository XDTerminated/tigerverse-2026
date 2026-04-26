#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    public static class TigerversePaperTexture
    {
        // Paper003 from ambientCG, 1K JPG pack (~3 MB, free CC0 licence).
        private const string DownloadUrl = "https://ambientcg.com/get?file=Paper003_1K-JPG.zip";
        private const string DestFolder  = "Assets/_Project/Resources/PaperTextures";

        [MenuItem("Tigerverse/Textures -> Download Paper003 from ambientCG")]
        public static async void Download()
        {
            Directory.CreateDirectory(DestFolder);
            string tmpZip = Path.Combine(Path.GetTempPath(), "Paper003_1K-JPG.zip");

            try
            {
                EditorUtility.DisplayProgressBar("Tigerverse", "Downloading Paper003 from ambientCG…", 0.1f);
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Add("User-Agent", "TigerverseUnityEditor/1.0");
                    using var resp = await http.GetAsync(DownloadUrl);
                    if (!resp.IsSuccessStatusCode)
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogError($"[Tigerverse] ambientCG download failed: HTTP {(int)resp.StatusCode}. Manual fallback: visit {DownloadUrl} in a browser, unzip, drop the JPG files into {DestFolder}.");
                        return;
                    }
                    using var fs = File.Create(tmpZip);
                    await resp.Content.CopyToAsync(fs);
                }

                EditorUtility.DisplayProgressBar("Tigerverse", "Unpacking…", 0.6f);
                int copied = 0;
                using (var archive = ZipFile.OpenRead(tmpZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        // Only keep the textures we use: Color + NormalGL + Roughness.
                        string lower = entry.Name.ToLowerInvariant();
                        bool wanted = lower.Contains("color") || lower.Contains("normalgl") || lower.Contains("roughness");
                        if (!wanted) continue;

                        string outPath = Path.Combine(DestFolder, entry.Name);
                        entry.ExtractToFile(outPath, overwrite: true);
                        copied++;
                    }
                }
                File.Delete(tmpZip);

                AssetDatabase.Refresh();

                // Force the imported normal map to be marked as a normal map.
                foreach (var p in Directory.GetFiles(DestFolder, "*Normal*"))
                {
                    string assetPath = p.Replace('\\', '/');
                    var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (ti != null && ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.SaveAndReimport();
                    }
                }

                Debug.Log($"[Tigerverse] Paper003 downloaded, {copied} texture(s) saved to {DestFolder}. Now spawn a test sphere or restart Play to see it.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Tigerverse] Paper download failed: {e.Message}\nManual fallback: visit {DownloadUrl} in a browser, unzip, drop JPG files into {DestFolder}.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
