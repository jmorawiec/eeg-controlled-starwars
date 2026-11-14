using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ForceDemo {
// The Valley of Echoes: a sunken ceremonial plaza ringed by a broken colonnade and amphitheatre tiers,
// with a processional path, reflecting channels and a glowing rift gate backlit by a low sunset.
public static class WorldBuilder {
    public static Material Stone, Moss, Ground, Plaza, Cobble, Rune, GlowAdd;
    public static Vector3 SunDirection = new Vector3(.3f, .32f, .9f).normalized; public static Color SunColor = new Color(1, .74f, .5f);
    static Transform root; static System.Random rnd;
    static readonly Dictionary<string,GameObject[]> variants=new Dictionary<string,GameObject[]>();
    const float Floor = -.075f;
    static Material Load(string n) => Resources.Load<Material>("Generated/" + n);
    static float R() => (float)rnd.NextDouble();
    static float R(float a, float b) => a + (b - a) * R();

    public static void Build(Transform parent) {
        root = parent; rnd = new System.Random(31);
        Stone = Load("RuinWall") ?? Load("Stone"); Moss = Load("MossWall") ?? Load("Moss"); Ground = Load("ValleyGround") ?? Load("Ground");
        Plaza = Load("PlazaStone") ?? Stone; Cobble = Load("CobbleMoss") ?? Moss; Rune = Load("RuneGlow"); GlowAdd = Load("GlowAdd");
        Lighting(); Terrain(); PlazaFloor(); Runes(); Gate(); Colonnade(); Tiers(); GardenCourts(); Statues(); Braziers(); Lanterns(); Channels(); Rubble(); Flora(); Atmosphere();
        // Mentor plinth
        StoneBlock("Mentor meditation plinth", new Vector3(0, .1f, 11), new Vector3(3f, .24f, 3f), Cobble);
        ArenaArt.Ring(new Vector3(0, .24f, 11), 1.25f, new Color(.3f, 1.4f, 1.9f), .03f, root);
    }

    // ---------------------------------------------------------------- lighting
    static void Lighting() {
        var ta = Resources.Load<TextAsset>("Generated/SunDirection");
        if (ta != null) { var p = ta.text.Split(' '); if (p.Length >= 6) { SunDirection = new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)).normalized;
            SunColor = new Color(float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture)); } }
        // Keep the warm tint but never let a washed-out measurement kill the golden-hour feel.
        SunColor = new Color(1,.86f,.69f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = .013f; RenderSettings.fogColor = new Color(.26f,.34f,.40f);
        RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.49f,.61f,.73f); RenderSettings.ambientEquatorColor = new Color(.31f,.39f,.45f); RenderSettings.ambientGroundColor = new Color(.15f,.18f,.20f);
        RenderSettings.skybox = Load("SunsetSky") ?? Load("DawnSky"); RenderSettings.reflectionIntensity = .8f; RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        var sun = new GameObject("Golden hour sun").AddComponent<Light>(); sun.transform.SetParent(root); sun.type = LightType.Directional; sun.color = SunColor; sun.intensity = 1.15f;
        var flat = new Vector3(SunDirection.x, 0, SunDirection.z).normalized; var lightDir = (flat * Mathf.Cos(18 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(18 * Mathf.Deg2Rad)).normalized; // the real sun sits lower in the sky; lift the light so the arena stays readable
        sun.transform.rotation = Quaternion.LookRotation(-lightDir); sun.cullingMask=~(1<<29);sun.shadows = LightShadows.Soft; sun.shadowStrength = .72f; sun.shadowBias = .03f; sun.shadowNormalBias = .23f; RenderSettings.sun = sun;
        var fill = new GameObject("Cool sky fill").AddComponent<Light>(); fill.transform.SetParent(root); fill.type = LightType.Directional; fill.color = new Color(.45f, .58f, 1f); fill.intensity = .42f; fill.cullingMask=~(1<<29);fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(SunDirection.x, -.35f, SunDirection.z).normalized);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.34f,.40f,.47f);
        var probe=new UnityEngine.Rendering.SphericalHarmonicsL2();probe.AddAmbientLight(new Color(.24f,.30f,.37f));RenderSettings.ambientProbe=probe;
        QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.shadowDistance = 45; QualitySettings.shadowCascades = 2; QualitySettings.shadowProjection = ShadowProjection.StableFit; QualitySettings.antiAliasing = 2; QualitySettings.pixelLightCount = 4;
    }

    // ---------------------------------------------------------------- terrain & plaza
    static void GardenCourts(){
        // Broken cover makes three readable lanes: central approach, western garden, eastern archive.
        foreach(var p in new[]{new Vector3(-9,0,0),new Vector3(9,0,2),new Vector3(-10,0,8)}){
            StoneBlock("Fractured cover plinth",p+Vector3.up*.17f,new Vector3(2.6f,.45f,2.5f),Moss,17);
            Column(p,2.8f,7);
        }
        foreach(int side in new[]{-1,1}){
            Vector3 c=new Vector3(side*16,0,1);
            for(int i=0;i<5;i++){
                Vector3 p=c+new Vector3(side*(i%2)*.5f,0,-5+i*2.6f);
                StoneBlock("Garden retaining wall",p+Vector3.up*.38f,new Vector3(1.1f,.85f,2.4f),Moss,side*8);
                Prop(i%2==0?"shrub_01":"fern_02",p+Vector3.up*.7f,Vector3.one*1.2f,i*43);
            }
            for(int i=0;i<9;i++){
                Vector3 p=new Vector3(side*(12.6f+Mathf.Sin(i*.5f)*.8f),Floor+.01f,-9+i*2);
                var b=StoneBlock("Pilgrim stepping stone",p,new Vector3(2.8f,.09f,1.8f),Cobble,i*3);b.GetComponent<BoxCollider>().enabled=false;
            }
            Prop("gothic_statue",new Vector3(side*14,0,11),Vector3.one*4.5f,side<0?30:-30);
        }
        // A ruined aqueduct and a pair of waterfalls frame the gate in the far view.
        for(int i=0;i<7;i++){
            float x=-42+i*14;
            StoneBlock("Aqueduct pier",new Vector3(x,8.5f,58),new Vector3(3.6f,18,5),Stone,i%2*3);
            if(i<6){
                for(int k=0;k<11;k++){
                    float a=(k+.5f)/11f*Mathf.PI;Vector3 center=new Vector3(x+7+Mathf.Cos(a)*6.8f,17+Mathf.Sin(a)*6.8f,58);
                    var block=StoneBlock("Aqueduct arch voussoir",center,new Vector3(2.15f,1.5f,4.6f),k%3==0?Moss:Stone);
                    block.transform.rotation=Quaternion.Euler(0,0,a*Mathf.Rad2Deg-90);
                }
            }
        }
        foreach(float x in new[]{-24f,28f}){
            var waterfall=GameObject.CreatePrimitive(PrimitiveType.Quad);waterfall.name="Distant cascading waterfall";waterfall.transform.SetParent(root);waterfall.transform.position=new Vector3(x,12,51);waterfall.transform.localScale=new Vector3(4,26,1);Object.Destroy(waterfall.GetComponent<Collider>());
            var mat=Load("Waterfall");waterfall.GetComponent<Renderer>().sharedMaterial=mat;waterfall.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            Motes(new Vector3(x,1,50),new Vector3(5,.2f,4),new Color(.5f,.7f,.75f,.12f),4,5,2.5f,.35f);
        }
    }

    static float Height(float x, float z) {
        float d = Mathf.Sqrt(x * x + z * z);
        float bowl = Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - 26f) / 30f)) * 15f * (.65f + Mathf.PerlinNoise(x * .02f + 3, z * .02f + 7) * .7f);
        float lumps = Mathf.PerlinNoise(x * .08f, z * .08f) * Mathf.Clamp01((d - 22f) / 8f) * 1.3f;
        return bowl + lumps;
    }
    static void Terrain() {
        const int n = 171; const float size = 240; var vertices = new Vector3[n * n]; var uv = new Vector2[vertices.Length]; var triangles = new List<int>();
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) {
            int i = z * n + x; float px = (x / (n - 1f) - .5f) * size, pz = (z / (n - 1f) - .5f) * size;
            vertices[i] = new Vector3(px, -.08f + Height(px, pz), pz); uv[i] = new Vector2(px / 6, pz / 6);
            if (x < n - 1 && z < n - 1) { triangles.Add(i); triangles.Add(i + n); triangles.Add(i + 1); triangles.Add(i + 1); triangles.Add(i + n); triangles.Add(i + n + 1); }
        }
        var mesh = new Mesh { name = "Valley terrain", indexFormat = IndexFormat.UInt32 }; mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        var g = new GameObject("Overgrown valley floor"); g.transform.SetParent(root); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = Ground; g.AddComponent<MeshCollider>().sharedMesh = mesh;
    }
    class MB {
        public List<Vector3> v = new List<Vector3>(), nm = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>();
        // a->b and a->d span the quad; the face is flipped so its normal agrees with `hint`.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud) {
            Vector3 n = Vector3.Cross(b - a, d - a); if (Vector3.Dot(n, hint) < 0) { var tv = b; b = d; d = tv; var tu = ub; ub = ud; ud = tu; n = -n; }
            n.Normalize(); int s = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int i = 0; i < 4; i++) nm.Add(n); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.Add(s); t.Add(s + 1); t.Add(s + 3); t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
        }
        public Mesh Build(string name) { var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetNormals(nm); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateTangents(); m.RecalculateBounds(); return m; }
    }
    static GameObject Obj(string name, Mesh mesh, Material mat, bool collider = false) {
        var g = new GameObject(name); g.transform.SetParent(root); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) g.AddComponent<MeshCollider>().sharedMesh = mesh; return g;
    }
    static void PlazaFloor() {
        // An irregular disc: worn edge, uneven radius, so the plaza dissolves into the valley instead of ending on a perfect circle.
        const int seg = 96, rings = 20; var mb = new MB(); float baseR = 23.5f; var pos = new Vector3[rings + 1, seg + 1];
        for (int r = 0; r <= rings; r++) for (int s = 0; s <= seg; s++) {
            float a = (s % seg) * Mathf.PI * 2 / seg; float edge = baseR + (Mathf.PerlinNoise(Mathf.Cos(a) * 2 + 5, Mathf.Sin(a) * 2 + 5) - .5f) * 7f; float rad = Mathf.Lerp(0, edge, (float)r / rings);
            pos[r, s] = new Vector3(Mathf.Sin(a) * rad, Floor, Mathf.Cos(a) * rad);
        }
        for (int r = 0; r < rings; r++) for (int s = 0; s < seg; s++) {
            Vector3 a = pos[r, s], b = pos[r + 1, s], c = pos[r + 1, s + 1], d = pos[r, s + 1]; const float k = .22f;
            mb.Quad(a, b, c, d, Vector3.up, new Vector2(a.x, a.z) * k, new Vector2(b.x, b.z) * k, new Vector2(c.x, c.z) * k, new Vector2(d.x, d.z) * k);
        }
        Obj("Weathered plaza", mb.Build("Plaza"), Plaza);
        // Processional path: mossy cobbles from the player's start to the gate stairs.
        var path = new MB(); float y = Floor + .012f;
        for (int i = 0; i < 20; i++) {
            float z0 = -14 + i * 1.5f, z1 = z0 + 1.5f; float w0 = 2.7f + Mathf.PerlinNoise(i * .4f, 1) * .5f, w1 = 2.7f + Mathf.PerlinNoise((i + 1) * .4f, 1) * .5f; const float k = .25f;
            path.Quad(new Vector3(-w0, y, z0), new Vector3(w0, y, z0), new Vector3(w1, y, z1), new Vector3(-w1, y, z1), Vector3.up, new Vector2(-w0, z0) * k, new Vector2(w0, z0) * k, new Vector2(w1, z1) * k, new Vector2(-w1, z1) * k);
        }
        Obj("Processional path", path.Build("Path"), Cobble);
    }
    static void Runes() {
        if (!Rune) return; float y = Floor + .02f;
        // Inlaid energy rings: dashed arcs with gaps; the material colour is driven by the chosen side (see WorldLife).
        void RingDashes(float radius, float width, int dashes, float fill, float phase) {
            var mb = new MB(); int sub = 5;
            for (int d = 0; d < dashes; d++) {
                float a0 = (d + (1 - fill) * .5f) * Mathf.PI * 2 / dashes + phase, a1 = (d + 1 - (1 - fill) * .5f) * Mathf.PI * 2 / dashes + phase;
                for (int s = 0; s < sub; s++) {
                    float t0 = Mathf.Lerp(a0, a1, (float)s / sub), t1 = Mathf.Lerp(a0, a1, (s + 1f) / sub);
                    Vector3 i0 = new Vector3(Mathf.Sin(t0), 0, Mathf.Cos(t0)) * (radius - width / 2), o0 = new Vector3(Mathf.Sin(t0), 0, Mathf.Cos(t0)) * (radius + width / 2), i1 = new Vector3(Mathf.Sin(t1), 0, Mathf.Cos(t1)) * (radius - width / 2), o1 = new Vector3(Mathf.Sin(t1), 0, Mathf.Cos(t1)) * (radius + width / 2);
                    i0.y = o0.y = i1.y = o1.y = y; mb.Quad(i0, o0, o1, i1, Vector3.up, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                }
            }
            var g = Obj("Rune ring " + radius, mb.Build("Rune ring"), Rune); g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        RingDashes(3.6f,.035f,12,.7f,0); RingDashes(15.5f,.025f,36,.22f,0);
        var spokes = new MB();
        for (int i = 0; i < 0; i++) {
            float a = i * Mathf.PI * 2 / 12; Vector3 dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), side = new Vector3(dir.z, 0, -dir.x) * .04f;
            Vector3 p0 = dir * 3.9f, p1 = dir * 9.1f; p0.y = p1.y = y; spokes.Quad(p0 - side, p0 + side, p1 + side, p1 - side, Vector3.up, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }
        Obj("Rune spokes", spokes.Build("Spokes"), Rune).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------------------------------------------------------- architecture
    public static GameObject StoneBlock(string name, Vector3 position, Vector3 size, Material material, float angle = 0) {
        var g = new GameObject(name); g.transform.SetParent(root); g.transform.position = position; g.transform.rotation = Quaternion.Euler(0, angle, 0);
        Mesh mesh = RoundedBox(size, .10f); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = material;
        g.AddComponent<BoxCollider>().size = size; return g;
    }
    static void Gate() {
        // Steps up to the gate platform.
        for (int i = 0; i < 6; i++) StoneBlock("Gate step", new Vector3(0, Floor + .11f + i * .22f, 12.6f + i * .85f), new Vector3(15.5f - i * .5f, .24f, 1.1f), i % 2 == 0 ? Stone : Moss);
        StoneBlock("Gate platform", new Vector3(0, .98f, 19.2f), new Vector3(17, .32f, 8), Stone);
        for (int side = -1; side <= 1; side += 2) {
            StoneBlock("Gate pedestal", new Vector3(side * 6.6f, 1.45f, 17.5f), new Vector3(4.6f, .9f, 4.6f), Stone);
            for (int j = 0; j < 6; j++) StoneBlock("Weathered gate pillar", new Vector3(side * 6.6f + (j % 2 == 0 ? .0f : .1f) * side, 2.4f + j * 1.75f, 17.5f), new Vector3(3.3f - j * .14f, 1.7f, 3.3f - j * .14f), j % 3 == 1 ? Moss : Stone, j * 1.8f * side);
            StoneBlock("Broken arch crown", new Vector3(side * 3.6f, 13.2f, 17.5f), new Vector3(4.4f, 1.5f, 2.9f), Moss, side * -6);
            // Carved script lines that glow.
            if (Rune) for (int i = 0; i < 9; i++) Shape("Temple script", new Vector3(side * 5.0f, 3.2f + i * .8f, 15.82f), new Vector3(.26f + (i % 3) * .14f, .05f, .03f), Rune);
        }
        StoneBlock("Ancient lintel", new Vector3(0, 13.4f, 17.5f), new Vector3(5.2f, 1.7f, 3.1f), Stone, 1.5f);
        StoneBlock("Lintel cap", new Vector3(0, 14.7f, 17.5f), new Vector3(13.8f, 1.1f, 3.4f), Moss, -1);
        // The rift: a soft vertical glow and drifting motes between the pillars.
        var rift = new GameObject("Rift glow"); rift.transform.SetParent(root); rift.transform.position = new Vector3(0, 7.5f, 19f); var l = rift.AddComponent<Light>(); l.type = LightType.Point; l.range = 30; l.intensity = 1.7f; l.color = new Color(.5f, .85f, 1f); l.shadows = LightShadows.None; rift.AddComponent<WorldLifeTint>().kind = 0;
        Glow(new Vector3(0, 6.8f, 19.4f), new Color(.28f,.6f,.75f,.18f), 10f, true);
        Glow(new Vector3(0, 3.5f, 19.4f), new Color(.35f,.7f,.9f,.18f), 5f, true);
        Motes(new Vector3(0, 1.3f, 19.2f), new Vector3(7.5f, .3f, 2f), new Color(.7f, .95f, 1f, .9f), 22, 7, .07f, 1.2f);
    }
    static GameObject Shape(string name, Vector3 position, Vector3 scale, Material mat) {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(root); g.transform.position = position; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(g.GetComponent<Collider>()); g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; return g;
    }
    static void Colonnade() {
        const int count = 22; float radius = 22.5f; var positions = new Vector3[count]; var intact = new bool[count];
        for (int i = 0; i < count; i++) {
            float a = i * Mathf.PI * 2 / count; float deg = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0)); positions[i] = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
            if (deg < 24) continue; if (R() < .1f) continue; intact[i] = R() < .55f;
            float h = intact[i] ? R(7.5f, 9f) : R(2.5f, 6f); Column(positions[i], h, i);
            if (!intact[i]) {
                var fallen = GameObject.CreatePrimitive(PrimitiveType.Cylinder); fallen.name = "Fallen column drum"; fallen.transform.SetParent(root);
                Vector3 tan = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a)); fallen.transform.position = positions[i] + tan * R(2.5f, 3.6f) - positions[i].normalized * R(.5f, 1.5f) + Vector3.up * .75f;
                fallen.transform.rotation = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, R(0, 360), 0); fallen.transform.localScale = new Vector3(1.5f, R(1.2f, 2.2f), 1.5f);
                fallen.GetComponent<Renderer>().sharedMaterial = Moss;
            }
        }
        for (int i = 0; i < count; i++) {
            int j = (i + 1) % count; if (!intact[i] || !intact[j]) continue; Vector3 mid = (positions[i] + positions[j]) / 2; Vector3 chord = positions[j] - positions[i];
            var beam = StoneBlock("Architrave", new Vector3(mid.x, 9.6f, mid.z), new Vector3(chord.magnitude + .6f, 1.1f, 1.7f), Stone, Mathf.Atan2(-chord.z, chord.x) * Mathf.Rad2Deg);
        }
    }
    static void Column(Vector3 p, float h, int seed) {
        float groundY = Floor; var root0 = new GameObject("Colonnade column"); root0.transform.SetParent(root); root0.transform.position = p;
        var baseB = StoneBlock("Column base", p + Vector3.up * (groundY + .3f), new Vector3(2.2f, .6f, 2.2f), Stone, seed * 9); baseB.transform.SetParent(root0.transform, true);
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder); shaft.name = "Column shaft"; shaft.transform.SetParent(root0.transform, false);
        shaft.transform.localPosition = Vector3.up * (groundY + .6f + h / 2); shaft.transform.localScale = new Vector3(1.55f, h / 2, 1.55f); shaft.GetComponent<Renderer>().sharedMaterial = seed % 3 == 0 ? Moss : Stone;
        Object.Destroy(shaft.GetComponent<Collider>()); var cap = shaft.AddComponent<CapsuleCollider>(); cap.radius = .5f; cap.height = 2;
        if (h > 7f) { var top = StoneBlock("Column capital", p + Vector3.up * (groundY + .6f + h + .25f), new Vector3(2.4f, .6f, 2.4f), Stone, seed * 13); top.transform.SetParent(root0.transform, true); }
        else { var jag = StoneBlock("Broken column edge", p + Vector3.up * (groundY + .6f + h + .15f) + new Vector3(.2f, 0, .1f), new Vector3(1.2f, .5f, .9f), Moss, seed * 40); jag.transform.rotation *= Quaternion.Euler(R(-15, 15), 0, R(-18, 18)); jag.transform.SetParent(root0.transform, true); }
    }
    static void Tiers() {
        // Amphitheatre steps wrapping the back and flanks of the plaza; a gap is left where the gate stands.
        for (int t = 0; t < 4; t++) {
            float rIn = 25.4f + t * 3.0f, rOut = rIn + 3.6f, height = 1.0f + t * 1.15f; int parts = 14;
            for (int s = 0; s < parts; s++) {
                float a0 = 28 + s * (304f / parts), a1 = a0 + 304f / parts - 1.2f; if (R() < .12f) continue;
                float hh = height + R(-.25f, .35f); var mb = new MB(); const float k = .2f; int sub = 4; float bottom = -1.2f;
                for (int q = 0; q < sub; q++) {
                    float t0 = Mathf.Lerp(a0, a1, (float)q / sub) * Mathf.Deg2Rad, t1 = Mathf.Lerp(a0, a1, (q + 1f) / sub) * Mathf.Deg2Rad;
                    Vector3 d0 = new Vector3(Mathf.Sin(t0), 0, Mathf.Cos(t0)), d1 = new Vector3(Mathf.Sin(t1), 0, Mathf.Cos(t1)); float arc0 = t0 * rOut, arc1 = t1 * rOut;
                    Vector3 i0 = d0 * rIn + Vector3.up * hh, o0 = d0 * rOut + Vector3.up * hh, i1 = d1 * rIn + Vector3.up * hh, o1 = d1 * rOut + Vector3.up * hh;
                    mb.Quad(i0, o0, o1, i1, Vector3.up, new Vector2(i0.x, i0.z) * k, new Vector2(o0.x, o0.z) * k, new Vector2(o1.x, o1.z) * k, new Vector2(i1.x, i1.z) * k);
                    mb.Quad(i0 + Vector3.down * (hh - bottom), i0, i1, i1 + Vector3.down * (hh - bottom), -d0, new Vector2(arc0, bottom) * k, new Vector2(arc0, hh) * k, new Vector2(arc1, hh) * k, new Vector2(arc1, bottom) * k);
                }
                Vector3 e0 = new Vector3(Mathf.Sin(a0 * Mathf.Deg2Rad), 0, Mathf.Cos(a0 * Mathf.Deg2Rad)), e1 = new Vector3(Mathf.Sin(a1 * Mathf.Deg2Rad), 0, Mathf.Cos(a1 * Mathf.Deg2Rad));
                var wall = new MB(); // outer wall facing the plaza and end caps
                float aa0 = a0 * Mathf.Deg2Rad, aa1 = a1 * Mathf.Deg2Rad;
                for (int q = 0; q < sub; q++) {
                    float t0 = Mathf.Lerp(aa0, aa1, (float)q / sub), t1 = Mathf.Lerp(aa0, aa1, (q + 1f) / sub); Vector3 d0 = new Vector3(Mathf.Sin(t0), 0, Mathf.Cos(t0)), d1 = new Vector3(Mathf.Sin(t1), 0, Mathf.Cos(t1));
                    wall.Quad(d0 * rIn + Vector3.up * bottom, d0 * rIn + Vector3.up * hh, d1 * rIn + Vector3.up * hh, d1 * rIn + Vector3.up * bottom, -d0, new Vector2(t0 * rIn, bottom) * k, new Vector2(t0 * rIn, hh) * k, new Vector2(t1 * rIn, hh) * k, new Vector2(t1 * rIn, bottom) * k);
                }
                var g = Obj("Amphitheatre step", mb.Build("Step top"), (t + s) % 3 == 0 ? Moss : Stone, true); var g2 = Obj("Amphitheatre riser", wall.Build("Step riser"), (t + s) % 3 == 0 ? Moss : Stone, true); g2.transform.SetParent(g.transform, true);
            }
        }
    }
    static void Statues() {
        float[] angles = { 62, -62, 128, -128 };
        for (int i = 0; i < angles.Length; i++) {
            float a = angles[i] * Mathf.Deg2Rad; Vector3 p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 19.5f;
            StoneBlock("Statue pedestal", p + Vector3.up * (Floor + .8f), new Vector3(3.4f, 1.6f, 3.4f), Stone, angles[i]);
            StoneBlock("Statue plinth", p + Vector3.up * (Floor + 1.75f), new Vector3(2.7f, .3f, 2.7f), Moss, angles[i]);
            var s = Prop("gothic_statue", p + Vector3.up * (1.9f - Height(p.x, p.z)), Vector3.one * 5.2f, 0); if (s != null) { s.transform.rotation = Quaternion.LookRotation(-new Vector3(p.x, 0, p.z).normalized) * Quaternion.Euler(0, 180, 0); s.name = "Guardian statue"; }
            if (Rune) Shape("Statue light", p + Vector3.up * (Floor + .1f) - new Vector3(p.x, 0, p.z).normalized * 1.8f, new Vector3(2.6f, .03f, .08f), Rune).transform.rotation = Quaternion.Euler(0, angles[i], 0);
        }
        StoneBlock("Archive bust pedestal",new Vector3(7.5f,.5f,12.8f),new Vector3(1.2f,1,1.2f),Stone);
        Prop("marble_bust_01",new Vector3(7.5f,1,12.8f),Vector3.one*.9f,210);
    }
    static void Braziers() {
        Vector3[] spots = { new Vector3(-5.6f, 0, -2f), new Vector3(5.6f, 0, -2f), new Vector3(-5.6f, 0, 6f), new Vector3(5.6f, 0, 6f), new Vector3(-9.5f, 0, 14.2f), new Vector3(9.5f, 0, 14.2f), new Vector3(-4.4f, 0, 12.1f), new Vector3(4.4f, 0, 12.1f) };
        for (int i = 0; i < spots.Length; i++) {
            Vector3 p = spots[i]; float baseY = i >= 4 ? .1f : Floor;
            StoneBlock("Brazier column", p + Vector3.up * (baseY + .85f), new Vector3(.8f, 1.7f, .8f), Stone, i * 20);
            var bowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bowl.name = "Brazier bowl"; bowl.transform.SetParent(root); bowl.transform.position = p + Vector3.up * (baseY + 1.85f); bowl.transform.localScale = new Vector3(1.25f, .16f, 1.25f); bowl.GetComponent<Renderer>().sharedMaterial = ArenaArt.Mat(new Color(.15f,.18f,.20f)); Object.Destroy(bowl.GetComponent<Collider>());
            Vector3 fire = p + Vector3.up * (baseY + 2.05f);
            var light = new GameObject("Brazier light").AddComponent<Light>(); light.transform.SetParent(root); light.transform.position = fire + Vector3.up * .5f; light.type = LightType.Point; light.range = 12; light.intensity = 1.25f; light.color = new Color(1, .58f, .26f); light.shadows = LightShadows.None; light.gameObject.AddComponent<LightFlicker>();
            Flame(fire); Glow(fire + Vector3.up * .45f, new Color(1, .6f, .25f, .55f), 3.2f, false);
        }
    }
    static void Lanterns() {
        Vector3[] spots = { new Vector3(-7.4f, 0, -9f), new Vector3(7.4f, 0, -9f), new Vector3(-7.8f, 0, 1.8f), new Vector3(7.8f, 0, 1.8f), new Vector3(-8.8f, 0, 9f), new Vector3(8.8f, 0, 9f) };
        foreach (var p in spots) {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pole.name = "Lantern post"; pole.transform.SetParent(root); pole.transform.position = p + Vector3.up * 1.5f; pole.transform.localScale = new Vector3(.12f, 1.5f, .12f);
            pole.GetComponent<Renderer>().sharedMaterial = ArenaArt.Mat(new Color(.1f, .08f, .07f)); Object.Destroy(pole.GetComponent<Collider>());
            var lantern = Prop("wooden_lantern_01", p + Vector3.up * 3.2f, Vector3.one * .62f, R(0, 360)); if (lantern != null) lantern.transform.position = p + Vector3.up * 2.75f;
            var light = new GameObject("Lantern light").AddComponent<Light>(); light.transform.SetParent(root); light.transform.position = p + Vector3.up * 3.05f; light.type = LightType.Point; light.range = 10; light.intensity = .65f; light.color = new Color(1, .7f, .4f); light.shadows = LightShadows.None; light.gameObject.AddComponent<LightFlicker>();
            Glow(p + Vector3.up * 3.05f, new Color(1,.68f,.39f,.30f), 1.1f, false);
        }
    }
    static void Channels() {
        var water = Load("Water"); if (!water) return;
        foreach (int side in new[] { -1, 1 }) {
            float x0 = side * 3.3f, x1 = side * 4.9f; if (x0 > x1) { var t = x0; x0 = x1; x1 = t; }
            var mb = new MB(); float y = Floor + .035f; float z0 = -3f, z1 = 12f;
            mb.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
            var mesh = mb.Build("Reflecting channel"); var tangents = new Vector4[4]; for (int i = 0; i < 4; i++) tangents[i] = new Vector4(1, 0, 0, -1); mesh.tangents = tangents;
            var g = Obj("Reflecting channel", mesh, water); g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            foreach (float cx in new[] { x0 - .22f, x1 + .22f }) StoneBlock("Channel curb", new Vector3(cx, Floor + .05f, (z0 + z1) / 2), new Vector3(.4f, .18f, z1 - z0 + .4f), Stone).GetComponent<BoxCollider>().enabled = false;
        }
    }
    static void Rubble() {
        for (int i = 0; i < 46; i++) {
            float a = R(0, Mathf.PI * 2), r = R(14, 23); Vector3 p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (Mathf.Abs(p.x) < 6 && p.z > 3) continue;
            float s = R(.5f, 1.7f); var b = StoneBlock("Fallen masonry", p + Vector3.up * (Floor + s * .22f), new Vector3(s * R(1, 2), s * .5f, s * R(.8f, 1.5f)), R() < .5f ? Moss : Stone, R(0, 360)); b.transform.rotation *= Quaternion.Euler(R(-12, 12), 0, R(-14, 14));
        }
    }

    // ---------------------------------------------------------------- flora & scenery
    static void Flora() {
        for (int i = 0; i < 40; i++) {
            float a = R(0, Mathf.PI * 2), r = R(24, 64); Vector3 p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; float size = R(4, 12);
            if (i < 14) { p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * R(60, 70); size = R(24, 44); }
            string id = i < 14 ? "coastal_cliff_01" : i % 4 == 0 ? "rock_face_01" : i % 3 == 0 ? "rock_moss_set_02" : i % 2 == 0 ? "rock_moss_set_01" : "boulder_01";
            Prop(id, p, new Vector3(size, size * R(.65f, 1.15f), size), rnd.Next(360));
        }
        Prop("coastal_cliff_01", new Vector3(-30, -3, 40), new Vector3(58, 75, 54), 80); Prop("coastal_cliff_01", new Vector3(35, -5, 46), new Vector3(65, 85, 56), -80); Prop("coastal_cliff_01", new Vector3(-8, -4, 74), new Vector3(75, 100, 60), 140);
        for (int i = 0; i < 70; i++) { // trees on the slopes and beyond the tiers
            float a = R(0, Mathf.PI * 2), r = R(34, 78); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0)) < 22 && r < 70) continue;
            Prop("fir_sapling", p, Vector3.one * R(7, 12), rnd.Next(360));
        }
        for (int i = 0; i < 70; i++) { float a = R(0, Mathf.PI * 2), r = R(14, 47); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (r < 24 && PlazaClear(p)) continue; Prop(i % 2 == 0 ? "shrub_01" : "shrub_03", p, Vector3.one * R(.9f, 2.0f), rnd.Next(360)); }
        for (int i = 0; i < 90; i++) { float a = R(0, Mathf.PI * 2), r = R(12, 44); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (r < 24 && PlazaClear(p)) continue; Prop("celandine_01", p, Vector3.one * R(.35f, .7f), rnd.Next(360)); }
        for (int i = 0; i < 130; i++) { float a = R(0, Mathf.PI * 2), r = R(14, 44); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (r < 24 && PlazaClear(p)) continue; Prop("fern_02", p, Vector3.one * R(.6f, 1.5f), rnd.Next(360)); }
        for (int i = 0; i < 160; i++) { float a = R(0, Mathf.PI * 2), r = R(10, 46); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; if (r < 24 && PlazaClear(p)) continue; Prop("grass_medium_01", p, Vector3.one * R(.45f, 1.1f), rnd.Next(360)); }
        for (int i = 0; i < 10; i++) { float a = R(0, Mathf.PI * 2), r = R(26, 44); var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; Prop(i % 2 == 0 ? "dead_tree_trunk_02" : "tree_stump_01", p, Vector3.one * R(2.2f, 4.5f), rnd.Next(360)); }
    }
    // Vegetation stays at the plaza fringe, never on the walking floor or the processional path.
    static bool PlazaClear(Vector3 p) => p.magnitude < 20.5f || (Mathf.Abs(p.x) < 6 && p.z > -15 && p.z < 22);

    public static GameObject Prop(string id, Vector3 p, Vector3 scale, float yaw) {
        if(!variants.TryGetValue(id,out var set)){set=Resources.LoadAll<GameObject>("Generated/Variants/"+id);variants[id]=set;}
        var prefab=set.Length>0?set[rnd.Next(set.Length)]:Resources.Load<GameObject>("Generated/"+id);if(!prefab)return null;
        var g = Object.Instantiate(prefab, root); g.name = id; g.transform.position = p + Vector3.up * Height(p.x, p.z); g.transform.localScale = scale; g.transform.rotation = Quaternion.Euler(0, yaw, 0);if(id=="fir_sapling"&&p.magnitude>40)foreach(var r in g.GetComponentsInChildren<Renderer>())r.shadowCastingMode=ShadowCastingMode.Off;return g;
    }

    // ---------------------------------------------------------------- particles / glow
    public static void Glow(Vector3 pos, Color color, float size, bool pulse) {
        if (!GlowAdd) return; var g = new GameObject("Glow"); g.transform.SetParent(root); g.transform.position = pos; var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
        var main = ps.main; main.loop = true; main.startLifetime = 1000; main.startSpeed = 0; main.startSize = size; main.startColor = color; main.maxParticles = 1; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = true;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 1) }); var sh = ps.shape; sh.enabled = false;
        var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = GlowAdd; r.shadowCastingMode = ShadowCastingMode.Off; r.sortingFudge = 5; ps.Play();
        if (pulse) g.AddComponent<WorldLifeTint>().kind = 1;
    }
    static void Flame(Vector3 pos) {
        if (!GlowAdd) return; var g = new GameObject("Flame"); g.transform.SetParent(root); g.transform.position = pos; var ps = g.AddComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f); main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.2f); main.startSize = new ParticleSystem.MinMaxCurve(.35f, .7f); main.startColor = new Color(1, .55f, .2f, .9f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 60; main.gravityModifier = -.1f;
        var em = ps.emission; em.rateOverTime = 34; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .28f; sh.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true; var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(new Color(1, .85f, .4f), 0), new GradientColorKey(new Color(1, .35f, .1f), .6f), new GradientColorKey(new Color(.3f, .08f, .05f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.9f, .15f), new GradientAlphaKey(0, 1) }); col.color = grad;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(.3f, 1), new Keyframe(1, 0)));
        var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = GlowAdd; r.shadowCastingMode = ShadowCastingMode.Off;
        var embers = new GameObject("Embers"); embers.transform.SetParent(g.transform, false); var es = embers.AddComponent<ParticleSystem>(); var em2 = es.main; em2.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); em2.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.6f); em2.startSize = .06f; em2.startColor = new Color(1, .7f, .3f, 1); em2.simulationSpace = ParticleSystemSimulationSpace.World; em2.maxParticles = 30;
        var ee = es.emission; ee.rateOverTime = 5; var esh = es.shape; esh.shapeType = ParticleSystemShapeType.Circle; esh.radius = .3f; esh.rotation = new Vector3(-90, 0, 0); var nz = es.noise; nz.enabled = true; nz.strength = .6f; nz.frequency = .5f;
        embers.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowAdd;
    }
    static void Motes(Vector3 pos, Vector3 box, Color color, int rate, float life, float size, float speed) {
        if (!GlowAdd) return; var g = new GameObject("Drifting motes"); g.transform.SetParent(root); g.transform.position = pos; var ps = g.AddComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = life; main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, speed); main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size); main.startColor = color; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400; main.gravityModifier = -.02f;
        var em = ps.emission; em.rateOverTime = rate; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box; var nz = ps.noise; nz.enabled = true; nz.strength = .8f; nz.frequency = .35f;
        var col = ps.colorOverLifetime; col.enabled = true; var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = grad;
        var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = GlowAdd; r.shadowCastingMode = ShadowCastingMode.Off;
    }
    static void Atmosphere() {
        Motes(new Vector3(0, 2.2f, 2), new Vector3(46, 4, 46), new Color(1, .86f, .55f, .75f), 14, 9, .12f, .5f);   // fireflies / pollen
        Motes(new Vector3(0, 7, 4), new Vector3(52, 8, 52), new Color(1, .8f, .5f, .35f), 8, 14, .05f, .3f);
        var leaves = new GameObject("Falling leaves"); leaves.transform.SetParent(root); leaves.transform.position = new Vector3(0, 14, 4); var ps = leaves.AddComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = 12; main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.1f, .2f); main.startColor = new Color(.9f, .55f, .25f, .9f); main.gravityModifier = .06f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 120;
        var em = ps.emission; em.rateOverTime = 6; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60, 1, 60); var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = .6f; vel.z = .3f; var nz = ps.noise; nz.enabled = true; nz.strength = 1.2f; nz.frequency = .4f;
        var r = leaves.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Load("Dust"); r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------------------------------------------------------- stone block mesh
    static Mesh RoundedBox(Vector3 size, float radius) {
        radius = Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .4f);
        var v = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3[] ns = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int face = 0; face < 6; face++) {
            Vector3 normal = ns[face], u = face < 2 ? Vector3.forward : Vector3.right, w = Vector3.Cross(normal, u); int start = v.Count; const int n = 5;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
                Vector3 q = Vector3.Scale(normal + u * (x / (n - 1f) * 2 - 1) + w * (y / (n - 1f) * 2 - 1), size * .5f);
                Vector3 inner = new Vector3(Mathf.Clamp(q.x, -size.x * .5f + radius, size.x * .5f - radius), Mathf.Clamp(q.y, -size.y * .5f + radius, size.y * .5f - radius), Mathf.Clamp(q.z, -size.z * .5f + radius, size.z * .5f - radius));
                Vector3 d = (q - inner).normalized; v.Add(inner + d * radius); normals.Add(d); uv.Add(new Vector2(Vector3.Dot(q, u) / 2.2f, Vector3.Dot(q, w) / 2.2f));
                if (x < n - 1 && y < n - 1) { int i = start + y * n + x; t.Add(i); t.Add(i + 1); t.Add(i + n); t.Add(i + 1); t.Add(i + n + 1); t.Add(i + n); }
            }
        }
        var mesh = new Mesh { name = "Eroded stone block" }; mesh.SetVertices(v); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
    }
}
}
