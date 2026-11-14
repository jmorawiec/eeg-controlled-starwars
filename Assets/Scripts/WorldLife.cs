using UnityEngine;
namespace ForceDemo {
public class LightFlicker : MonoBehaviour {
    Light l; float baseIntensity, seed;
    void Awake() { l = GetComponent<Light>(); baseIntensity = l.intensity; seed = Random.value * 100; }
    void Update() { if (l) l.intensity = baseIntensity * (.82f + Mathf.PerlinNoise(seed, Time.time * 7f) * .36f); }
}
// Re-tints world glow (rift light, rune rings, glow sprites) to the chosen side of the Force.
public class WorldLifeTint : MonoBehaviour {
    public int kind; // 0 = light, 1 = particle glow
    Light l; ParticleSystem ps; Color baseColor; float baseAlpha, baseIntensity;
    void Start() { l = GetComponent<Light>(); if (l) baseIntensity = l.intensity; ps = GetComponent<ParticleSystem>(); if (ps) { baseColor = ps.main.startColor.color; baseAlpha = baseColor.a; } }
    void Update() {
        var game = WorldLife.Game; Color a = game ? game.Accent : ArenaArt.Cyan; float pulse = .88f + Mathf.Sin(Time.time * 1.7f) * .12f;
        if (l) { l.color = Color.Lerp(l.color, new Color(a.r * .55f + .45f, a.g * .55f + .45f, a.b * .55f + .45f), Time.deltaTime * 2); l.intensity = baseIntensity * pulse; }
        if (ps && ps.isPlaying) { var m = ps.main; var c = Color.Lerp(m.startColor.color, new Color(a.r, a.g, a.b, baseAlpha * pulse), Time.deltaTime * 2); m.startColor = c; var parts = new ParticleSystem.Particle[1]; int n = ps.GetParticles(parts); for (int i = 0; i < n; i++) parts[i].startColor = c; ps.SetParticles(parts, n); }
    }
}
public class WorldLife : MonoBehaviour {
    public static ForceGame Game; public ForceGame game; Material rune;
    void Start() { Game = game; rune = WorldBuilder.Rune; }
    void Update() {
        if (!rune || !game) return; Color a = game.Accent; float pulse = 1.25f + Mathf.Sin(Time.time * 1.6f) * .3f; if (game.stage == DemoStage.Boss) pulse += Mathf.Sin(Time.time * 5f) * .25f;
        rune.color = Color.Lerp(rune.color, new Color(a.r * pulse, a.g * pulse, a.b * pulse, 1), Time.deltaTime * 3);
    }
}
}
