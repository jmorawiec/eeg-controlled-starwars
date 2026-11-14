using System.IO;
using UnityEditor;
using UnityEngine;
using ForceDemo;

// Contact sheets of the real animator (blend tree + upper-body layer): -executeMethod LocomotionPreview.Run
public static class LocomotionPreview {
    public static void Run() {
        CombatClips.ApplyToProject();
        string outDir = Path.GetFullPath("../../work/combat-v5"); Directory.CreateDirectory(outDir);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab");
        var go = Object.Instantiate(prefab); var animator = go.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(.05f);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var sword = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(sword.GetComponent<Collider>());
        var grip = CharacterVisual.GripRotation(animator, hand, Quaternion.identity); sword.transform.rotation = grip; sword.transform.position = hand.position + grip * Vector3.up * .8f; sword.transform.localScale = new Vector3(.04f, .6f, .04f);
        sword.transform.SetParent(hand, true); sword.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.cyan };
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.22f, .25f, .28f); cam.fieldOfView = 30;
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 150, 0);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * .6f;
        // name, MoveX, MoveZ, upper state ("" = none), base state
        var scenes = new (string name, float x, float z, string upper, string bas)[] {
            ("walk", 0, 1.8f, "", "Locomotion"), ("run", 0, 3.4f, "", "Locomotion"), ("sprint", 0, 5.2f, "", "Locomotion"), ("strafe", 3.4f, 0, "", "Locomotion"),
            ("guardwalk", 0, 1.5f, "UpGuard", "Locomotion"), ("attackrun", 0, 3.4f, "UpAttack1", "Locomotion"), ("attackstill", 0, 0, "", "Attack2") };
        int w = 300, h = 450, n = 7;
        foreach (var sc in scenes) {
            animator.Rebind(); animator.SetFloat("Speed", Mathf.Abs(sc.z) + Mathf.Abs(sc.x)); animator.SetFloat("MoveX", sc.x); animator.SetFloat("MoveZ", sc.z);
            animator.Play(sc.bas, 0, 0); if (sc.upper != "") { animator.SetLayerWeight(1, 1); animator.Play(sc.upper, 1, 0); } else animator.SetLayerWeight(1, 0);
            animator.Update(.2f);
            var sheet = new Texture2D(w * n, h * 2, TextureFormat.RGB24, false); var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            for (int v = 0; v < 2; v++) for (int i = 0; i < n; i++) {
                animator.Update(i == 0 ? 0 : .08f);
                cam.transform.position = v == 0 ? new Vector3(2.4f, 1.5f, 3.4f) : new Vector3(4.2f, 1.2f, 0); cam.transform.LookAt(new Vector3(0, .95f, 0));
                if (v == 1 && i == 0) { animator.Rebind(); animator.SetFloat("MoveX", sc.x); animator.SetFloat("MoveZ", sc.z); animator.Play(sc.bas, 0, 0); if (sc.upper != "") { animator.SetLayerWeight(1, 1); animator.Play(sc.upper, 1, 0); } animator.Update(.2f); }
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.enabled = false; var baked = new Mesh(); smr.BakeMesh(baked, true); var mats = smr.sharedMaterials; var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int sub = 0; sub < baked.subMeshCount; sub++) Graphics.DrawMesh(baked, mtx, mats[Mathf.Min(sub, mats.Length - 1)], 0, cam, sub); }
                cam.Render(); RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); sheet.SetPixels(i * w, (1 - v) * h, w, h, tex.GetPixels()); Object.DestroyImmediate(tex);
            }
            File.WriteAllBytes(Path.Combine(outDir, "loco_" + sc.name + ".png"), sheet.EncodeToPNG()); RenderTexture.active = null;
        }
        Debug.Log("LOCO_PREVIEW_OK");
    }
}
