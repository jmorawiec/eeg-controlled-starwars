using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Imports the CC0 Poly Haven set used by the "polish" pass: ruin / floor / cliff surfaces, props,
// a sunset sky and the sun direction measured from that sky, so light and shadows match the HDR.
public static class PolishAssets {
    const string Source = "Assets/Resources/WorldAssets/", Output = "Assets/Resources/Generated/";
    static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Source + n);
    static void Save(Object asset, string name) {
        string p = Output + name; var old = AssetDatabase.LoadAssetAtPath<Object>(p);
        if (old) { EditorUtility.CopySerialized(asset, old); Object.DestroyImmediate(asset); } else AssetDatabase.CreateAsset(asset, p);
    }
    static Texture2D Albedo(string id) => Tex(id + "_albedo.png") ? Tex(id + "_albedo.png") : Tex(id + "_albedo.jpg");

    static void Surface(string name, string id, Color tint, float gloss, float bump = 1f, float metallic = 0f) {
        var m = new Material(Shader.Find("Standard")) { color = tint, enableInstancing = true };
        m.mainTexture = Tex(id + "_albedo.jpg"); m.SetTexture("_BumpMap", Tex(id + "_normal.jpg")); m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_BumpScale", bump); m.SetFloat("_Glossiness", gloss); m.SetFloat("_Metallic", metallic);
        Save(m, name + ".mat");
    }

    static void ConfigureTextures() {
        foreach (var p in AssetDatabase.FindAssets("t:Texture", new[] { Source.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath)) {
            var ti = AssetImporter.GetAtPath(p) as TextureImporter; if (ti == null) continue;
            bool normal = p.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool plant = p.Contains("shrub_") || p.Contains("celandine");
            if (!(plant || p.Contains("monastery") || p.Contains("mossy_cobble") || p.Contains("rabdentse") || p.Contains("mossy_stone") || p.Contains("forest_ground") || p.Contains("rock_wall_02") || p.Contains("stony_dirt") || p.Contains("sunset"))) continue;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal; ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.maxTextureSize = 2048;
            ti.alphaIsTransparency = plant && !normal; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (p.Contains("sunset")) { ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; }
            ti.SaveAndReimport();
        }
    }

    static void Scan(string id, bool foliage, bool collider, Color tint, float gloss = .12f) {
        string p = Source + id + ".fbx"; if (!File.Exists(p)) { Debug.LogWarning("Missing " + p); return; }
        var importer = (ModelImporter)AssetImporter.GetAtPath(p);
        importer.importAnimation = false; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.meshCompression = ModelImporterMeshCompression.Medium; importer.isReadable = true; importer.SaveAndReimport();
        var mat = new Material(Shader.Find(foliage ? "Force/Foliage" : "Standard")) { color = tint, enableInstancing = true };
        mat.mainTexture = Albedo(id);
        if (foliage) { mat.SetFloat("_Cutoff", .42f); mat.SetFloat("_Wind", .05f); }
        else { var n = Tex(id + "_normal.png"); if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); } mat.SetFloat("_Glossiness", gloss); }
        Save(mat, id + ".mat"); mat = AssetDatabase.LoadAssetAtPath<Material>(Output + id + ".mat");
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p)); model.name = "Scan";
        var wrapper = new GameObject(id); model.transform.SetParent(wrapper.transform, false);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        var renderers = model.GetComponentsInChildren<Renderer>(); Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
        float size = foliage ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        model.transform.localScale /= size; model.transform.localPosition = (model.transform.localPosition - new Vector3(b.center.x, b.min.y, b.center.z)) / size;
        if (collider) foreach (var r in renderers) { var f = r.GetComponent<MeshFilter>(); if (!f) continue; var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = f.sharedMesh; }
        PrefabUtility.SaveAsPrefabAsset(wrapper, Output + id + ".prefab"); Object.DestroyImmediate(wrapper);
        Debug.Log("POLISH_PROP " + id + " renderers=" + renderers.Length);
    }

    static void GlowAssets() {
        var t = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float r = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f; float a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.4f); t.SetPixel(x, y, new Color(1, 1, 1, a)); }
        File.WriteAllBytes(Output + "GlowSprite.png", t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(Output + "GlowSprite.png");
        var ti = (TextureImporter)AssetImporter.GetAtPath(Output + "GlowSprite.png"); ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        var m = new Material(Shader.Find("Force/Glow")); m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Output + "GlowSprite.png"); m.SetColor("_Color", Color.white); m.SetFloat("_Intensity", 1f); Save(m, "GlowAdd.mat");
        var rune = new Material(Shader.Find("Unlit/Color")) { color = new Color(.4f, 1.8f, 2.4f) }; Save(rune, "RuneGlow.mat");
    }
    static void Sky() {
        var tex = Tex("sunset.hdr"); if (!tex) { Debug.LogWarning("sunset.hdr missing"); return; }
        var sky = new Material(Shader.Find("Skybox/Panoramic")); sky.SetTexture("_MainTex", tex); sky.SetFloat("_Exposure", 1.05f); sky.SetFloat("_Rotation", 0); sky.SetColor("_Tint", new Color(.5f, .5f, .5f, .5f));
        Save(sky, "SunsetSky.mat"); sky = AssetDatabase.LoadAssetAtPath<Material>(Output + "SunsetSky.mat");
        // Measure the brightest region: that is the sun direction in texture space (Skybox/Panoramic mapping).
        var px = tex.GetPixels(); int w = tex.width, h = tex.height; float best = 0; int bi = 0;
        for (int i = 0; i < px.Length; i++) { float l = px[i].r * .2126f + px[i].g * .7152f + px[i].b * .0722f; if (l > best) { best = l; bi = i; } }
        Vector3 acc = Vector3.zero; Color col = Color.black; float wsum = 0;
        for (int i = 0; i < px.Length; i++) {
            float l = px[i].r * .2126f + px[i].g * .7152f + px[i].b * .0722f; if (l < best * .5f) continue;
            float u = (i % w + .5f) / w, v = (i / w + .5f) / h; float lon = (.5f - u) * Mathf.PI * 2, lat = (v - .5f) * Mathf.PI;
            var d = new Vector3(Mathf.Cos(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Sin(lon) * Mathf.Cos(lat)); acc += d * l; col += px[i] * l; wsum += l;
        }
        Vector3 dir = (acc / wsum).normalized; col /= wsum; float m = Mathf.Max(col.r, Mathf.Max(col.g, col.b)); col = new Color(col.r / m, col.g / m, col.b / m);
        // Skybox/Panoramic rotates the lookup so that world azimuth = texture azimuth + _Rotation. Put the sun just right of the gate (+Z).
        float texAz = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, wantAz = 18f; float rotation = Mathf.Repeat(wantAz - texAz, 360f);
        sky.SetFloat("_Rotation", rotation); EditorUtility.SetDirty(sky);
        float elev = Mathf.Asin(Mathf.Clamp(dir.y, -1, 1)); Vector3 world = new Vector3(Mathf.Sin(wantAz * Mathf.Deg2Rad) * Mathf.Cos(elev), Mathf.Sin(elev), Mathf.Cos(wantAz * Mathf.Deg2Rad) * Mathf.Cos(elev));
        File.WriteAllText(Output + "SunDirection.txt", world.x.ToString("F4") + " " + world.y.ToString("F4") + " " + world.z.ToString("F4") + " " + col.r.ToString("F3") + " " + col.g.ToString("F3") + " " + col.b.ToString("F3"));
        AssetDatabase.ImportAsset(Output + "SunDirection.txt");
        Debug.Log("POLISH_SUN texAz=" + texAz + " rotation=" + rotation + " world=" + world + " color=" + col + " peak=" + best);
        var ti = (TextureImporter)AssetImporter.GetAtPath(Source + "sunset.hdr"); ti.isReadable = false; ti.SaveAndReimport();
    }

    public static void Build() {
        ConfigureTextures();
        Surface("PlazaStone", "monastery_stone_floor", new Color(.92f, .9f, .86f), .22f, 1.1f);
        Surface("CobbleMoss", "mossy_cobblestone", new Color(.9f, .95f, .88f), .18f, 1.1f);
        Surface("RuinWall", "rabdentse_ruins_wall", new Color(.95f, .92f, .88f), .12f, 1.2f);
        Surface("MossWall", "mossy_stone_wall", new Color(.92f, .96f, .9f), .12f, 1.2f);
        Surface("ValleyGround", "forest_ground_06", new Color(.78f, .82f, .7f), .06f, 1f);
        Surface("DirtPath", "stony_dirt_path", new Color(.85f, .8f, .72f), .1f, 1f);
        Surface("CliffRock", "rock_wall_02", new Color(.8f, .78f, .76f), .1f, 1.3f);
        Scan("rock_moss_set_02", false, true, Color.white);
        Scan("rock_face_01", false, true, Color.white);
        Scan("dead_tree_trunk_02", false, true, Color.white, .06f);
        Scan("tree_stump_01", false, true, Color.white, .06f);
        Scan("gothic_statue", false, true, new Color(.86f, .88f, .86f), .2f);
        Scan("marble_bust_01", false, true, Color.white, .35f);
        Scan("wooden_lantern_01", false, false, Color.white, .3f);
        Scan("shrub_01", true, false, new Color(.85f, 1f, .8f));
        Scan("shrub_03", true, false, new Color(.9f, 1f, .8f));
        Scan("celandine_01", true, false, Color.white);
        GlowAssets();
        var water = new Material(Shader.Find("Force/Water")); Save(water, "Water.mat");
        Sky();
        AssetDatabase.SaveAssets(); Debug.Log("POLISH_ASSETS_READY");
    }
    public static void Run() { Build(); }
}
