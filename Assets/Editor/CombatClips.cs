using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ForceDemo;

// Generates humanoid sword-combat clips (three distinct slashes, guard, force cast).
// Poses are authored as muscle deltas on top of a real combat stance captured from CombatAttack,
// so they retarget to any humanoid rig and keep a believable stance. Hip height is calibrated per key
// so the lowest foot always rests on the ground.
public static class CombatClips {
    const string Dir = "Assets/Resources/Generated/Combat/";
    static readonly string[] Names = HumanTrait.MuscleName;

    class Key { public float t, pitch, tilt; public (string, float)[] d; public Key(float t, params (string, float)[] d) { this.t = t; this.d = d; } public Key At(float time) => new Key(time, d) { pitch = pitch, tilt = tilt }; }

    static (string, float) R(string muscle, float v) => ("Right " + muscle, v);
    static (string, float) L(string muscle, float v) => ("Left " + muscle, v);
    static (string, float) S(string muscle, float v) => (muscle, v);

    // Torso: lean (+ forward), twist (+ toward the sword side), spread over the spine chain.
    static (string, float)[] Torso(float lean, float twist) => new[] {
        S("Spine Front-Back", lean * .8f), S("Chest Front-Back", lean * .5f), S("UpperChest Front-Back", lean * .3f),
        S("Spine Twist Left-Right", twist * .7f), S("Chest Twist Left-Right", twist * .6f), S("UpperChest Twist Left-Right", twist * .4f) };
    static Key K(float t, (string, float)[] torso, params (string, float)[] rest) => new Key(t, torso.Concat(rest).ToArray());

