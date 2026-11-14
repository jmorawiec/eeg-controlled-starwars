using ForceDemo;
using System.IO;
using UnityEditor;
using UnityEngine;

// Renders a contact sheet of a clip's poses on the hero prefab: -executeMethod CombatPreview.Run
public static class CombatPreview {
    public static void All() { CombatClips.ApplyToProject(); Run(); }
    static string[] Names() => System.Environment.GetEnvironmentVariable("CLIPS") is string c && c.Length > 0 ? c.Split(',') : new[] { "Attack1", "Attack2", "Attack3", "Guard", "Cast" };
    static AnimationClip Load(string name) {
        string[] fbx = { "CombatAttack", "CombatGuard", "m@FallLandRoll", "m@Idle" };
        if (System.Array.IndexOf(fbx, name) >= 0) {
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/WorldAssets/" + name + ".fbx");
            foreach (var o in all) if (o is AnimationClip ac && !ac.name.StartsWith("__")) return ac;
        }
        return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Generated/Cinematic/" + name + ".anim");
    }
    public static void Run() {
        string outDir = Path.GetFullPath("../../work/v10/combat-preview");
        Directory.CreateDirectory(outDir);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab");
        var go = Object.Instantiate(prefab);
        var animator = go.GetComponentInChildren<Animator>();
        var ctrl = animator.runtimeAnimatorController; animator.Rebind(); animator.Update(.05f);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.22f, .25f, .28f); cam.fieldOfView = 32;
        cam.transform.position = new Vector3(2.6f, 1.5f, 3.2f); cam.transform.LookAt(new Vector3(0, 1.0f, 0));
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 150, 0);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * .6f;
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var sword = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(sword.GetComponent<Collider>());
        
        var grip = CharacterVisual.GripRotation(animator, hand, Quaternion.identity); sword.transform.rotation = grip; sword.transform.position = hand.position + grip * Vector3.up * .8f; sword.transform.localScale = new Vector3(.04f, .6f, .04f); animator.runtimeAnimatorController = null; sword.transform.SetParent(hand, true);
        sword.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.cyan };
        foreach (var name in Names()) {
            var clip = Load(name);
            int n = 6; int w = 360, h = 540; var views = new[] { new Vector3(2.6f, 1.5f, 3.2f), new Vector3(4.2f, 1.3f, 0f) };
            var sheet = new Texture2D(w * n, h * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            for (int v = 0; v < 2; v++) {
                cam.transform.position = views[v]; cam.transform.LookAt(new Vector3(0, 1.0f, 0));
                for (int i = 0; i < n; i++) {
                    float t = clip.length * i / (n - 1);
                    clip.SampleAnimation(animator.gameObject, t);
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                        smr.enabled = false; var baked = new Mesh(); smr.BakeMesh(baked, true);
                        var mats = smr.sharedMaterials; var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        for (int sub = 0; sub < baked.subMeshCount; sub++) Graphics.DrawMesh(baked, mtx, mats[Mathf.Min(sub, mats.Length - 1)], 0, cam, sub);
                    }
                    cam.Render(); RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    sheet.SetPixels(i * w, (1 - v) * h, w, h, tex.GetPixels()); Object.DestroyImmediate(tex);
                }
            }
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), sheet.EncodeToPNG());
            RenderTexture.active = null; Object.DestroyImmediate(sheet);
        }
        Debug.Log("COMBAT_PREVIEW_OK " + outDir);
    }

    // Sweeps single muscles from -1 to +1 on a base stance to learn their sign/range on this rig.
    public static void Diag() {
        string outDir = Path.GetFullPath("../../work/v10/combat-preview"); Directory.CreateDirectory(outDir);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab");
        var go = Object.Instantiate(prefab); var animator = go.GetComponentInChildren<Animator>();
        var ctrl = animator.runtimeAnimatorController; animator.Rebind(); animator.Update(.05f);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var sword = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(sword.GetComponent<Collider>());
        var grip = CharacterVisual.GripRotation(animator, hand, Quaternion.identity);
        sword.transform.rotation = grip; sword.transform.position = hand.position + grip * Vector3.up * .8f; sword.transform.localScale = new Vector3(.04f, .6f, .04f);
        sword.transform.SetParent(hand, true); sword.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.cyan };
        animator.runtimeAnimatorController = null;
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.22f, .25f, .28f); cam.fieldOfView = 32;
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 150, 0);
        var handler = new HumanPoseHandler(animator.avatar, animator.transform);
        var names = HumanTrait.MuscleName;
        var sets = new[] { new[] { "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Shoulder Down-Up" },
                           new[] { "Right Hand Down-Up", "Spine Twist Left-Right", "Spine Front-Back", "Right Upper Leg Front-Back", "Right Lower Leg Stretch" } };
        float[] vals = { -1, -.5f, 0, .5f, 1 }; int w = 240, h = 360;
        for (int si = 0; si < sets.Length; si++) {
            var sheet = new Texture2D(w * 5, h * 5, TextureFormat.RGB24, false); var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) {
                HumanPose pose = new HumanPose(); handler.GetHumanPose(ref pose);
                for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0;
                void Set(string n, float v) { int ix = System.Array.IndexOf(names, n); if (ix >= 0) pose.muscles[ix] = v; }
                Set("Left Arm Down-Up", -.6f); Set("Right Arm Down-Up", -.6f);
                Set(sets[si][r], vals[c]);
                pose.bodyPosition = new Vector3(0, 1f, 0); pose.bodyRotation = Quaternion.identity; handler.SetHumanPose(ref pose);
                cam.transform.position = new Vector3(2.4f, 1.4f, 3.0f); cam.transform.LookAt(new Vector3(0, 1f, 0));
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                    smr.enabled = false; var baked = new Mesh(); smr.BakeMesh(baked, true);
                    var mats = smr.sharedMaterials; var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int sub = 0; sub < baked.subMeshCount; sub++) Graphics.DrawMesh(baked, mtx, mats[Mathf.Min(sub, mats.Length - 1)], 0, cam, sub);
                }
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                sheet.SetPixels(c * w, (4 - r) * h, w, h, tex.GetPixels()); Object.DestroyImmediate(tex);
            }
            File.WriteAllBytes(Path.Combine(outDir, "diag" + si + ".png"), sheet.EncodeToPNG()); RenderTexture.active = null;
        }
        Debug.Log("COMBAT_DIAG_OK");
    }
}
