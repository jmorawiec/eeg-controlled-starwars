using UnityEngine;
using UnityEngine.Rendering;
namespace ForceDemo {
[DefaultExecutionOrder(80)]
public class CharacterVisual:MonoBehaviour {
    public static bool LightHeroSelected=true;
    public Animator animator;public Transform pivot,body,weapon;public LineRenderer blade;public Light weaponLight;
    public bool isPlayer,boss;public float speed,attack,roll,channel;public int combo;public bool blocking;public Color accent;
    public bool dead;public float hitReaction;public DressKind kind;public Renderer[] tintRenderers;public Color baseTint=Color.white;public float flash,flinch,warn;public Vector3 flinchDir;public ClothCape cape,skirt;bool moving;string activeUpper="Empty";float upperWeight;Vector3 lastVelocity,leanSmooth,previous2;TrailRenderer trail;MaterialPropertyBlock block;
    VolumetricLines.VolumetricLineBehavior volumetricBlade;Transform rightHand;Vector3 previous;Material glow;string activeState="";float naturalRunSpeed=4,groundOffset,rollRecovery;public Transform Hips,LeftFoot,RightFoot;
    public void ResetMotion(){dead=false;hitReaction=attack=roll=channel=0;previous=previous2=transform.position;lastVelocity=Vector3.zero;activeUpper="Empty";upperWeight=0;speed=0;activeState="";groundOffset=rollRecovery=0;if(body)body.localPosition=Vector3.down*.75f;if(animator){animator.Rebind();animator.Update(0);}}
    public static Transform Create(string name,Vector3 position,Color color,Transform parent,bool boss,bool player){
        var g=new GameObject(name);g.transform.SetParent(parent);g.transform.position=position;g.layer=8;
        var v=g.AddComponent<CharacterVisual>();v.isPlayer=player;v.boss=boss;v.accent=color;
        v.pivot=new GameObject("Pose pivot").transform;v.pivot.SetParent(g.transform,false);v.pivot.localPosition=Vector3.up*.75f;
        var dressKind=CharacterDress.KindOf(name,boss,player);var prefab=Resources.Load<GameObject>(player?(LightHeroSelected?"Generated/LightHero":"Generated/Clovis"):(dressKind==DressKind.Ranged||dressKind==DressKind.Mentor)?"Generated/Clovis":"Generated/Explorer");if(!prefab)prefab=Resources.Load<GameObject>(player?"Generated/Clovis":"Generated/Explorer");
        if(prefab)v.body=Instantiate(prefab,v.pivot).transform;
        else v.body=new GameObject("Missing imported character").transform;
        v.body.localPosition=Vector3.down*.75f;v.animator=v.body.GetComponentInChildren<Animator>();
        if(v.animator && v.animator.isHuman){
            v.animator.applyRootMotion=false;v.animator.Rebind();v.animator.Update(0);
            v.rightHand=v.animator.GetBoneTransform(HumanBodyBones.RightHand);
            v.Hips=v.animator.GetBoneTransform(HumanBodyBones.Hips);v.LeftFoot=v.animator.GetBoneTransform(HumanBodyBones.LeftFoot);v.RightFoot=v.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            foreach(var clip in Resources.LoadAll<AnimationClip>("WorldAssets/m@RunForwards"))if(clip.name=="Run")v.naturalRunSpeed=Mathf.Max(2,clip.averageSpeed.magnitude);
        }
        v.Weapon();v.previous=position;CharacterDress.Apply(v,CharacterDress.KindOf(name,boss,player));
        if(boss)g.transform.localScale=Vector3.one*1.35f;
        foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=8;
        return g.transform;
    }
    public void SetAccent(Color color){accent=color;if(trail){var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.Lerp(color,Color.white,.55f),0),new GradientColorKey(color,.35f),new GradientColorKey(color,1)},new[]{new GradientAlphaKey(.95f,0),new GradientAlphaKey(.55f,.4f),new GradientAlphaKey(0,1)});trail.colorGradient=g;}if(cape&&(kind==DressKind.Hero||kind==DressKind.Mentor))cape.SetColor(kind==DressKind.Hero?new Color(color.r*.11f+.02f,color.g*.11f+.03f,color.b*.11f+.04f):Color.Lerp(new Color(.8f,.76f,.64f),color,.05f));if(glow)glow.SetColor("_EmissionColor",color*5);if(blade){blade.startColor=blade.endColor=Color.white;}if(weaponLight)weaponLight.color=color;if(volumetricBlade)volumetricBlade.LineColor=color*1.2f;}
    // Blade direction from the hand's anatomy (thumb side, cocked slightly toward the forearm), so it holds on any humanoid rig and in every pose.
    public static Quaternion GripRotation(Animator animator,Transform hand,Quaternion fallback){
        if(!animator||!animator.isHuman||!hand)return fallback;
        var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);var thumb=animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
        if(!lower||!thumb||!little)return fallback;
        Vector3 f=(hand.position-lower.position).normalized;Vector3 t=thumb.position-little.position;t-=Vector3.Dot(t,f)*f;
        if(t.sqrMagnitude<1e-6f)return fallback;t.Normalize();
        return Quaternion.FromToRotation(Vector3.up,(f*.82f+t*.57f).normalized); // blade continues the forearm line, cocked ~35 degrees toward the thumb
    }
    public float BladeExtension { get; private set; } = 1;
    public void SetBladeExtension(float value){
        BladeExtension=Mathf.Clamp01(value);
        if(!weapon)return;
        var core=weapon.Find("Luminous blade");
        if(core){core.localPosition=Vector3.up*(.2f+.57f*BladeExtension);core.localScale=new Vector3(.028f,.56f*BladeExtension,.028f);}
        if(volumetricBlade){volumetricBlade.gameObject.SetActive(BladeExtension>.001f);volumetricBlade.SetStartAndEndPoints(Vector3.up*.2f,Vector3.up*(.2f+1.14f*BladeExtension));}
        else if(blade){blade.enabled=BladeExtension>.001f;blade.SetPosition(1,Vector3.up*(.2f+1.14f*BladeExtension));}
        if(weaponLight)weaponLight.intensity=1.5f*BladeExtension;
        if(trail&&BladeExtension<1)trail.Clear();
    }
    void Weapon(){
        weapon=new GameObject("Glowing Stick").transform;weapon.SetParent(pivot,false);
        ArenaArt.Shape("Machined grip",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.037f,.14f,.037f),new Color(.18f,.2f,.2f),weapon);
        for(int i=0;i<5;i++)ArenaArt.Shape("Grip ring",PrimitiveType.Cylinder,new Vector3(0,-.1f+i*.048f,0),new Vector3(.043f,.009f,.043f),new Color(.05f,.06f,.06f),weapon);
        ArenaArt.Shape("Emitter",PrimitiveType.Cylinder,new Vector3(0,.165f,0),new Vector3(.052f,.025f,.052f),new Color(.4f,.42f,.4f),weapon);
        var gripMaterial=Resources.Load<Material>("Generated/WeaponGrip");if(gripMaterial)foreach(var r in weapon.GetComponentsInChildren<Renderer>())r.sharedMaterial=gripMaterial;
        var core=ArenaArt.Shape("Luminous blade",PrimitiveType.Cylinder,new Vector3(0,.77f,0),new Vector3(.028f,.56f,.028f),Color.white,weapon,true);
        glow=new Material(Shader.Find("Standard"));glow.color=Color.white;glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",accent*5);core.GetComponent<Renderer>().sharedMaterial=glow;
        blade=ArenaArt.Line("Blade corona",new[]{new Vector3(0,.2f,0),new Vector3(0,1.34f,0)},accent,.055f,weapon);blade.useWorldSpace=false;blade.sharedMaterial=glow;
        var bladeMaterial=Resources.Load<Material>("Generated/BladeVolumetric");
        if(bladeMaterial){
            blade.enabled=false;core.GetComponent<Renderer>().enabled=false;
            var beam=new GameObject("Volumetric blade");beam.transform.SetParent(weapon,false);
            volumetricBlade=beam.AddComponent<VolumetricLines.VolumetricLineBehavior>();
            volumetricBlade.TemplateMaterial=bladeMaterial;volumetricBlade.LineColor=accent*1.2f;
            volumetricBlade.LineWidth=.075f;volumetricBlade.LightSaberFactor=.78f;
            volumetricBlade.SetStartAndEndPoints(Vector3.up*.2f,Vector3.up*1.34f);
        }
        weaponLight=new GameObject("Blade light").AddComponent<Light>();weaponLight.transform.SetParent(weapon,false);weaponLight.transform.localPosition=Vector3.up*.7f;weaponLight.type=LightType.Point;weaponLight.color=accent;weaponLight.intensity=1.5f;weaponLight.range=3;weaponLight.shadows=LightShadows.None;
        var tipObj=new GameObject("Blade tip");tipObj.transform.SetParent(weapon,false);tipObj.transform.localPosition=Vector3.up*1.28f;
        trail=tipObj.AddComponent<TrailRenderer>();trail.time=.12f;trail.minVertexDistance=.04f;trail.widthMultiplier=.23f;trail.widthCurve=new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0));trail.emitting=false;trail.numCapVertices=2;trail.shadowCastingMode=ShadowCastingMode.Off;
        var glowMat=Resources.Load<Material>("Generated/GlowAdd");if(glowMat)trail.sharedMaterial=glowMat;
        SetAccent(accent);
        if(rightHand){
            weapon.SetParent(rightHand,true);weapon.position=rightHand.position;
            weapon.rotation=GripRotation(animator,rightHand,transform.rotation*Quaternion.Euler(-22,0,-18));
        }
    }
    void Update(){
        float dt=Mathf.Max(Time.deltaTime,.001f);
        Vector3 delta=transform.position-previous;delta.y=0;previous=transform.position;
        // Drive gait from distance actually travelled, including collisions and stopping.
        speed=Mathf.Lerp(speed,Mathf.Min(delta.magnitude/dt,7),1-Mathf.Exp(-dt*18));
        if(trail)trail.emitting=(attack>.20f&&attack<.62f)||(roll>0&&isPlayer&&false);
        if(!animator)return;
        // Hysteresis so the stance does not flicker while speed hovers around the threshold.
        moving=moving?speed>.55f:speed>1f;
        hitReaction=Mathf.Max(0,hitReaction-Time.deltaTime);
        string state=dead?"Death":hitReaction>0?"Hit":roll>0?"Roll":attack>0?"Attack"+Mathf.Clamp(combo,1,3):(blocking&&!moving)?"Guard":(channel>0&&!moving)?"Cast":"Locomotion";
        string upper=dead||hitReaction>0||roll>0?"Empty":(attack>0&&moving&&false)?"UpAttack"+Mathf.Clamp(combo,1,3):(blocking&&moving)?"UpGuard":(channel>0&&moving)?"UpCast":"Empty";
        if(state!=activeState){
            float blend=state=="Roll"?.055f:state=="Locomotion"?.14f:.075f;
            animator.CrossFadeInFixedTime(state,blend,0,0);activeState=state;
        }
        if(animator.layerCount>1){
            if(upper!=activeUpper){animator.CrossFadeInFixedTime(upper,upper=="Empty"?.12f:.07f,1,0);activeUpper=upper;}
            upperWeight=Mathf.MoveTowards(upperWeight,upper=="Empty"?0:1,dt*(upper=="Empty"?8f:14f));animator.SetLayerWeight(1,upperWeight);
        }
        animator.SetFloat("Speed",speed,.08f,dt);
        Vector3 local=transform.InverseTransformDirection(delta/dt);
        animator.SetFloat("MoveX",Mathf.Clamp(local.x,-5.2f,5.2f),.08f,dt);animator.SetFloat("MoveZ",Mathf.Clamp(local.z,-5.2f,5.2f),.08f,dt);
        float strideSpeed=Mathf.Lerp(1.8f,naturalRunSpeed,Mathf.InverseLerp(1.8f,3.6f,speed));
        animator.speed=1; // Each directional clip is authored for its own blend-tree speed.
    }
    void LateUpdate(){
        float fdt=Mathf.Max(Time.deltaTime,.001f);
        Quaternion lean=Quaternion.identity;
        if(flinch>.001f){Vector3 localDir=transform.InverseTransformDirection(flinchDir);Vector3 axis=Vector3.Cross(Vector3.up,localDir);if(axis.sqrMagnitude>.0001f)lean=Quaternion.AngleAxis(flinch*15,axis.normalized);}
        {   // Move-lean: tilt into speed, acceleration and turns so gait never reads as a sliding statue.
            Vector3 vel=(transform.position-previous2)/fdt;previous2=transform.position;vel.y=0;if(vel.magnitude>12)vel=Vector3.zero;
            Vector3 acc=(vel-lastVelocity)/fdt;lastVelocity=vel;Vector3 accLocal=transform.InverseTransformDirection(Vector3.ClampMagnitude(acc,40));Vector3 velLocal=transform.InverseTransformDirection(vel);
            float forwardLean=Mathf.Clamp(velLocal.z*1.15f+accLocal.z*.09f,-3f,9f);float sideLean=Mathf.Clamp(-velLocal.x*.9f-accLocal.x*.08f,-6f,6f);
            if(roll>0||attack>0&&!moving)forwardLean=sideLean=0;
            leanSmooth=Vector3.Lerp(leanSmooth,new Vector3(forwardLean,0,sideLean),1-Mathf.Exp(-fdt*9));
            lean=lean*Quaternion.Euler(leanSmooth.x,0,leanSmooth.z);
        }
        pivot.localRotation=lean;flinch=Mathf.MoveTowards(flinch,0,fdt*4.5f);flash=Mathf.MoveTowards(flash,0,fdt*6f);ApplyTint();
        if(roll>0)rollRecovery=.18f;else rollRecovery=Mathf.Max(0,rollRecovery-Time.deltaTime);
        if(animator&&rollRecovery>0){
            float min=100;
            Contact(HumanBodyBones.LeftFoot,.07f,ref min);Contact(HumanBodyBones.RightFoot,.07f,ref min);
            Contact(HumanBodyBones.LeftHand,.04f,ref min);Contact(HumanBodyBones.RightHand,.04f,ref min);
            Contact(HumanBodyBones.Head,.12f,ref min);Contact(HumanBodyBones.Hips,.14f,ref min);Contact(HumanBodyBones.Chest,.12f,ref min);
            // Ground the retargeted tumble using the skeleton's actual contact point.
            // This changes only height; the controller owns all horizontal movement.
            float target=Mathf.Clamp(transform.position.y-(min-groundOffset),-1.2f,1.5f);
            groundOffset=target>groundOffset?target:Mathf.MoveTowards(groundOffset,target,Time.deltaTime*12);
        }else groundOffset=Mathf.MoveTowards(groundOffset,0,Time.deltaTime*5);
        body.localPosition=Vector3.down*.75f+Vector3.up*groundOffset;
    }
    public void Hurt(Vector3 fromWorldDirection,float power){if(power>.45f&&roll<=0){hitReaction=.28f;activeState="";}flinch=Mathf.Max(flinch,power);flash=Mathf.Max(flash,Mathf.Clamp01(power));fromWorldDirection.y=0;if(fromWorldDirection.sqrMagnitude>.001f)flinchDir=fromWorldDirection.normalized;}
    void ApplyTint(){
        if(tintRenderers==null||tintRenderers.Length==0)return;if(block==null)block=new MaterialPropertyBlock();
        Color c=Color.Lerp(baseTint,new Color(2.4f,.5f,.35f),warn*.24f)*(1+flash*.35f);c.a=1;
        foreach(var r in tintRenderers){if(!r)continue;r.GetPropertyBlock(block);block.SetColor("_Color",c);r.SetPropertyBlock(block);}
    }
    void Contact(HumanBodyBones bone,float radius,ref float minimum){var t=animator.GetBoneTransform(bone);if(t)minimum=Mathf.Min(minimum,t.position.y-radius);}
}
}
