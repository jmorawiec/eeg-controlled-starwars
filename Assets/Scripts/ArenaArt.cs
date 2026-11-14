using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ForceDemo {
public static class ArenaArt {
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    public static Color Cyan = new Color(.25f,.88f,1), Violet = new Color(.72f,.38f,1), Gold = new Color(1,.73f,.36f), Red = new Color(1,.25f,.3f);
    public static Material Mat(Color color, bool unlit = false) {
        string key = color.ToString("F3") + unlit;
        if (materials.TryGetValue(key, out Material existing) && existing) return existing;
        var m = new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard")); m.color = color;
        if (!unlit) { m.SetFloat("_Glossiness", .32f); m.SetFloat("_Metallic", .5f); }
        materials[key] = m; return m;
    }
    public static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Transform parent = null, bool unlit = false) {
        var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(parent, false);
        g.transform.localPosition = position; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = Mat(color,unlit);
        var collider = g.GetComponent<Collider>(); if(collider) Object.Destroy(collider);
        return g;
    }
    public static LineRenderer Line(string name, Vector3[] points, Color color, float width, Transform parent = null, bool loop = false) {
        var g = new GameObject(name); g.transform.SetParent(parent, false); var l = g.AddComponent<LineRenderer>();
        l.sharedMaterial = Mat(color,true); l.useWorldSpace = true; l.positionCount = points.Length; l.SetPositions(points);
        l.startWidth = l.endWidth = width; l.loop = loop; l.numCornerVertices = 2; l.numCapVertices = 3;
        l.shadowCastingMode = ShadowCastingMode.Off; return l;
    }
    public static LineRenderer Ring(Vector3 center, float radius, Color color, float width = .035f, Transform parent = null) {
        var points = new Vector3[96]; for (int i=0;i<points.Length;i++) { float a = i * Mathf.PI * 2 / points.Length; points[i] = center + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius; }
        return Line("Energy ring",points,color,width,parent,true);
    }
    public static void Build(Transform root) { WorldBuilder.Build(root); }
    public static Transform Character(string name, Vector3 position, Color accent, Transform root, bool boss = false, bool player = false) {
        return CharacterVisual.Create(name,position,accent,root,boss,player);
    }

}
public class ForceEffect : MonoBehaviour {
    public float duration=.5f, age, from=1, to=1; public bool scaleEffect;
    public LineRenderer line;
    void Update() {
        age += Time.deltaTime; float t=Mathf.Clamp01(age/duration);
        if(scaleEffect) transform.localScale=Vector3.one*Mathf.Lerp(from,to,t);
        if(line) { line.startWidth=line.endWidth=Mathf.Lerp(.13f,.005f,t); }
        if(t>=1) Destroy(gameObject);
    }
}
}
