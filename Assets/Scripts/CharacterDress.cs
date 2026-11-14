using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ForceDemo {
public enum DressKind { None, Hero, Sentinel, Ranged, Training, Boss, Mentor }

// Gives each character class its own silhouette: cloaks with simple cloth physics, visors, pauldrons, crests, glowing cores.
public static class CharacterDress {
    public static DressKind KindOf(string name, bool boss, bool player) {
        if (player) return DressKind.Hero; if (boss) return DressKind.Boss; string n = name.ToLowerInvariant();
        if (n.Contains("mentor")) return DressKind.Mentor; if (n.Contains("training")) return DressKind.Training; if (n.Contains("ranged")) return DressKind.Ranged; return DressKind.Sentinel;
    }
    public static Color Tint(DressKind k) {
        switch (k) {
            case DressKind.Sentinel: return new Color(.30f, .27f, .27f); case DressKind.Ranged: return new Color(.27f, .22f, .34f);
            case DressKind.Training: return new Color(.62f, .7f, .76f); case DressKind.Boss: return new Color(.13f, .115f, .1f); case DressKind.Mentor: return new Color(.82f, .72f, .58f);
            default: return Color.white;
        }
    }
    static Color Glow(DressKind k) => k == DressKind.Ranged ? new Color(.85f, .35f, 1.5f) : k == DressKind.Training ? new Color(.3f, 1.2f, 1.6f) : k == DressKind.Boss ? new Color(1.9f, 1.2f, .35f) : new Color(2f, .35f, .25f);

    public static void Apply(CharacterVisual v,DressKind kind){
        if(!v.animator||!v.animator.isHuman)return;v.kind=kind;
        var renderers=new List<Renderer>();
        foreach(var r in v.body.GetComponentsInChildren<Renderer>()){
            if(v.weapon&&r.transform.IsChildOf(v.weapon))continue;
            string name=r.name.ToLowerInvariant();
            bool skin=name.Contains("head")||name.Contains("hair")||name.Contains("eye")||name.Contains("teeth");
            if(skin)continue;
            renderers.Add(r);
        }
        v.tintRenderers=renderers.ToArray();
        v.baseTint=kind==DressKind.Hero?Color.white:kind==DressKind.Boss?new Color(.30f,.34f,.38f):kind==DressKind.Training?new Color(.80f,.87f,.86f):kind==DressKind.Ranged?new Color(.30f,.34f,.40f):kind==DressKind.Mentor?new Color(.83f,.77f,.64f):new Color(.49f,.56f,.61f);
        var chest=v.animator.GetBoneTransform(HumanBodyBones.UpperChest)??v.animator.GetBoneTransform(HumanBodyBones.Chest);
        if(kind==DressKind.Boss)v.cape=ClothCape.Create(v,chest,1.22f,.42f,.68f,new Color(.10f,.07f,.075f));
        if(kind==DressKind.Sentinel||kind==DressKind.Boss){
            var head=v.animator.GetBoneTransform(HumanBodyBones.Head);
            Part(v,"Recessed visor",PrimitiveType.Cube,new Vector3(.12f,.014f,.012f),new Color(1.8f,.12f,.08f),true,head,new Vector3(0,.025f,.113f));
        }
    }

    static Material cloth;
    static Material Cloth(Color c) { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", .12f); m.SetFloat("_Metallic", 0); return m; }
    // Hooded caster (faceless, glowing eyes) or the mentor's robe (hood down, bright mantle).
    static void Robe(CharacterVisual v, DressKind kind, Transform head, Transform chest, Transform spine, Color glow) {
        bool dark = kind == DressKind.Ranged; Color robe = dark ? new Color(.1f, .065f, .13f) : new Color(.86f, .8f, .66f); var mat = Cloth(robe);
        Transform neck = chest ? chest : spine;
        if (dark) {
            var hood = Part(v, "Hood", PrimitiveType.Sphere, new Vector3(.36f, .41f, .4f), robe, false, head, new Vector3(0, .035f, -.025f)); if (hood) hood.GetComponent<Renderer>().sharedMaterial = mat;
            Part(v, "Eye L", PrimitiveType.Cube, new Vector3(.045f, .018f, .02f), glow, true, head, new Vector3(-.052f, .03f, .17f));
            Part(v, "Eye R", PrimitiveType.Cube, new Vector3(.045f, .018f, .02f), glow, true, head, new Vector3(.052f, .03f, .17f));
            var peak = Part(v, "Hood peak", PrimitiveType.Capsule, new Vector3(.12f, .13f, .12f), robe, false, head, new Vector3(0, .17f, -.1f)); if (peak) { peak.GetComponent<Renderer>().sharedMaterial = mat; peak.rot = Quaternion.Euler(-40, 0, 0); }
        } else {
            var cowl = Part(v, "Cowl", PrimitiveType.Sphere, new Vector3(.33f, .2f, .26f), robe, false, head, new Vector3(0, -.12f, -.11f)); if (cowl) cowl.GetComponent<Renderer>().sharedMaterial = mat;
            Part(v, "Circlet", PrimitiveType.Cylinder, new Vector3(.2f, .006f, .2f), new Color(1.6f, 1.2f, .5f), true, head, new Vector3(0, .1f, -.012f));
        }
        var mantle = Part(v, "Mantle", PrimitiveType.Sphere, new Vector3(.5f, .13f, .3f), robe, false, neck, new Vector3(0, .13f, -.02f)); if (mantle) mantle.GetComponent<Renderer>().sharedMaterial = mat;
        var sash = Part(v, "Sash", PrimitiveType.Cylinder, new Vector3(.4f, .035f, .3f), dark ? new Color(.45f, .12f, .6f) : new Color(.7f, .55f, .2f), false, spine, new Vector3(0, -.02f, 0)); if (sash) sash.rot = Quaternion.Euler(0, 0, 0);
    }
    static BoneFollow Part(CharacterVisual v, string name, PrimitiveType type, Vector3 scale, Color color, bool unlit, Transform bone, Vector3 local) {
        if (!bone) return null; var g = GameObject.CreatePrimitive(type); g.name = name; Object.Destroy(g.GetComponent<Collider>()); g.transform.SetParent(v.transform, false); g.transform.localScale = scale;
        var rend = g.GetComponent<Renderer>(); rend.sharedMaterial = ArenaArt.Mat(color, unlit); rend.shadowCastingMode = unlit ? ShadowCastingMode.Off : ShadowCastingMode.On;
        var f = g.AddComponent<BoneFollow>(); f.bone = bone; f.root = v.transform; f.local = local; return f;
    }
}

public class BoneFollow : MonoBehaviour {
    public Transform bone, root; public Vector3 local; public Quaternion rot = Quaternion.identity;
    void LateUpdate() { if (!bone || !root) { Destroy(gameObject); return; } transform.position = bone.position + root.TransformDirection(local) * root.lossyScale.x; transform.rotation = root.rotation * rot; }
}

// A hanging cloak: a column of damped points hanging from the shoulders, trailing behind movement.
public class ClothCape : MonoBehaviour {
    Transform anchor; Vector3[] p; Vector3[] centers; Mesh mesh; GameObject meshObject; Material mat; Vector3 previous; int rows = 8; const int cols = 13; float length, top, bottom; CharacterVisual visual; bool snap = true, front; Vector3[] vertices;
    public static ClothCape Create(CharacterVisual v, Transform anchor, float length, float top, float bottom, Color color, bool front = false) {
        if (!anchor) return null; var c = v.gameObject.AddComponent<ClothCape>(); c.anchor = anchor; c.front = front; c.length = length; c.top = top; c.bottom = bottom; c.visual = v;
        c.mat = new Material(Shader.Find("Force/Foliage")); c.mat.mainTexture = Resources.Load<Texture2D>("WorldAssets/fabric_pattern_07_albedo"); c.mat.SetFloat("_Wind", 0); c.mat.SetFloat("_Cutoff", .1f); c.SetColor(color); return c;
    }
    public void SetColor(Color c) { if (mat) mat.color = c; }
    public bool Visible { set { if (meshObject) meshObject.SetActive(value); } }
    void Start() {
        float scale = transform.lossyScale.x; rows = 16; p = new Vector3[rows]; centers = new Vector3[rows]; vertices = new Vector3[rows * cols];
        mesh = new Mesh { name = "Cloak" }; mesh.MarkDynamic(); var uv = new Vector2[rows * cols]; var tris = new List<int>();
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) {
            uv[r * cols + c] = new Vector2(c / (cols - 1f), 1 - r / (rows - 1f));
            if (r < rows - 1 && c < cols - 1) { int i = r * cols + c; tris.Add(i); tris.Add(i + cols); tris.Add(i + 1); tris.Add(i + 1); tris.Add(i + cols); tris.Add(i + cols + 1); }
        }
        mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(tris, 0);
        meshObject = new GameObject("Cloak"); meshObject.layer=visual.gameObject.layer; var mf = meshObject.AddComponent<MeshFilter>(); mf.sharedMesh = mesh; var mr = meshObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
        previous = transform.position;
    }
    void LateUpdate() {
        if (!anchor || mesh == null) return; float dt = Mathf.Max(Time.deltaTime, .001f); float scale = transform.lossyScale.x;
        meshObject.SetActive(visual.roll <= 0);
        Vector3 vel = (transform.position - previous) / dt; previous = transform.position; if (vel.magnitude > 12) vel = Vector3.zero;
        Vector3 back = front ? transform.forward : -transform.forward; Vector3 anchorPos = anchor.position + back * (front ? .14f : .13f) * scale + Vector3.up * (front ? -.02f : .1f) * scale;
        float seg = length * scale / (rows - 1);
        if (snap || (p[0] - anchorPos).magnitude > 2.5f) { for (int i = 0; i < rows; i++) p[i] = anchorPos + Vector3.down * seg * i + back * .02f * i * scale; snap = false; }
        p[0] = anchorPos;
        for (int i = 1; i < rows; i++) {
            float f = i / (rows - 1f);
            Vector3 target = p[i - 1] + Vector3.down * seg + back * seg * .18f - vel * dt * (1.8f + f * 3.5f) + transform.right * Mathf.Sin(Time.time * 1.6f + i * .7f) * .012f * scale * f;
            p[i] = Vector3.Lerp(p[i], target, 1 - Mathf.Exp(-dt * (16 - f * 6)));
            Vector3 d = p[i] - p[i - 1]; p[i] = p[i - 1] + d.normalized * seg;
            // Keep the cloth outside the torso: never forward of the back plane.
            float behind = Vector3.Dot(p[i] - anchorPos, back); if (behind < .03f * scale) p[i] += back * (.03f * scale - behind);
        }
        Vector3 right = transform.right;
        for (int r = 0; r < rows; r++) {
            float f = r / (rows - 1f); float w = Mathf.Lerp(top, bottom, f) * .5f * scale; Vector3 ripple = back * Mathf.Sin(Time.time * 2.2f + r * .9f) * .015f * f * scale;
            for (int c = 0; c < cols; c++) { float u = c / (cols - 1f) * 2 - 1; vertices[r * cols + c] = p[r] + right * (u * w) + back * (1 - u * u) * .035f * scale + ripple; }
        }
        mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }
    void OnDestroy() { if (meshObject) Destroy(meshObject); if (mat) Destroy(mat); }
}
}