    static List<Key> Attack(int n) {
        var ready = new Key(0);
        if (n == 1) return new List<Key> { ready,
            K(.20f, Torso(-.15f, -.5f), R("Arm Down-Up", .4f), R("Arm Front-Back", -.1f), R("Forearm Stretch", -.4f), R("Upper Leg Front-Back", .15f)),
            K(.38f, Torso(.25f, .6f), R("Arm Down-Up", -.15f), R("Arm Front-Back", .35f), R("Forearm Stretch", .3f), R("Upper Leg Front-Back", -.5f), R("Lower Leg Stretch", -.3f), L("Upper Leg Front-Back", .3f)),
            K(.58f, Torso(.15f, .45f), R("Arm Down-Up", -.25f), R("Arm Front-Back", .3f), R("Forearm Stretch", .1f), R("Upper Leg Front-Back", -.4f), R("Lower Leg Stretch", -.25f)),
            ready.At(.85f) };
        if (n == 2) return new List<Key> { ready,
            K(.20f, Torso(.05f, .6f), R("Arm Down-Up", .1f), R("Arm Front-Back", .55f), R("Forearm Stretch", -.35f), L("Upper Leg Front-Back", .1f)),
            K(.38f, Torso(.10f, -.65f), R("Arm Down-Up", .15f), R("Arm Front-Back", -.55f), R("Forearm Stretch", .15f), L("Upper Leg Front-Back", -.5f), L("Lower Leg Stretch", -.3f), R("Upper Leg Front-Back", .3f)),
            K(.58f, Torso(.08f, -.45f), R("Arm Down-Up", .15f), R("Arm Front-Back", -.4f), R("Forearm Stretch", -.05f), L("Upper Leg Front-Back", -.4f), L("Lower Leg Stretch", -.25f)),
            ready.At(.85f) };
        return new List<Key> { ready,
            K(.30f, Torso(-.35f, 0), R("Arm Down-Up", .45f), R("Arm Front-Back", .45f), L("Arm Down-Up", .35f), L("Arm Front-Back", .6f), L("Forearm Stretch", -.2f), R("Forearm Stretch", -.35f), R("Upper Leg Front-Back", .2f)),
            K(.46f, Torso(.55f, .15f), R("Arm Down-Up", -.2f), R("Arm Front-Back", .7f), R("Forearm Stretch", .5f), L("Arm Down-Up", -.15f), L("Arm Front-Back", .6f), R("Upper Leg Front-Back", -.45f), R("Lower Leg Stretch", -.3f)),
            K(.70f, Torso(.5f, .1f), R("Arm Down-Up", -.2f), R("Arm Front-Back", .5f), R("Forearm Stretch", .3f), L("Arm Front-Back", .45f), R("Upper Leg Front-Back", -.4f), R("Lower Leg Stretch", -.28f)),
            ready.At(1.0f) };
    }
    // Forward tumble: crouch, tuck, a full 360 degree pitch about the hips, then rise back into the sword stance.
    static List<Key> Roll() {
        float[] t =    { 0f, .10f, .18f, .26f, .34f, .42f, .50f, .58f, .66f, .76f, .88f, 1.0f };
        float[] pitch = { 0f,   0f,  35f, 105f, 175f, 245f, 315f, 360f, 360f, 360f, 360f, 360f };
        float[] tuck =  { 0f,  .45f, 1f,   1f,   1f,   1f,   1f,  .85f, .55f, .25f, .08f, 0f };
        float[] tilt =  { 0f,   0f,  2f, -10f, -20f, -24f, -18f,  -8f,   0f,   0f,  0f, 0f }; // rolls over one shoulder instead of a dead-straight somersault
        var keys = new List<Key>();
        for (int i = 0; i < t.Length; i++) {
            float k = tuck[i];
            var key = K(t[i], Torso(1.3f * k, 0),
                R("Upper Leg Front-Back", -1.2f * k), L("Upper Leg Front-Back", -1.2f * k), R("Lower Leg Stretch", -1.2f * k), L("Lower Leg Stretch", -1.2f * k),
                R("Arm Front-Back", 1.0f * k), L("Arm Front-Back", 1.0f * k), R("Arm Down-Up", -.4f * k), L("Arm Down-Up", -.4f * k),
                R("Forearm Stretch", -.9f * k), L("Forearm Stretch", -.9f * k), S("Neck Nod Down-Up", -.6f * k), S("Head Nod Down-Up", -.4f * k));
            key.pitch = pitch[i]; key.tilt = tilt[i]; keys.Add(key);
        }
        return keys;
    }
    static List<Key> Guard() {
        var a = K(0, Torso(.05f, -.2f), R("Arm Down-Up", -.05f), R("Arm Front-Back", .65f), R("Forearm Stretch", -.45f), L("Arm Down-Up", .0f), L("Arm Front-Back", .45f), L("Forearm Stretch", -.35f));
        var b = K(.6f, Torso(.08f, -.2f), R("Arm Down-Up", -.03f), R("Arm Front-Back", .68f), R("Forearm Stretch", -.47f), L("Arm Down-Up", .02f), L("Arm Front-Back", .47f), L("Forearm Stretch", -.35f));
        return new List<Key> { a, b, a.At(1.2f) };
    }
    static List<Key> Cast() {
        var a = K(0, Torso(.1f, -.1f), R("Arm Down-Up", .05f), R("Arm Front-Back", .9f), R("Forearm Stretch", .6f), L("Arm Front-Back", -.1f));
        var b = K(.5f, Torso(.12f, -.12f), R("Arm Down-Up", .08f), R("Arm Front-Back", .95f), R("Forearm Stretch", .65f), L("Arm Front-Back", -.1f));
        return new List<Key> { a, b, a.At(1f) };
    }
    // Sword stance shared by every key: staggered feet, soft knees, free hand raised in front.
    static readonly (string, float)[] Stance = {
        R("Upper Leg Front-Back", -.22f), L("Upper Leg Front-Back", .18f), R("Lower Leg Stretch", -.14f), L("Lower Leg Stretch", -.08f),
        R("Upper Leg In-Out", -.06f), L("Upper Leg In-Out", -.1f),
        L("Arm Down-Up", .08f), L("Arm Front-Back", .4f), L("Forearm Stretch", -.4f),
        R("Arm Down-Up", .0f), R("Arm Front-Back", .3f), R("Forearm Stretch", -.3f),
        S("Spine Front-Back", .05f), S("Chest Twist Left-Right", -.1f) };
    // ---- base pose capture ---------------------------------------------------------------
    static float[] baseMuscles; static Vector3 basePos; static Quaternion baseRot;
    static AnimationClip StanceClip() {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/WorldAssets/CombatAttack.fbx")) if (o is AnimationClip c && !c.name.StartsWith("__")) return c;
        return null;
    }
    static void CaptureBase() {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab");
        var go = Object.Instantiate(prefab); var animator = go.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = null;
        var stance = StanceClip(); stance.SampleAnimation(go, stance.length * .5f);
        var h = new HumanPoseHandler(animator.avatar, animator.transform); var pose = new HumanPose(); h.GetHumanPose(ref pose);
        baseMuscles = (float[])pose.muscles.Clone(); basePos = pose.bodyPosition; baseRot = pose.bodyRotation;
        Object.DestroyImmediate(go);
    }

