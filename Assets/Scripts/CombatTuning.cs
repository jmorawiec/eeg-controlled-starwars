namespace ForceDemo {
// Distances are in metres for the 1.86 m character; one place to tune game feel.
public static class CombatTuning {
    public const float WalkSpeed=1.8f,JogSpeed=3.4f,SprintSpeed=5.2f;
    public const float Acceleration=11,Deceleration=17,TurnSpeed=520;
    public const float RollDuration=.78f,RollDistance=2.8f,RollCooldown=.9f;
    public const float MeleeRange=2.15f,BossMeleeRange=2.35f;
    public const float ForceRange=9,LockRange=16;
    public static float ImpactTime(int combo)=>combo==3?.45f:combo==2?.31f:.29f;
    public static float AttackDuration(int combo)=>combo==3?1.02f:combo==2?.80f:.76f;
}
}
