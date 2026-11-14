using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForceDemo {
public enum DemoStage { Choice, Opening, Tutorial, Waves, BossIntro, Boss, Victory, Defeat }
public enum ForceSide { Light, Dark }
[Serializable] public class Enemy {
    public Transform body; public float hp, maxHP, attack, stun, lift, hit, action, telegraph;
    public Vector3 velocity, attackPoint; public bool boss, ranged, training;
    public int pattern; public float stagger;public bool hitResolved;
    public Vector3 Position => body.position;
}
public class Bolt {
    public Transform body; public Vector3 velocity; public float life=5; public float damage;
}
public class ForceGame : MonoBehaviour {
    public DemoStage stage = DemoStage.Choice; public ForceSide side;
    public readonly ForceInput controls = new ForceInput();
    public readonly List<Enemy> enemies = new List<Enemy>();
    public readonly List<Bolt> bolts = new List<Bolt>();
    public Camera view; public Transform player, mentor, world, actors, effects;
    public float health=100, energy=100, primaryCooldown, secondaryCooldown, dodgeCooldown, dodgeTime, hurtFlash;
    public float stageTime, playTime, primaryPractice, secondaryPractice, healGlow, shake;
    public int wave, kills, tutorialStep; public bool paused, muted;
    public Vector3 facing=Vector3.forward; Vector3 dodgeDirection;
    public Enemy target; public string notice=""; public float noticeTime;
    public Color Accent => side==ForceSide.Light ? ArenaArt.Cyan : ArenaArt.Red;
    public bool Playing => stage==DemoStage.Tutorial || stage==DemoStage.Waves || stage==DemoStage.Boss;
    public float ArenaRadius=24;
    public float stamina=100,attackTimer,guardBroken,staminaDelay,blockTime;
    public int combo;public Enemy lockedTarget;public ShoulderCamera orbit;
    public CharacterController motor;public CharacterVisual visuals;public OpeningSequence opening;
    public bool Blocking => controls.Block && guardBroken<=0 && stamina>0 && dodgeTime<=0 && attackTimer<=0;
    public bool Invulnerable => dodgeTime>0 && CombatTuning.RollDuration-dodgeTime>=.13f && CombatTuning.RollDuration-dodgeTime<=.45f;
    public bool AttackQueued;bool swingResolved;float inputGrace,verticalSpeed,comboIdle;Vector3 planarVelocity;
    public float MasterVolume= .65f;
    public int basicPractice,blockPractice,rollPractice;
    public float hitMarker,lunge,castPose;float hitStopTime,assistTime;Vector3 assistDir;
    public void HitStop(float seconds){hitStopTime=Mathf.Max(hitStopTime,seconds);}
    public string WeaponName => "Glowing Stick";
    float waveDelay, lightningSound, channelVfx;
    LineRenderer aimLine; LineRenderer channelLine; AudioSource audioSource;
    AudioClip pushSound, lightningClip, healSound, hitSound, chokeSound;
    public bool SkipInput;

    void Awake() {
        Application.targetFrameRate=60;QualitySettings.vSyncCount=0;Time.maximumDeltaTime=.05f;
        if(!Application.isEditor&&(Screen.width>1920||Screen.height>1080))Screen.SetResolution(1600,900,FullScreenMode.Windowed);
        world=new GameObject("Temple environment").transform; actors=new GameObject("Characters").transform; effects=new GameObject("Transient effects").transform;
        ArenaArt.Build(world);
        world.gameObject.AddComponent<WorldLife>().game=this;
        var cam=new GameObject("Arena Camera"); view=cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>();
        view.fieldOfView=62;view.nearClipPlane=.12f;view.farClipPlane=220;view.allowHDR=true;view.allowMSAA=true;view.clearFlags=CameraClearFlags.Skybox;
        var fillLight=new GameObject("Character bounce light").AddComponent<Light>();fillLight.transform.SetParent(cam.transform,false);fillLight.type=LightType.Spot;fillLight.range=14;fillLight.spotAngle=105;fillLight.intensity=.65f;fillLight.color=new Color(.68f,.81f,1);fillLight.cullingMask=1<<8;fillLight.shadows=LightShadows.None;
        BuildPlayer(true);
        orbit=cam.AddComponent<ShoulderCamera>();orbit.game=this;orbit.atmosphere=cam.AddComponent<ForceAtmosphere>();orbit.sensitivity=PlayerPrefs.GetFloat("CameraSensitivity",1);orbit.ResetView();
        MasterVolume=PlayerPrefs.GetFloat("MasterVolume",.65f);orbit.atmosphere.bloom=PlayerPrefs.GetInt("Bloom",1)==1;orbit.atmosphere.sunDirection=WorldBuilder.SunDirection;orbit.atmosphere.sunColor=WorldBuilder.SunColor;
        mentor=ArenaArt.Character("Mentor Auren / Veyr",new Vector3(0,.22f,11),ArenaArt.Cyan,actors);mentor.rotation=Quaternion.Euler(0,180,0);
        aimLine=ArenaArt.Line("Aim reticle",new[]{Vector3.zero,Vector3.one},ArenaArt.Cyan,.03f,effects);
        channelLine=ArenaArt.Line("Channelled force",new[]{Vector3.zero,Vector3.one},ArenaArt.Violet,.025f,effects); channelLine.enabled=false;
        audioSource=gameObject.AddComponent<AudioSource>(); audioSource.spatialBlend=0; audioSource.volume=MasterVolume*.5f;
        pushSound=Tone(100,.4f,2); lightningClip=Tone(65,.18f,1); healSound=Tone(480,.65f,0); hitSound=Tone(150,.12f,1); chokeSound=Tone(70,.4f,2);
        opening=gameObject.AddComponent<OpeningSequence>();opening.game=this;
        gameObject.AddComponent<ForceHUD>().game=this;
    }
    void BuildPlayer(bool light){
        if(player){player.gameObject.SetActive(false);Destroy(player.gameObject);}CharacterVisual.LightHeroSelected=light;
        player=ArenaArt.Character("Apprentice",new Vector3(0,0,-5),ArenaArt.Cyan,actors,false,true);
        visuals=player.GetComponent<CharacterVisual>();
        motor=player.gameObject.AddComponent<CharacterController>();motor.height=1.85f;motor.radius=.3f;motor.center=Vector3.up*.93f;motor.stepOffset=.2f;motor.slopeLimit=48;motor.skinWidth=.03f;motor.minMoveDistance=0;
    }
    AudioClip Tone(float frequency,float duration,int mode) {
        int n=(int)(22050*duration); float[] data=new float[n]; var random=new System.Random(14);
        for(int i=0;i<n;i++) {
            float t=(float)i/22050, env=Mathf.Sin(Mathf.PI*i/n)*Mathf.Exp(-t*3);
            float v=Mathf.Sin(t*frequency*(mode==2?Mathf.Exp(-t*5):1)*Mathf.PI*2);
            if(mode==1) v=v*.25f+((float)random.NextDouble()*2-1)*.5f;
            if(mode==0) v=v*.45f+Mathf.Sin(t*frequency*1.5f*Mathf.PI*2)*.25f;
            data[i]=v*env*.5f;
        }
        var clip=AudioClip.Create("Force tone",n,1,22050,false); clip.SetData(data,0); return clip;
    }
    void Sound(AudioClip clip) { if(!muted && audioSource) audioSource.PlayOneShot(clip); }
    void Update() {
        if(!SkipInput) controls.Read();
        if(stage==DemoStage.Choice&&player)player.rotation=Quaternion.Slerp(player.rotation,Quaternion.Euler(0,-125f,0),Mathf.Min(Time.deltaTime,.05f)*3);
        if(!Application.isFocused&&!SkipInput){UpdateCursor();return;}
        if(stage==DemoStage.Opening){opening.Tick(Mathf.Min(Time.unscaledDeltaTime,.05f));UpdateCursor();return;}
        if(controls.Pause && stage!=DemoStage.Choice) { paused=!paused; Time.timeScale=paused?0:1; }
        if(paused) { UpdateCursor();return; }
        if(hitStopTime>0){hitStopTime-=Time.unscaledDeltaTime;Time.timeScale=hitStopTime>0?.04f:1f;}else if(Time.timeScale<1f&&Time.timeScale>0)Time.timeScale=1f;
        UpdateCursor();
        if((stage==DemoStage.Defeat || stage==DemoStage.Victory) && (controls.Restart || controls.Confirm)) ReturnToMenu();
        float dt=Mathf.Min(Time.deltaTime,.05f); stageTime+=dt;
        if(Playing) playTime+=dt;
        primaryCooldown=Mathf.Max(0,primaryCooldown-dt); secondaryCooldown=Mathf.Max(0,secondaryCooldown-dt);
        dodgeCooldown=Mathf.Max(0,dodgeCooldown-dt); dodgeTime=Mathf.Max(0,dodgeTime-dt); hurtFlash=Mathf.Max(0,hurtFlash-dt*2);
        healGlow=Mathf.Max(0,healGlow-dt); hitMarker=Mathf.Max(0,hitMarker-dt); noticeTime-=dt; lightningSound-=dt; channelVfx-=dt;
        channelLine.enabled=false;
        inputGrace=Mathf.Max(0,inputGrace-dt);
        guardBroken=Mathf.Max(0,guardBroken-dt);staminaDelay=Mathf.Max(0,staminaDelay-dt);
        if(Playing) {
            if(staminaDelay<=0 && dodgeTime<=0 && attackTimer<=0) stamina=Mathf.Min(100,stamina+dt*(Blocking?8:27));
            blockTime=controls.Block?blockTime+dt:0;
            if(controls.LockOn) ToggleLock();
            if(lockedTarget!=null && (!lockedTarget.body || lockedTarget.hp<=0 || Vector3.Distance(lockedTarget.Position,player.position)>30))lockedTarget=null;
            MovePlayer(dt);
            UpdateMelee(dt);
            target=FindTarget(17,.73f);
            aimLine.enabled=false; aimLine.sharedMaterial=ArenaArt.Mat(Accent*.65f,true);
            aimLine.SetPosition(0,player.position+Vector3.up*.06f+facing*.8f); aimLine.SetPosition(1,player.position+Vector3.up*.06f+facing*(target!=null?Mathf.Min(17,Vector3.Distance(target.Position,player.position)):4));
            bool channel=side==ForceSide.Dark && (controls.Primary || controls.Secondary) && energy>0;
            if(!channel) energy=Mathf.Min(100,energy+dt*(stage==DemoStage.Tutorial?24:17));
            if(inputGrace<=0 && dodgeTime<=0 && attackTimer<=0 && !Blocking && guardBroken<=0 && side==ForceSide.Light) {
                if(controls.PrimaryDown) Push(); if(controls.SecondaryDown) Heal();
            } else if(inputGrace<=0 && dodgeTime<=0 && attackTimer<=0 && !Blocking && guardBroken<=0 && side==ForceSide.Dark) {
                if(controls.Secondary) Choke(dt); else if(controls.Primary) Lightning(dt);
            }
            visuals.blocking=Blocking;visuals.attack=attackTimer>0?Mathf.Max(.001f,1-attackTimer/CombatTuning.AttackDuration(combo)):0;visuals.combo=combo;
            visuals.roll=dodgeTime>0?Mathf.Max(.001f,1-dodgeTime/CombatTuning.RollDuration):0;castPose=Mathf.Max(0,castPose-dt);visuals.channel=controls.Primary||controls.Secondary||castPose>0?1:0;
            UpdateEnemies(dt); UpdateBolts(dt); Progress(dt);
        } else { aimLine.enabled=false; }
        shake=Mathf.Max(0,shake-dt*2);
        audioSource.volume=MasterVolume*.5f;
    }
    void UpdateCursor(){bool active=Playing&&!paused&&!SkipInput&&Application.isFocused;Cursor.lockState=active?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!active&&stage!=DemoStage.Opening;}
    public void Teleport(Vector3 position){if(motor)motor.enabled=false;player.position=position;if(motor)motor.enabled=true;planarVelocity=Vector3.zero;verticalSpeed=0;if(visuals)visuals.ResetMotion();}
    public void Begin(ForceSide chosen, bool playOpening=true) {
        opening.Cancel();
        ClearCombat(); hitStopTime=lunge=castPose=hitMarker=0;side=chosen;BuildPlayer(chosen==ForceSide.Light); stage=DemoStage.Tutorial; stageTime=0; playTime=0; wave=0; kills=0;
        health=chosen==ForceSide.Light?58:100; energy=100; primaryCooldown=secondaryCooldown=dodgeCooldown=dodgeTime=0;
        primaryPractice=secondaryPractice=0; tutorialStep=0; paused=false; Time.timeScale=1;
        hurtFlash=healGlow=shake=lightningSound=channelVfx=0;
        Teleport(new Vector3(0,0,-5));player.rotation=Quaternion.identity;facing=Vector3.forward;stamina=100;attackTimer=guardBroken=staminaDelay=blockTime=0;combo=0;AttackQueued=false;lockedTarget=null;basicPractice=blockPractice=rollPractice=0;
        visuals.SetAccent(Accent);{var mv=mentor?mentor.GetComponent<CharacterVisual>():null;if(mv)mv.SetAccent(Accent);}visuals.roll=visuals.attack=0;visuals.blocking=false;orbit.ResetView();inputGrace=.25f;
        UpdateCursor();
        Spawn(new Vector3(-2.5f,0,2),true); Spawn(new Vector3(3,0,4),true);
        if(playOpening){stage=DemoStage.Opening;opening.Play();UpdateCursor();}
        else Notify(chosen==ForceSide.Light?"Master Auren: Balance gives the Force direction.":"Master Veyr: Feel its energy. Bend it to your will.",5);
    }
    public void FinishOpening(){
        opening.Cancel();stage=DemoStage.Tutorial;stageTime=0;inputGrace=.35f;noticeTime=0;orbit.ResetView();view.fieldOfView=62;UpdateCursor();
    }
    public void ReturnToMenu() {
        opening.Cancel();
        ClearCombat(); stage=DemoStage.Choice; stageTime=0; paused=false; Time.timeScale=1;
        Teleport(new Vector3(0,0,-5));health=energy=stamina=100;attackTimer=dodgeTime=0;lockedTarget=null;visuals.roll=visuals.attack=0;visuals.blocking=false;visuals.speed=0;UpdateCursor();
    }
    void ClearCombat() {
        foreach(var e in enemies) if(e.body) Destroy(e.body.gameObject); enemies.Clear();
        foreach(var b in bolts) if(b.body) Destroy(b.body.gameObject); bolts.Clear(); target=null;
        if(effects) foreach(Transform t in effects) if(t!=aimLine.transform && t!=channelLine.transform) Destroy(t.gameObject);
        channelLine.enabled=false;
    }
    void MovePlayer(float dt){
        Vector3 forward=orbit.Forward,right=Vector3.Cross(Vector3.up,forward);
        Vector3 input=right*controls.Move.x+forward*controls.Move.y;
        if(lockedTarget!=null){facing=lockedTarget.Position-player.position;facing.y=0;facing.Normalize();}
        else if(attackTimer>0&&assistTime>0)facing=assistDir;
        else if(controls.Primary||controls.Secondary||Blocking||attackTimer>0)facing=forward;
        else if(input.sqrMagnitude>.015f)facing=input.normalized;
        if(controls.Dodge&&inputGrace<=0)Roll(input.sqrMagnitude>.01f?input:-facing);
        if(dodgeTime>0)facing=dodgeDirection;
        bool sprint=controls.Sprint&&stamina>0&&!Blocking&&attackTimer<=0&&guardBroken<=0;
        float moveSpeed=Blocking?1.45f:attackTimer>0?.35f:guardBroken>0?.6f:sprint?CombatTuning.SprintSpeed:CombatTuning.JogSpeed;
        Vector3 desired=input*moveSpeed;
        float acceleration=desired.sqrMagnitude>planarVelocity.sqrMagnitude?CombatTuning.Acceleration:CombatTuning.Deceleration;
        planarVelocity=Vector3.MoveTowards(planarVelocity,desired,acceleration*dt);
        if(sprint&&input.sqrMagnitude>.01f&&dodgeTime<=0){stamina=Mathf.Max(0,stamina-dt*12);staminaDelay=.7f;}
        Vector3 velocity=planarVelocity;assistTime=Mathf.Max(0,assistTime-dt);if(attackTimer>0&&dodgeTime<=0)velocity+=facing*lunge;lunge=Mathf.MoveTowards(lunge,0,dt*9f);
        if(dodgeTime>0){float t=1-dodgeTime/CombatTuning.RollDuration;velocity=dodgeDirection*(CombatTuning.RollDistance/CombatTuning.RollDuration)*(6*t*(1-t));}
        Vector3 flat=new Vector3(player.position.x,0,player.position.z);
        if(flat.magnitude>ArenaRadius-.3f&&Vector3.Dot(velocity,flat)>0)velocity=Vector3.ProjectOnPlane(velocity,flat.normalized);
        verticalSpeed=motor.isGrounded?-2:Mathf.Max(-16,verticalSpeed-18*dt);
        motor.Move((velocity+Vector3.up*verticalSpeed)*dt);
        if(facing.sqrMagnitude>.01f)player.rotation=Quaternion.RotateTowards(player.rotation,Quaternion.LookRotation(facing),CombatTuning.TurnSpeed*dt);
    }
    public bool Roll(Vector3 direction){
        if(stamina<25 || dodgeCooldown>0 || attackTimer>.24f || guardBroken>0)return false;
        stamina-=25;staminaDelay=.8f;dodgeCooldown=CombatTuning.RollCooldown;dodgeTime=CombatTuning.RollDuration;attackTimer=0;planarVelocity=Vector3.zero;AttackQueued=false;dodgeDirection=direction.normalized;rollPractice++;facing=dodgeDirection;if(dodgeDirection.sqrMagnitude>.01f)player.rotation=Quaternion.LookRotation(dodgeDirection);
        Burst(player.position+Vector3.up*.15f,new Color(.6f,.55f,.4f),6);CombatFX.Dust(effects,player.position+Vector3.up*.1f,new Color(.75f,.65f,.5f),12);orbit.Punch(.3f);Sound(pushSound);return true;
    }
    public bool BasicAttack(){
        if(stamina<18 || dodgeTime>0 || guardBroken>0 || Blocking)return false;
        if(attackTimer>0){if(attackTimer<.42f)AttackQueued=true;return false;}
        stamina-=18;staminaDelay=.8f;combo=combo%3+1;comboIdle=0;attackTimer=CombatTuning.AttackDuration(combo);swingResolved=false;basicPractice++;Sound(pushSound);
        Enemy near=null;float bestD=4.2f;foreach(var en in enemies){if(!en.body||en.hp<=0)continue;Vector3 dd=en.Position-player.position;dd.y=0;float ds=dd.magnitude;if(ds<bestD&&Vector3.Dot(facing,dd.normalized)>.25f){bestD=ds;near=en;}}
        if(near!=null){Vector3 dd=near.Position-player.position;dd.y=0;assistDir=dd.normalized;assistTime=.4f;lunge=Mathf.Clamp((bestD-1.6f)*2.0f,.2f,1.8f);}else{assistDir=facing;assistTime=0;lunge=.55f;}
        return true;
    }
    void UpdateMelee(float dt){
        if(controls.Attack && inputGrace<=0)BasicAttack();
        if(attackTimer<=0){comboIdle+=dt;if(comboIdle>.85f)combo=0;return;}
        attackTimer=Mathf.Max(0,attackTimer-dt);
        if(!swingResolved && attackTimer<CombatTuning.AttackDuration(combo)-CombatTuning.ImpactTime(combo)){
            swingResolved=true;
            foreach(var e in enemies.ToArray()){
                Vector3 delta=e.Position-player.position;delta.y=0;
                if(delta.magnitude<(e.boss?CombatTuning.BossMeleeRange:CombatTuning.MeleeRange) && Vector3.Dot(player.forward,delta.normalized)>.35f){
                    Hit(e,combo==3?36:24);e.velocity=delta.normalized*(e.boss?.6f:3);if(!e.boss)e.stun=.35f;else Stagger(e,.13f);
                    Vector3 hp=e.Position+Vector3.up*1.15f;Vector3 hd=delta.normalized;Burst(hp,Accent,combo==3?16:10);CombatFX.Sparks(effects,hp-hd*.2f,Color.Lerp(Accent,Color.white,.4f),combo==3?34:20,9f,hd);CombatFX.Flash(effects,hp,Accent,combo==3?2.6f:1.5f);shake=combo==3?.4f:.22f;Sound(hitSound);HitStop(combo==3?.1f:.06f);orbit.Punch(combo==3?1f:.55f);orbit.atmosphere.impact=Mathf.Max(orbit.atmosphere.impact,combo==3?.35f:.15f);orbit.atmosphere.impactColor=Color.Lerp(Accent,Color.white,.5f);hitMarker=.2f;
                }
            }
        }
        if(AttackQueued&&swingResolved&&attackTimer<.24f){attackTimer=0;AttackQueued=false;BasicAttack();}
    }
    public void ToggleLock(){
        if(lockedTarget!=null){lockedTarget=null;return;}
        Enemy best=null;float bestScore=9999;
        foreach(var e in enemies){if(!e.body||e.hp<=0)continue;Vector3 delta=e.Position-player.position;delta.y=0;float distance=delta.magnitude;if(distance>CombatTuning.LockRange)continue;
            float alignment=Vector3.Dot(orbit.Forward,delta.normalized);if(alignment<.15f)continue;float score=distance+(1-alignment)*15;if(score<bestScore){best=e;bestScore=score;}}
        lockedTarget=best;
    }
    public Enemy FindTarget(float range,float alignment) {
        Enemy best=null; float score=float.MaxValue;
        foreach(var e in enemies) {
            if(!e.body || e.hp<=0) continue;
            Vector3 delta=e.Position-player.position; delta.y=0; float d=delta.magnitude;
            if(d>range || Vector3.Dot(facing,delta.normalized)<alignment) continue;
            if(Physics.Linecast(player.position+Vector3.up*1.2f,e.Position+Vector3.up*1.2f,~(1<<8),QueryTriggerInteraction.Ignore))continue;
            float s=d+(1-Vector3.Dot(facing,delta.normalized))*14;
            if(s<score) {best=e;score=s;}
        }
        return best;
    }
    public bool Push() {
        if(primaryCooldown>0 || energy<23) return false;
        energy-=23; primaryCooldown=1.25f; bool hit=false;
        foreach(var e in enemies.ToArray()) {
            Vector3 delta=e.Position-player.position; delta.y=0;
            if(delta.magnitude<CombatTuning.ForceRange && Vector3.Dot(facing,delta.normalized)>.48f) {
                Hit(e,e.boss?38:38); e.velocity=delta.normalized*(e.boss?4:18); e.stun=e.boss?0:1.2f; hit=true;
                if(e.boss) Stagger(e,.5f);
            }
        }
        if(hit) primaryPractice+=1;
        // A fan-shaped shockwave visually matches the hit cone.
        Vector3 start=player.position+Vector3.up*.3f;
        var fx=new GameObject("Force Push wave"); fx.transform.position=start;
        var points=new Vector3[40]; for(int i=0;i<40;i++) points[i]=Quaternion.Euler(0,Mathf.Lerp(-60,60,(float)i/39),0)*facing;
        var l=ArenaArt.Line("Push crescent",points,Accent,.14f,fx.transform); l.useWorldSpace=false;
        var effect=fx.AddComponent<ForceEffect>(); effect.duration=.45f; effect.scaleEffect=true; effect.from=1; effect.to=11; effect.line=l;
        castPose=.4f;Sound(pushSound); shake=.20f; return true;
    }
    public bool Heal() {
        if(secondaryCooldown>0 || energy<35) return false;
        if(health>=100) {Notify("Health is full. Save your energy.",2);return false;}
        energy-=35; secondaryCooldown=7; float before=health; health=Mathf.Min(100,health+38);
        if(health>before) secondaryPractice+=1;
        castPose=.7f;healGlow=1.8f; Pulse(player.position,ArenaArt.Cyan,2.4f,1.2f); Burst(player.position+Vector3.up,Accent,24); Sound(healSound); return true;
    }
    public bool Lightning(float dt) {
        if(energy<dt*23) return false;
        energy-=dt*23;
        Enemy enemy=FindTarget(CombatTuning.ForceRange,.7f);
        Vector3 hand=player.position+Vector3.up*1.25f+player.right*.45f;
        Vector3 end=enemy!=null?enemy.Position+Vector3.up*1.2f:hand+facing*8;
        DrawLightning(hand,end);
        if(enemy!=null) { Hit(enemy,dt*43); if(!enemy.boss) enemy.stun=Mathf.Max(enemy.stun,.06f); primaryPractice+=dt; }
        if(lightningSound<=0) {Sound(lightningClip);lightningSound=.14f;}
        return true;
    }
    public bool Choke(float dt) {
        Enemy enemy=FindTarget(CombatTuning.ForceRange,.68f);
        if(enemy==null) {if(controls.SecondaryDown) Notify("Aim at an enemy and hold Force Choke.",2);return false;}
        if(energy<dt*18) return false;
        energy-=dt*18; enemy.lift=enemy.boss?.15f:1.8f; if(!enemy.boss) enemy.stun=Mathf.Max(enemy.stun,.15f);
        Hit(enemy,dt*(enemy.boss?25:32)); secondaryPractice+=dt;
        if(enemy.boss) Stagger(enemy,dt*.45f);
        channelLine.enabled=true; channelLine.sharedMaterial=ArenaArt.Mat(ArenaArt.Violet,true); channelLine.positionCount=2;
        channelLine.SetPosition(0,player.position+Vector3.up*1.3f); channelLine.SetPosition(1,enemy.Position+Vector3.up*1.4f);
        if(channelVfx<=0) {Pulse(enemy.Position,ArenaArt.Violet,1.1f,.4f);channelVfx=.25f;}
        if(lightningSound<=0) {Sound(chokeSound);lightningSound=.35f;}
        return true;
    }
    void DrawLightning(Vector3 a,Vector3 b) {
        channelLine.enabled=true; channelLine.sharedMaterial=ArenaArt.Mat(new Color(.72f,.65f,1),true); channelLine.positionCount=13;
        for(int i=0;i<13;i++) channelLine.SetPosition(i,Vector3.Lerp(a,b,i/12f)+(i==0||i==12?Vector3.zero:UnityEngine.Random.insideUnitSphere*.12f));
        if(channelVfx<=0) {Burst(b,ArenaArt.Violet,2);channelVfx=.08f;}
    }
    public Enemy Spawn(Vector3 pos,bool training=false,bool boss=false,bool ranged=false) {
        var e=new Enemy {training=training,boss=boss,ranged=ranged,maxHP=boss?600:training?150:68,attack=2};e.hp=e.maxHP;
        e.body=ArenaArt.Character(boss?"Guardian of the Rift":training?"Training sentinel":ranged?"Ranged sentinel":"Sentinel",pos,boss?ArenaArt.Gold:training?ArenaArt.Cyan:ArenaArt.Red,actors,boss);
        var capsule=e.body.gameObject.AddComponent<CapsuleCollider>();capsule.radius=.28f;capsule.height=1.85f;capsule.center=Vector3.up*.93f;
        var rigidbody=e.body.gameObject.AddComponent<Rigidbody>();rigidbody.isKinematic=true;rigidbody.useGravity=false;
        enemies.Add(e); Pulse(pos,boss?ArenaArt.Gold:ArenaArt.Red,boss?2:1,.5f); return e;
    }
    void SpawnWave() {
        wave++; stageTime=0; waveDelay=0; health=Mathf.Min(100,health+12); energy=100;
        int count=3+wave;
        for(int i=0;i<count;i++) {
            float a=(i+.35f)*Mathf.PI*2/count;
            Spawn(new Vector3(Mathf.Cos(a)*12,0,Mathf.Sin(a)*12),false,false,i%3==2);
        }
        Notify("WAVE "+wave+" / 2 — Guards approaching",3);
    }
    void Progress(float dt) {
        if(health<=0) {stage=DemoStage.Defeat;stageTime=0;channelLine.enabled=false;Sound(hitSound);return;}
        if(stage==DemoStage.Tutorial) {
            if(enemies.Count==0 && (primaryPractice<RequiredPrimary || secondaryPractice<RequiredSecondary)) Spawn(new Vector3(0,0,3),true);
            tutorialStep=primaryPractice>=RequiredPrimary?(secondaryPractice>=RequiredSecondary?2:1):0;
            if(tutorialStep==2 && controls.Confirm) {
                AdvanceTutorial();
            }
        } else if(stage==DemoStage.Waves && enemies.Count==0) {
            waveDelay+=dt;
            if(waveDelay>1.8f) { if(wave<2) SpawnWave(); else {ClearCombat(); stage=DemoStage.BossIntro; stageTime=0; health=energy=100;primaryCooldown=secondaryCooldown=0;} }
        } else if(stage==DemoStage.Boss && enemies.Count==0) {
            stage=DemoStage.Victory;stageTime=0;Pulse(Vector3.zero,Accent,15,2);Sound(healSound);
        }
    }
    public void AdvanceTutorial() {
        if(stage!=DemoStage.Tutorial || tutorialStep!=2)return;
        ClearCombat(); stage=DemoStage.Waves; health=energy=100; primaryCooldown=secondaryCooldown=0; SpawnWave();
    }
    public float RequiredPrimary => side==ForceSide.Light?1:.75f;
    public float RequiredSecondary => side==ForceSide.Light?1:.75f;
    public void StartBoss() {
        ClearCombat();stage=DemoStage.Boss;stageTime=0;Teleport(new Vector3(0,0,-7));facing=Vector3.forward;health=energy=stamina=100;orbit.ResetView();inputGrace=.25f;
        Spawn(new Vector3(0,0,6),false,true);Notify("RIFT WARDEN — Dodge attacks. Interrupt with the Force.",4);
    }
    void LateUpdate() {
        if(stage==DemoStage.BossIntro && !paused && controls.Confirm) StartBoss();
    }
    void Stagger(Enemy e,float value) {
        e.stagger+=value;
        if(e.stagger>=1) {e.stagger=0;e.stun=2.5f;e.telegraph=0;Notify("STAGGERED — The Warden is stunned!",2);Burst(e.Position+Vector3.up*2,Accent,24);}
    }
    Vector3 EnemyStep(Enemy enemy,Vector3 displacement,bool steer=true){
        Vector3 flat=new Vector3(displacement.x,0,displacement.z);float distance=flat.magnitude;if(distance<.00001f)return displacement;
        Vector3 p=enemy.Position;float radius=enemy.boss?.36f:.26f;
        if(!Physics.CapsuleCast(p+Vector3.up*.38f,p+Vector3.up*1.5f,radius,flat/distance,out var hit,distance+.035f,~(1<<8),QueryTriggerInteraction.Ignore))return displacement;
        Vector3 slide=Vector3.ProjectOnPlane(flat,hit.normal);slide.y=0;
        if(steer&&slide.magnitude<distance*.3f)slide=Vector3.Cross(Vector3.up,hit.normal).normalized*distance*(enemy.body.GetSiblingIndex()%2==0?1:-1);
        if(slide.sqrMagnitude<.00001f)return Vector3.zero;
        if(Physics.CapsuleCast(p+Vector3.up*.38f,p+Vector3.up*1.5f,radius,slide.normalized,slide.magnitude+.035f,~(1<<8),QueryTriggerInteraction.Ignore))return Vector3.zero;
        return slide;
    }
    void UpdateEnemies(float dt){
        foreach(var e in enemies.ToArray()){
            if(!e.body)continue;if(e.hp<=0){Kill(e);continue;}
            e.hit=Mathf.Max(0,e.hit-dt);e.attack-=dt;e.stun-=dt;e.action=Mathf.Max(0,e.action-dt);
            Vector3 pos=e.Position+EnemyStep(e,e.velocity*dt,false);e.velocity=Vector3.Lerp(e.velocity,Vector3.zero,dt*5);pos.y=Mathf.MoveTowards(pos.y,-.075f+e.lift,dt*8);e.lift=Mathf.Max(0,e.lift-dt*6);
            Vector3 flat=Vector3.ClampMagnitude(new Vector3(pos.x,0,pos.z),ArenaRadius);pos.x=flat.x;pos.z=flat.z;e.body.position=pos;
            Vector3 delta=player.position-e.Position;delta.y=0;float d=delta.magnitude;
            if(delta.sqrMagnitude>.01f)e.body.rotation=Quaternion.Slerp(e.body.rotation,Quaternion.LookRotation(delta),dt*5);
            var visual=e.body.GetComponent<CharacterVisual>();visual.attack=e.action>0?Mathf.Max(.001f,1-e.action/CombatTuning.AttackDuration(e.boss?3:e.pattern%2+1)):0;visual.combo=e.boss?3:e.pattern%2+1;visual.channel=e.telegraph>0?1:0;visual.warn=e.boss&&e.pattern%3==0&&e.telegraph>0?1:0;
            if(e.stun>0)continue;
            if(e.training){if(e.attack<=0 && tutorialStep>=1){Shoot(e.Position+Vector3.up*1.1f,delta.normalized,5,6);e.attack=5;}continue;}
            if(e.boss){UpdateBoss(e,delta,dt);continue;}
            if(e.ranged){
                if(d>10)e.body.position+=EnemyStep(e,delta.normalized*dt*2);else if(d<5)e.body.position+=EnemyStep(e,-delta.normalized*dt*2);
                if(e.attack<=0){Shoot(e.Position+Vector3.up*1.2f,delta.normalized,8,10);e.attack=2.5f;e.action=.55f;}
            }else{
                if(e.telegraph>0){
                    e.telegraph-=dt;
                    if(!e.hitResolved&&e.telegraph<=CombatTuning.AttackDuration(e.pattern%2+1)-CombatTuning.ImpactTime(e.pattern%2+1)){e.hitResolved=true;if(d<2.05f)DamagePlayer(14,e.Position);}
                    if(e.telegraph<=0){e.attack=1.15f;e.pattern++;};
                    continue;
                }
                if(d<1.9f && e.attack<=0){e.telegraph=CombatTuning.AttackDuration(e.pattern%2+1);e.action=e.telegraph;e.hitResolved=false;continue;}
                Vector3 separation=Vector3.zero;foreach(var other in enemies)if(other!=e&&other.body){Vector3 v=e.Position-other.Position;v.y=0;if(v.sqrMagnitude<1.8f&&v.sqrMagnitude>.01f)separation+=v.normalized;}
                if(d>1.65f)e.body.position+=EnemyStep(e,(delta.normalized*2.25f+separation)*dt);
            }
        }
    }
    void UpdateBoss(Enemy e,Vector3 delta,float dt){
        bool enraged=e.hp<e.maxHP*.5f;
        if(e.telegraph>0){
            e.telegraph-=dt;
            if(e.pattern%3==2&&!e.hitResolved&&e.telegraph<=CombatTuning.AttackDuration(3)-CombatTuning.ImpactTime(3)){e.hitResolved=true;if(delta.magnitude<2.9f)DamagePlayer(22,e.Position);}
            if(e.telegraph<=0){
                int pattern=e.pattern%3;
                if(pattern==0){if(Vector3.Distance(new Vector3(player.position.x,0,player.position.z),e.attackPoint)<3.3f)DamagePlayer(26,e.Position,true);
                    Pulse(e.attackPoint,ArenaArt.Red,3.3f,.4f);Burst(e.attackPoint+Vector3.up*.4f,ArenaArt.Gold,26);shake=.5f;Sound(pushSound);}
                else if(pattern==1){Vector3 aim=e.attackPoint-e.Position;aim.y=0;for(int i=-2;i<=2;i++)Shoot(e.Position+Vector3.up*1.4f,Quaternion.Euler(0,i*13,0)*aim.normalized,enraged?11:8,14);}
                else{Pulse(e.Position,ArenaArt.Gold,2.6f,.25f);}
                e.pattern++;e.attack=enraged?1.1f:1.8f;
            }return;
        }
        if(delta.magnitude>(e.pattern%3==2?2.3f:4.2f))e.body.position+=EnemyStep(e,delta.normalized*dt*(enraged?2.5f:1.9f));
        if(e.attack<=0){e.telegraph=e.pattern%3==2?CombatTuning.AttackDuration(3):enraged?.85f:1.25f;e.hitResolved=false;if(e.pattern%3==2)e.action=CombatTuning.AttackDuration(3);e.attackPoint=new Vector3(player.position.x,0,player.position.z);
            if(e.pattern%3==0){Warning(e.attackPoint,3.3f,e.telegraph);Notify("PULSE — Leave the red circle. This attack cannot be blocked.",1.6f);}
            else if(e.pattern%3==1){Notify("VOLLEY — Block or roll sideways.",1.5f);}
            else Notify("SLASH — Prepare to block or dodge.",1.5f);
        }
        e.stagger=Mathf.Max(0,e.stagger-dt*.05f);
    }
    void Shoot(Vector3 pos,Vector3 direction,float speed,float damage) {
        var b=new Bolt {body=ArenaArt.Shape("Plasma bolt",PrimitiveType.Sphere,pos,new Vector3(.22f,.22f,.7f),ArenaArt.Red,effects,true).transform,velocity=direction*speed,damage=damage};
        b.body.rotation=Quaternion.LookRotation(direction);bolts.Add(b);
    }
    void UpdateBolts(float dt) {
        foreach(var b in bolts.ToArray()) {
            if(!b.body) {bolts.Remove(b);continue;}
            Vector3 old=b.body.position;b.body.position+=b.velocity*dt;b.life-=dt;
            Vector3 a=new Vector3(old.x,0,old.z),end=new Vector3(b.body.position.x,0,b.body.position.z);
            Vector3 segment=end-a;float t=segment.sqrMagnitude>0?Mathf.Clamp01(Vector3.Dot(player.position-a,segment)/segment.sqrMagnitude):0;
            bool hit=Vector3.Distance(a+segment*t,player.position)<.65f;
            if(hit) DamagePlayer(b.damage,old);
            if(hit||b.life<=0||b.body.position.magnitude>45) {Destroy(b.body.gameObject);bolts.Remove(b);}
        }
    }
    public void Hit(Enemy e,float damage) {if(e.hp<=0)return;e.hp-=damage;e.hit=.15f;var cv=e.body?e.body.GetComponent<CharacterVisual>():null;if(cv)cv.Hurt(e.Position-player.position,damage>6?(e.boss?.35f:1f):.18f);if(!e.boss&&damage>=20){e.action=e.telegraph=0;e.attack=Mathf.Max(e.attack,.6f);}}
    public void Kill(Enemy e) {
        if(!enemies.Contains(e))return;
        Burst(e.Position+Vector3.up,ArenaArt.Gold,e.boss?50:14);CombatFX.Flash(effects,e.Position+Vector3.up,ArenaArt.Gold,e.boss?7f:3f);HitStop(e.boss?.18f:.08f);orbit.Punch(e.boss?1.4f:.8f);Pulse(e.Position,Accent,e.boss?5:1.4f,.4f);
        if(!e.training) {kills++;energy=Mathf.Min(100,energy+8);}
        if(target==e)target=null;if(lockedTarget==e)lockedTarget=null;var death=e.body.gameObject.AddComponent<FallenActor>();death.visual=e.body.GetComponent<CharacterVisual>();var col=e.body.GetComponent<Collider>();if(col)col.enabled=false;enemies.Remove(e);Sound(hitSound);
    }
    public void DamagePlayer(float amount){DamagePlayer(amount,player.position+facing*2);}
    public void DamagePlayer(float amount,Vector3 source,bool unblockable=false){
        if(Invulnerable || health<=0)return;
        Vector3 incoming=source-player.position;incoming.y=0;
        if(Blocking && !unblockable && Vector3.Dot(player.forward,incoming.normalized)>.15f){
            float cost=amount*1.05f+3;staminaDelay=.85f;
            if(stamina>=cost){stamina-=cost;blockPractice++;Burst(player.position+Vector3.up*1.2f+player.forward*.6f,Accent,8);CombatFX.Sparks(effects,player.position+Vector3.up*1.2f+player.forward*.7f,Color.white,22,8f,player.forward);HitStop(.045f);orbit.Punch(.4f);Sound(hitSound);
                if(blockTime<.2f){Enemy e=enemies.Find(x=>x.body&&Vector3.Distance(x.Position,source)<1);if(e!=null){e.stun=e.boss?.6f:1.2f;e.action=e.telegraph=0;var recoil=e.body.GetComponent<CharacterVisual>();if(recoil)recoil.Hurt(e.Position-player.position,.9f);}Notify("PARRIED",1);}
                return;
            }
            stamina=0;guardBroken=1.2f;staminaDelay=1.5f;Notify("GUARD BROKEN",1.4f);
        }
        if(stage==DemoStage.Tutorial)return;
        health=Mathf.Max(0,health-amount);hurtFlash=.65f;shake=.3f;orbit.atmosphere.hurt=Mathf.Max(orbit.atmosphere.hurt,.9f);orbit.atmosphere.aberration=.006f;visuals.Hurt(player.position-source,.9f);orbit.Punch(.7f);HitStop(.05f);if(amount>1)Sound(hitSound);
    }
    public void Notify(string text,float duration=2.5f) {notice=text;noticeTime=duration;}
    public void Pulse(Vector3 position,Color color,float radius,float duration) {
        var g=new GameObject("Force ripple");g.transform.SetParent(effects);g.transform.position=new Vector3(position.x,.1f,position.z);
        var l=ArenaArt.Ring(Vector3.zero,1,color*1.8f,.1f,g.transform);l.useWorldSpace=false;
        var fx=g.AddComponent<ForceEffect>();fx.duration=duration;fx.scaleEffect=true;fx.from=.2f;fx.to=radius;fx.line=l;
    }
    void Warning(Vector3 pos,float radius,float duration) {
        var l=ArenaArt.Ring(new Vector3(pos.x,.08f,pos.z),radius,ArenaArt.Red,.13f,effects);
        var fx=l.gameObject.AddComponent<ForceEffect>();fx.duration=duration;
        ArenaArt.Line("Danger marker",new[]{pos+new Vector3(-radius,.07f,0),pos+new Vector3(radius,.07f,0)},ArenaArt.Red*.7f,.035f,l.transform);
    }
    public void Burst(Vector3 pos,Color color,int count) {
        CombatFX.Sparks(effects,pos,color,count*2,6.5f);
        if(count>=8)CombatFX.Flash(effects,pos,color,count>=20?3.2f:1.6f);
    }
    void OnApplicationFocus(bool focused){if(opening)opening.SetFocus(focused);if(!focused&&!SkipInput&&Playing){paused=true;Time.timeScale=0;planarVelocity=Vector3.zero;UpdateCursor();}}
    void OnDestroy() {Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
}
public class EnergyMote : MonoBehaviour {
    public Vector3 velocity;public float life;
    void Update(){transform.position+=velocity*Time.deltaTime;velocity+=Vector3.down*Time.deltaTime*3;life-=Time.deltaTime;if(life<=0)Destroy(gameObject);}
}
}
