using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>Numeric probe for skin-mesh generation: compares the outline-band
    /// submesh's actual triangle area against the expected thin-band area, exposing
    /// keyhole/ear-clip failures (a solid-filled band is ~ring-area, a correct band is
    /// ~perimeter * width). Reads the live SkinLayer mesh of the selected shape.</summary>
    internal static class M3SkinDiagnostics
    {
        [MenuItem("Tools/Draw To Play/Toggle Skin Debug Overlay")]
        public static void ToggleSkinOverlay()
        {
            SkinDebugOverlay.enabled = !SkinDebugOverlay.enabled;
            Debug.Log($"[SkinDiag] SkinDebugOverlay.enabled={SkinDebugOverlay.enabled}");
        }

        [MenuItem("Tools/Draw To Play/Regenerate Skins In Scene")]
        public static void RegenerateAll()
        {
            foreach (var skin in Object.FindObjectsByType<DrawnShapeSkin>(FindObjectsSortMode.None))
            {
                skin.RegenerateSkin();
                Debug.Log($"[SkinDiag] regenerated {skin.name}");
            }
        }

        [MenuItem("Tools/Draw To Play/Diagnose Skin Mesh")]
        public static void Diagnose()
        {
            var skin = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInChildren<DrawnShapeSkin>()
                : Object.FindFirstObjectByType<DrawnShapeSkin>();
            if (skin == null)
            {
                Debug.Log("[SkinDiag] no DrawnShapeSkin in selection/scene");
                return;
            }
            var smr = skin.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null)
            {
                Debug.Log("[SkinDiag] no SkinLayer mesh");
                return;
            }
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var renderer = skin.GetComponent<DrawnShapeRenderer>();
            var ring = renderer.GetBakedRing();
            float ringArea = Mathf.Abs(DrawKit.SignedArea(ring));
            float perimeter = 0f;
            for (int i = 0; i < ring.Count; i++)
                perimeter += Vector2.Distance(ring[i], ring[(i + 1) % ring.Count]);
            float width = renderer.asset != null ? renderer.asset.outlineWidth : 0f;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                float area = 0f;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                    area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
                }
                Debug.Log($"[SkinDiag] submesh {s}: tris={tris.Length / 3} area={area:F4}");
            }
            Debug.Log($"[SkinDiag] ringArea={ringArea:F4} expectedBand={perimeter * width:F4} " +
                      $"perimeter={perimeter:F3} width={width:F3} verts={mesh.vertexCount} " +
                      $"bones={smr.bones.Length} weights={mesh.boneWeights.Length}");

            // Matrix chain: for each bone, world pose vs bindpose; then CPU-skin two probe
            // vertices and compare against where the authored mesh puts them at rest.
            var binds = mesh.bindposes;
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var b = smr.bones[i];
                var skinMatrix = b.localToWorldMatrix * binds[i];
                Debug.Log($"[SkinDiag] bone{i} '{(b ? b.name : "null")}' world={b.position} " +
                          $"bindT={binds[i].GetColumn(3)} skinT={skinMatrix.GetColumn(3)} " +
                          $"skinIsIdentityAtRest={(skinMatrix * smr.transform.worldToLocalMatrix).isIdentity}");
            }
            var bw = mesh.boneWeights;
            int bandStart = mesh.vertexCount - mesh.GetTriangles(mesh.subMeshCount - 1).Length == 0
                ? mesh.vertexCount : mesh.vertexCount;
            // probe a spread of fill AND band vertices; report max CPU-skin deviation overall
            float maxDev = 0f; int maxDevIndex = -1; int zeroWeightCount = 0;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                var w = bw[i];
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (sum < 0.99f) zeroWeightCount++;
                Vector3 v = verts[i];
                Matrix4x4 m0 = smr.bones[w.boneIndex0].localToWorldMatrix * binds[w.boneIndex0];
                Matrix4x4 m1 = smr.bones[w.boneIndex1].localToWorldMatrix * binds[w.boneIndex1];
                Vector3 skinned = m0.MultiplyPoint3x4(v) * w.weight0 + m1.MultiplyPoint3x4(v) * w.weight1;
                Vector3 rigid = skin.transform.localToWorldMatrix.MultiplyPoint3x4(v);
                float dev = (skinned - rigid).magnitude;
                if (dev > maxDev) { maxDev = dev; maxDevIndex = i; }
            }
            Debug.Log($"[SkinDiag] maxCpuDeviationAtCurrentPose={maxDev:F4} at v{maxDevIndex} " +
                      $"underweighted={zeroWeightCount} (expect 0 when bones are at rest and 0 deviation)");

            // Band-vertex weight histogram: submesh 1's minimum vertex index marks the band
            // range; a band frozen at rest means its verts are dominated by one static bone.
            var bandTris = mesh.GetTriangles(mesh.subMeshCount - 1);
            int bandMin = mesh.vertexCount;
            foreach (int idx in bandTris)
                if (idx < bandMin) bandMin = idx;
            var histo = new int[smr.bones.Length];
            for (int i = bandMin; i < mesh.vertexCount; i++)
                histo[bw[i].boneIndex0]++;
            string h = "";
            for (int i = 0; i < histo.Length; i++) h += $" b{i}={histo[i]}";
            var s0 = bw[bandMin];
            var s1 = bw[(bandMin + mesh.vertexCount) / 2];
            Debug.Log($"[SkinDiag] band verts {bandMin}..{mesh.vertexCount - 1} dominantHisto:{h} " +
                      $"sample0=({s0.boneIndex0}:{s0.weight0:F2},{s0.boneIndex1}:{s0.weight1:F2}) " +
                      $"sampleMid=({s1.boneIndex0}:{s1.weight0:F2},{s1.boneIndex1}:{s1.weight1:F2})");
        }
    }
}
