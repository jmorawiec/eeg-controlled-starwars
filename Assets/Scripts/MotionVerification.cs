using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
namespace ForceDemo {
public class MotionVerification:MonoBehaviour {
    ForceGame game;string output;int passed,frame;bool recording;float nextCapture;
    StringBuilder log=new StringBuilder();StringBuilder times=new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfRequested(){
        var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--verify-motion")<0)return;
        var q=new GameObject("Motion regression verification").AddComponent<MotionVerification>();
        foreach(var a in args)if(a.StartsWith("--qa-output="))q.output=a.Substring(12);
        if(string.IsNullOrEmpty(q.output))q.output=Path.Combine(Application.persistentDataPath,"MotionVerification");Directory.CreateDirectory(q.output);Directory.CreateDirectory(Path.Combine(q.output,"frames"));q.StartCoroutine(q.Guarded(q.Run()));
    }
    IEnumerator Guarded(IEnumerator routine){while(true){object current;try{if(!routine.MoveNext())break;current=routine.Current;}catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL: "+e+"\n"+log);Application.Quit(1);yield break;}yield return current;}}
    void Update(){if(recording&&Time.unscaledTime>=nextCapture){nextCapture=Time.unscaledTime+1/12f;times.AppendLine(Time.unscaledTime.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));ScreenCapture.CaptureScreenshot(Path.Combine(output,"frames","frame"+(frame++).ToString("D4")+".png"));}}
    void Check(bool condition,string text){if(!condition)throw new Exception(text);passed++;log.AppendLine("PASS "+text);Debug.Log("MOTION PASS: "+text);}
    IEnumerator Run(){
        yield return null;yield return null;game=FindFirstObjectByType<ForceGame>();game.SkipInput=true;game.muted=true;
        game.Begin(ForceSide.Light,false);game.controls.UsingPad=true;game.controls.Aim=Vector2.zero;game.orbit.sensitivity=1;
        game.enemies[0].body.position=new Vector3(0,-.075f,9);game.enemies[1].body.position=new Vector3(4,-.075f,10);
        yield return new WaitForSeconds(.5f);
        recording=true;nextCapture=Time.unscaledTime;
        Vector3 start=game.player.position,previous=start;Vector3 footMin=Vector3.one*100,footMax=Vector3.one*-100;float maxStep=0,maxOffset=0,maxHipJump=0;Vector3 previousHip=game.player.InverseTransformPoint(game.visuals.Hips.position);
        game.controls.Move=Vector2.up;float elapsed=0;
        while(elapsed<3){
            yield return null;float dt=Time.deltaTime;elapsed+=dt;
            Vector3 pos=game.player.position;maxStep=Mathf.Max(maxStep,Vector3.Distance(previous,pos));previous=pos;
            Vector3 hip=game.player.InverseTransformPoint(game.visuals.Hips.position);maxOffset=Mathf.Max(maxOffset,new Vector2(hip.x,hip.z).magnitude);maxHipJump=Mathf.Max(maxHipJump,Vector3.Distance(hip,previousHip));previousHip=hip;
            Vector3 foot=game.player.InverseTransformPoint(game.visuals.LeftFoot.position);footMin=Vector3.Min(footMin,foot);footMax=Vector3.Max(footMax,foot);
        }
        float travel=Vector3.Distance(start,game.player.position);
        log.AppendLine("walk: travel="+travel.ToString("F3")+" max frame step="+maxStep.ToString("F3")+" max hip offset="+maxOffset.ToString("F3")+" max hip jump="+maxHipJump.ToString("F3")+" foot excursion="+(footMax-footMin).magnitude.ToString("F3"));
        Check(travel>8.5f&&travel<10.6f,"Jog covers a controlled, human-scale distance in 3 seconds");
        Check(maxStep<.3f,"Physical character never jumps more than 30 cm in a frame");
        Check(maxOffset<.35f,"Animated mesh stays centred on its collision capsule");
        Check(maxHipJump<.2f,"Animation has no loop-reset teleport");
        Check((footMax-footMin).magnitude>.3f,"Walking visibly articulates the legs and feet");
        Check(game.visuals.animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),"Directional locomotion is playing");
        game.controls.Move=Vector2.zero;Vector3 stopStart=game.player.position;yield return new WaitForSeconds(.55f);
        Check(Vector3.Distance(stopStart,game.player.position)<.5f,"Release input stops without excessive sliding");
        Check(game.visuals.speed<.2f,"Animation returns to idle after stopping");
        Vector3 rollStart=game.player.position;game.stamina=100;game.Roll(game.player.forward);float tilt=1,minFootHeight=100,maximumContainerTilt=0;elapsed=0;
        while(elapsed<CombatTuning.RollDuration+.18f){yield return new WaitForEndOfFrame();elapsed+=Time.deltaTime;maximumContainerTilt=Mathf.Max(maximumContainerTilt,Quaternion.Angle(game.visuals.pivot.localRotation,Quaternion.identity));tilt=Mathf.Min(tilt,Vector3.Dot(game.visuals.animator.GetBoneTransform(HumanBodyBones.Chest).up,Vector3.up));minFootHeight=Mathf.Min(minFootHeight,Mathf.Min(game.visuals.LeftFoot.position.y,game.visuals.RightFoot.position.y)-game.player.position.y);}
        float rollTravel=Vector3.Distance(rollStart,game.player.position);log.AppendLine("roll: travel="+rollTravel.ToString("F3")+" minimum chest/up dot="+tilt.ToString("F3"));
        Check(rollTravel>2.5f&&rollTravel<3.1f,"Roll travels approximately 2.8 metres");
        Check(tilt<.6f,"Roll is a skeletal tumble rather than spinning the whole model");
        Check(maximumContainerTilt<12,"Container stays upright while the skeleton performs the roll");
        Check(game.dodgeTime==0,"Roll finishes and returns control");
        log.AppendLine("minimum foot height during roll: "+minFootHeight);Check(minFootHeight>-.12f,"Roll keeps the retargeted body above the floor");
        var near=game.enemies[0];var far=game.enemies[1];float nearHP=near.hp,farHP=far.hp;
        // Place the training target at the intended contact distance before the swing.
        near.body.position=game.player.position+game.player.forward*1.75f;far.body.position=game.player.position+game.player.forward*3.4f;
        game.energy=0;game.BasicAttack();float arc=0;Vector3 handStart=game.player.InverseTransformPoint(game.visuals.weapon.position);elapsed=0;
        while(elapsed<.8f){yield return null;elapsed+=Time.deltaTime;arc=Mathf.Max(arc,Vector3.Distance(handStart,game.player.InverseTransformPoint(game.visuals.weapon.position)));}
        Check(near.hp<nearHP,"Weapon hits at arm-and-blade distance");Check(far.hp==farHP,"Weapon cannot hit a target 3.4 metres away");Check(arc>.15f,"Attack is animated by the skeleton");
        Check(game.visuals.weapon.parent==game.visuals.animator.GetBoneTransform(HumanBodyBones.RightHand),"Weapon follows the animated hand");
        game.controls.Block=true;yield return new WaitForSeconds(.4f);Check(game.visuals.animator.GetCurrentAnimatorStateInfo(0).IsName("Guard"),"Block has an explicit animation state");
        game.controls.Block=false;game.controls.Aim=new Vector2(.6f,0);yield return new WaitForSeconds(.7f);game.controls.Aim=Vector2.zero;
        recording=false;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"gameplay.png"));
        yield return new WaitForSeconds(.3f);
        game.ReturnToMenu();game.Begin(ForceSide.Dark,false);game.controls.Move=Vector2.zero;game.controls.Aim=Vector2.zero;game.lockedTarget=game.enemies[0];yield return new WaitForSeconds(.35f);
        game.controls.Move=Vector2.left;yield return new WaitForSeconds(.7f);Check(game.visuals.animator.GetFloat("MoveX")<-.8f,"Locked-on movement uses directional strafe animation");game.controls.Move=Vector2.zero;
        yield return new WaitForSeconds(.5f);
        File.WriteAllText(Path.Combine(output,"times.txt"),times.ToString());log.AppendLine("Frames captured: "+frame);File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: "+passed+" motion and combat regression checks\n"+log);Debug.Log("MOTION_QA_OK "+passed);Application.Quit(0);
    }
}
}
