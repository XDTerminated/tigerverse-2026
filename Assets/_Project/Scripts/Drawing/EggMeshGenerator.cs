using UnityEngine;

namespace Tigerverse.Drawing
{
    /// <summary>
    /// Procedural egg mesh generator. Returns a stretched-ellipsoid mesh
    /// with a subtle taper toward the top so it reads as egg-shaped, not
    /// spherical. Also provides GenerateSplit() which returns the top and
    /// bottom halves as separate meshes for the hatch-burst effect.
    /// </summary>
    public static class EggMeshGenerator
    {
        public struct Config
        {
            public int   latSegments;
            public int   lonSegments;
            public float radiusEquator;
            public float halfHeightTop;
            public float halfHeightBottom;
            public float taperTop;

            public static Config Default => new Config
            {
                latSegments = 32,
                lonSegments = 40,
                radiusEquator    = 0.18f,
                halfHeightTop    = 0.30f,
                halfHeightBottom = 0.24f,
                taperTop         = 0.20f,
            };
        }

        public static Mesh Generate(Config c, string meshName = "TigerverseEgg")
        {
            return GenerateRange(c, 0, Mathf.Max(4, c.latSegments), meshName);
        }

        // Returns (top, bottom) — meshes for the upper and lower halves split at
        // approximately V = splitV (UV-space, where 1.0 is the top apex and 0.0
        // is the bottom apex).
        public static (Mesh top, Mesh bottom) GenerateSplit(Config c, float splitV = 0.50f, string baseName = "TigerverseEgg")
        {
            int lat = Mathf.Max(4, c.latSegments);
            // splitV is in [0,1] where 1 is the top apex. Our j index goes from 0 (top) to lat (bottom).
            int splitJ = Mathf.Clamp(Mathf.RoundToInt((1f - splitV) * lat), 1, lat - 1);
            Mesh top = GenerateRange(c, 0, splitJ, baseName + "_Top");
            Mesh bot = GenerateRange(c, splitJ, lat, baseName + "_Bottom");
            return (top, bot);
        }

        private static Mesh GenerateRange(Config c, int jMin, int jMax, string meshName)
        {
            int lat = Mathf.Max(4, c.latSegments);
            int lon = Mathf.Max(6, c.lonSegments);
            int rangeLat = jMax - jMin;
            if (rangeLat < 1) rangeLat = 1;

            int vertCount = (rangeLat + 1) * (lon + 1);
            var vertices = new Vector3[vertCount];
            var normals  = new Vector3[vertCount];
            var uvs      = new Vector2[vertCount];

            for (int j = 0; j <= rangeLat; j++)
            {
                int actualJ = jMin + j;
                float v = (float)actualJ / lat;        // 0 at top, 1 at bottom
                float phi = v * Mathf.PI;              // 0..PI
                float cosPhi = Mathf.Cos(phi);
                float sinPhi = Mathf.Sin(phi);

                float h = (cosPhi >= 0f) ? c.halfHeightTop * cosPhi : c.halfHeightBottom * cosPhi;
                float taper = (cosPhi > 0f) ? Mathf.Lerp(1f, 1f - c.taperTop, cosPhi) : 1f;
                float r = c.radiusEquator * sinPhi * taper;

                float radSq = c.radiusEquator * c.radiusEquator;
                float hSq   = (cosPhi >= 0f)
                    ? Mathf.Max(c.halfHeightTop * c.halfHeightTop, 1e-4f)
                    : Mathf.Max(c.halfHeightBottom * c.halfHeightBottom, 1e-4f);

                for (int i = 0; i <= lon; i++)
                {
                    float u = (float)i / lon;
                    float theta = u * Mathf.PI * 2f;
                    float ct = Mathf.Cos(theta);
                    float st = Mathf.Sin(theta);

                    int idx = j * (lon + 1) + i;
                    Vector3 p = new Vector3(r * ct, h, r * st);
                    vertices[idx] = p;
                    Vector3 n = new Vector3(p.x / Mathf.Max(radSq, 1e-4f),
                                            p.y / hSq,
                                            p.z / Mathf.Max(radSq, 1e-4f));
                    normals[idx] = n.normalized;
                    uvs[idx] = new Vector2(u, 1f - v);
                }
            }

            int triCount = rangeLat * lon * 6;
            var tris = new int[triCount];
            int t = 0;
            for (int j = 0; j < rangeLat; j++)
            {
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i;
                    int b = (j + 1) * (lon + 1) + i;
                    int c2 = (j + 1) * (lon + 1) + (i + 1);
                    int d = j * (lon + 1) + (i + 1);
                    tris[t++] = a; tris[t++] = b; tris[t++] = c2;
                    tris[t++] = a; tris[t++] = c2; tris[t++] = d;
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
