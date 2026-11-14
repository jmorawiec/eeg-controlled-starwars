using UnityEngine;
using UnityEngine.Rendering;
namespace ForceDemo {
// Short-lived impact effects built from a single additive particle material.
public static class CombatFX {
    static Material glow;
    static Material Glow { get { if (!glow) glow = Resources.Load<Material>("Generated/GlowAdd"); return glow; } }

    static ParticleSystem Make(Transform parent, Vector3 pos, string name) {
        var g = new GameObject(name); if (parent) g.transform.SetParent(parent); g.transform.position = pos; var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.Destroy; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Glow; r.shadowCastingMode = ShadowCastingMode.Off; return ps;
    }
    // Streaking sparks that spray outward (optionally biased along `direction`).
    public static void Sparks(Transform parent, Vector3 pos, Color color, int count, float speed = 6.5f, Vector3? direction = null) {
        if (!Glow || count <= 0) return; var ps = Make(parent, pos, "Sparks"); var main = ps.main;
        main.duration = .1f; main.startLifetime = new ParticleSystem.MinMaxCurve(.16f, .42f); main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .35f, speed); main.startSize = new ParticleSystem.MinMaxCurve(.05f, .13f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, .55f), color); main.gravityModifier = .9f; main.maxParticles = 160;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)Mathf.Min(count, 120)) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = direction.HasValue ? 38 : 90; sh.radius = .04f;
        if (direction.HasValue && direction.Value.sqrMagnitude > .001f) ps.transform.rotation = Quaternion.LookRotation(direction.Value.normalized);
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0, 1) }); col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 2.4f; r.velocityScale = .1f;
        ps.Play();
    }
    // A brief hot flash at the point of impact.
    public static void Flash(Transform parent, Vector3 pos, Color color, float size) {
        if (!Glow) return; var ps = Make(parent, pos, "Impact flash"); var main = ps.main; main.duration = .1f; main.startLifetime = .16f; main.startSpeed = 0; main.startSize = size; main.startColor = Color.Lerp(color, Color.white, .5f); main.maxParticles = 2;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 1) }); var sh = ps.shape; sh.enabled = false;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .45f), new Keyframe(.25f, 1), new Keyframe(1, .1f)));
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = g;
        ps.Play();
    }
    // Rising dust / debris puff for roll starts and heavy landings.
    public static void Dust(Transform parent, Vector3 pos, Color color, int count) {
        if (!Glow) return; var ps = Make(parent, pos, "Dust puff"); var main = ps.main; main.duration = .1f; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f); main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(.35f, .8f);
        main.startColor = new Color(color.r, color.g, color.b, .28f); main.gravityModifier = -.03f; main.maxParticles = 40;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) }); var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .5f; sh.rotation = new Vector3(-90, 0, 0);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(1, 1.4f)));
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) }); col.color = g;
        ps.Play();
    }
}
}