    static AnimationClip Make(string name, List<Key> keys, bool loop, float amp = 1f) {
        var curves = Names.Select(_ => new AnimationCurve()).ToArray();
        foreach (var k in keys) {
            var m = (float[])baseMuscles.Clone();
            foreach (var (n, v) in Stance.Concat(k.d.Select(x => (x.Item1, x.Item2 * amp)))) { int ix = System.Array.IndexOf(Names, n); if (ix < 0) Debug.LogWarning("Unknown muscle " + n); else m[ix] += n.EndsWith("Arm Front-Back") ? -v : v; }
            for (int i = 0; i < Names.Length; i++) curves[i].AddKey(new Keyframe(k.t, Mathf.Clamp(m[i], -1.3f, 1.3f)));
        }
        var clip = new AnimationClip { name = name, frameRate = 60 };
        void Put(string prop, AnimationCurve c) {
            for (int k = 0; k < c.length; k++) { AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), c);
        }
        for (int i = 0; i < Names.Length; i++) Put(Names[i], curves[i]);
        float len = keys.Last().t;
        foreach (var (prop, val) in new[] { ("RootT.x", basePos.x), ("RootT.z", basePos.z) })
            Put(prop, AnimationCurve.Constant(0, len, val));
        if (keys.Any(k => k.pitch != 0)) {
            var qx = new AnimationCurve(); var qy = new AnimationCurve(); var qz = new AnimationCurve(); var qw = new AnimationCurve();
            foreach (var k in keys) { var q = Quaternion.Euler(k.pitch, 0, k.tilt) * baseRot; qx.AddKey(k.t, q.x); qy.AddKey(k.t, q.y); qz.AddKey(k.t, q.z); qw.AddKey(k.t, q.w); }
            Put("RootQ.x", qx); Put("RootQ.y", qy); Put("RootQ.z", qz); Put("RootQ.w", qw);
        } else foreach (var (prop, val) in new[] { ("RootQ.x", baseRot.x), ("RootQ.y", baseRot.y), ("RootQ.z", baseRot.z), ("RootQ.w", baseRot.w) })
            Put(prop, AnimationCurve.Constant(0, len, val));
        Put("RootT.y", AnimationCurve.Constant(0, len, basePos.y));
        Calibrate(clip, keys, keys.Any(k => k.pitch != 0));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop; settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    // RootT.y per key so the lowest foot stays at the base stance's ankle height.
    static void Calibrate(AnimationClip clip, List<Key> keys, bool contactSet = false) {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab");
        var go = Object.Instantiate(prefab); var animator = go.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = null;
        var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var stance = StanceClip(); stance.SampleAnimation(go, stance.length * .5f);
        float Low() {
            if (!contactSet) return Mathf.Min(lf.position.y, rf.position.y);
            float m = 100;
            void C(HumanBodyBones b, float r) { var tr = animator.GetBoneTransform(b); if (tr) m = Mathf.Min(m, tr.position.y - r); }
            C(HumanBodyBones.LeftFoot, .07f); C(HumanBodyBones.RightFoot, .07f); C(HumanBodyBones.LeftHand, .04f); C(HumanBodyBones.RightHand, .04f);
            C(HumanBodyBones.Head, .12f); C(HumanBodyBones.Hips, .14f); C(HumanBodyBones.Chest, .12f);
            return m;
        }
        float target = Low();
        float scale = Mathf.Max(.5f, animator.humanScale);
        var ys = keys.Select(_ => basePos.y).ToArray();
        for (int pass = 0; pass < 6; pass++) {
            var curve = new AnimationCurve(); for (int k = 0; k < keys.Count; k++) curve.AddKey(new Keyframe(keys[k].t, ys[k]));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"), curve);
            for (int k = 0; k < keys.Count; k++) { clip.SampleAnimation(go, keys[k].t); ys[k] -= (Low() - target) / scale; }
        }
        var final = new AnimationCurve(); for (int k = 0; k < keys.Count; k++) final.AddKey(new Keyframe(keys[k].t, ys[k]));
        for (int k = 0; k < final.length; k++) { AnimationUtility.SetKeyLeftTangentMode(final, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(final, k, AnimationUtility.TangentMode.ClampedAuto); }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"), final);
        Object.DestroyImmediate(go);
    }

    public static Dictionary<string, AnimationClip> BuildAll() {
        CaptureBase();
        Directory.CreateDirectory(Dir);
        var result = new Dictionary<string, AnimationClip>();
        var specs = new (string, List<Key>, bool, float)[] { ("Attack1", Attack(1), false, 1.8f), ("Attack2", Attack(2), false, 1.8f), ("Attack3", Attack(3), false, 1.6f), ("Guard", Guard(), true, 1f), ("Cast", Cast(), true, 1f), ("Roll", Roll(), false, 1f) };
        foreach (var (name, keys, loop, amp) in specs) {
            var path = Dir + name + ".anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(Make(name, keys, loop, amp), path);
            result[name] = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    // Replaces motions of the combat states in an existing controller; called from WorldAssetBuild.
    public static void Apply(AnimatorController controller) {
        var clips = BuildAll();
        foreach (var s in controller.layers[0].stateMachine.states) {
            string n = s.state.name;
            if (n.StartsWith("Attack") && clips.TryGetValue(n, out var attack)) { s.state.motion = attack; s.state.speed = attack.length / (n == "Attack3" ? .72f : .58f); }
            else if (n == "Guard") s.state.motion = clips["Guard"];
            else if (n == "Cast") s.state.motion = clips["Cast"];
            else if (n == "Roll") { s.state.motion = clips["Roll"]; s.state.speed = clips["Roll"].length / CombatTuning.RollDuration; }
        }
        AddUpperLayer(controller, clips);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        Debug.Log("COMBAT_CLIPS_APPLIED");
    }
    // Layer 1 plays attack / guard / cast on the upper body only, so the legs keep walking or running underneath.
    static void AddUpperLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips) {
        var maskPath = Dir + "UpperBody.mask"; var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (!mask) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, maskPath); }
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart))) {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        EditorUtility.SetDirty(mask);
        for (int i = controller.layers.Length - 1; i >= 1; i--) controller.RemoveLayer(i);
        controller.AddLayer("Upper");
        var layers = controller.layers; layers[1].avatarMask = mask; layers[1].defaultWeight = 0; layers[1].blendingMode = AnimatorLayerBlendingMode.Override; controller.layers = layers;
        var sm = controller.layers[1].stateMachine; foreach (var st in sm.states) sm.RemoveState(st.state);
        var empty = sm.AddState("Empty"); sm.defaultState = empty;
        for (int i = 1; i <= 3; i++) { var s = sm.AddState("UpAttack" + i); s.motion = clips["Attack" + i]; s.speed = clips["Attack" + i].length / (i == 3 ? .72f : .58f); }
        sm.AddState("UpGuard").motion = clips["Guard"]; sm.AddState("UpCast").motion = clips["Cast"];
    }
    public static void ApplyToProject() => Apply(AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Resources/Generated/Locomotion.controller"));
}
