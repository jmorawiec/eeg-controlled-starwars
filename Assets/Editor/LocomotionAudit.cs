using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Estimates the ground speed each in-place gait clip is authored for, from the planted foot's motion relative to the hips.
public static class LocomotionAudit {
    public static float NaturalSpeed(GameObject go, AnimationClip clip) {
        var animator = go.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = null;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips); var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
        int n = 90; float dt = clip.length / n; float best = 0;
        var graph = PlayableGraph.Create("audit"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); var output = AnimationPlayableOutput.Create(graph, "o", animator); var cp = AnimationClipPlayable.Create(graph, clip); output.SetSourcePlayable(cp);
        foreach (var foot in feet) {
            var pos = new Vector2[n + 1]; var height = new float[n + 1];
            for (int i = 0; i <= n; i++) { cp.SetTime(Mathf.Min(i * dt, clip.length - 1e-4f)); graph.Evaluate(0); Vector3 local = go.transform.InverseTransformPoint(foot.position) - go.transform.InverseTransformPoint(hips.position); pos[i] = new Vector2(local.x, local.z); height[i] = go.transform.InverseTransformPoint(foot.position).y; }
            if (clip.name.Contains("Forward")) Debug.Log("LOCODBG " + clip.name + " footY=" + height.Min().ToString("F3") + ".." + height.Max().ToString("F3") + " z=" + pos.Min(v => v.y).ToString("F3") + ".." + pos.Max(v => v.y).ToString("F3"));
            float min = height.Min(); Vector2 sum = Vector2.zero; int count = 0;
            for (int i = 0; i < n; i++) if (height[i] < min + .035f && height[i + 1] < min + .035f) { sum += (pos[i + 1] - pos[i]) / dt; count++; }
            if (count > 2) best = Mathf.Max(best, (sum / count).magnitude);
        }
        graph.Destroy(); return best;
    }
    public static void Run() {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab"); var go = Object.Instantiate(prefab);
        foreach (var p in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Kevin Iglesias" }).Select(AssetDatabase.GUIDToAssetPath).Where(x => x.Contains("/Male/") && (x.Contains("Walk01") || x.Contains("Run01") || x.Contains("Sprint01")))) {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__")); if (!clip) continue;
            Debug.Log("LOCO " + System.IO.Path.GetFileNameWithoutExtension(p) + " len=" + clip.length.ToString("F2") + " naturalSpeed=" + NaturalSpeed(go, clip).ToString("F2"));
        }
    }
}
