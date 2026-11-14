using System.IO;
using UnityEditor;
using UnityEngine;
using ForceDemo;

// Renders a front view of every character class side by side: -executeMethod CharacterLineup.Run
public static class CharacterLineup {
    public static void Run() {
        EnemyModels.Build();
        string outDir = Path.GetFullPath("../../work/combat-v5"); Directory.CreateDirectory(outDir);
        var parent = new GameObject("Lineup").transform;
        var specs = new (string name, bool boss, bool player)[] { ("Apprentice", false, true), ("Sentinel", false, false), ("Ranged sentinel", false, false), ("Training sentinel", false, false), ("Guardian of the Rift", true, false), ("Mentor Auren / Veyr", false, false) };
        var chars = new GameObject[specs.Length];
        for (int i = 0; i < specs.Length; i++) {
            var t = CharacterVisual.Create(specs[i].name, new Vector3((i - 2.5f) * 1.9f, 0, 0), new Color(.25f, .88f, 1), parent, specs[i].boss, specs[i].player);
            t.rotation = Quaternion.Euler(0, 180, 0); chars[i] = t.gameObject;
            var an = t.GetComponentInChildren<Animator>(); if (an) { an.Rebind(); an.Update(.1f); }
            foreach (var m in t.GetComponentsInChildren<MonoBehaviour>()) { if (m is BoneFollow) m.SendMessage("LateUpdate"); if (m is ClothCape) { m.SendMessage("Start"); m.SendMessage("LateUpdate"); m.SendMessage("LateUpdate"); } }
        }
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.28f, .3f, .33f); cam.fieldOfView = 30;
        cam.transform.position = new Vector3(0, 1.5f, -9.5f); cam.transform.LookAt(new Vector3(0, 1.05f, 0));
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(35, -150, 0); light.intensity = 1.3f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .5f, .55f);
        var rt = new RenderTexture(1920, 640, 24); cam.targetTexture = rt;
        foreach (var smr in parent.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.enabled = false; var baked = new Mesh(); smr.BakeMesh(baked, true); var mats = smr.sharedMaterials; var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            // tint from CharacterVisual is a property block; mimic it for the preview
            var cv = smr.GetComponentInParent<CharacterVisual>(); var mpb = new MaterialPropertyBlock(); smr.GetPropertyBlock(mpb);
            for (int sub = 0; sub < baked.subMeshCount; sub++) Graphics.DrawMesh(baked, mtx, mats[Mathf.Min(sub, mats.Length - 1)], 0, cam, sub, mpb); }
        cam.Render(); RenderTexture.active = rt; var tex = new Texture2D(1920, 640, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 640), 0, 0);
        File.WriteAllBytes(Path.Combine(outDir, "lineup.png"), tex.EncodeToPNG()); Debug.Log("LINEUP_OK");
    }
}
