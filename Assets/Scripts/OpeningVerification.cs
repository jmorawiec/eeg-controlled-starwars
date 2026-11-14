using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace ForceDemo {
public class OpeningVerification:MonoBehaviour {
    ForceGame g;string output;int checks,frame;bool recording;float next;StringBuilder times=new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--verify-opening")<0)return;
        var q=new GameObject("Opening verification").AddComponent<OpeningVerification>();
        q.output=Path.Combine(Application.persistentDataPath,"OpeningReview");foreach(var a in args)if(a.StartsWith("--qa-output="))q.output=a.Substring(12);
        Directory.CreateDirectory(q.output);Directory.CreateDirectory(q.output+"/frames");q.StartCoroutine(q.Guarded(q.Run()));
    }
    void Update(){if(recording&&Time.unscaledTime>=next){next=Time.unscaledTime+1/15f;times.AppendLine(Time.unscaledTime.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));ScreenCapture.CaptureScreenshot(output+"/frames/"+frame++.ToString("D4")+".png");}}
    void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Debug.Log("OPENING PASS: "+text);}
    IEnumerator Guarded(IEnumerator r){while(true){object current;try{if(!r.MoveNext())break;current=r.Current;}catch(Exception e){File.WriteAllText(output+"/result.txt","FAIL: "+e);Debug.LogException(e);Application.Quit(1);yield break;}yield return current;}}
    IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(output+"/"+name+".png");yield return null;}
    IEnumerator Run(){
        yield return null;yield return null;g=FindFirstObjectByType<ForceGame>();g.SkipInput=true;g.controls.UsingPad=false;
        yield return new WaitForSeconds(1);g.Begin(ForceSide.Light);recording=true;next=Time.unscaledTime;
        Check(g.stage==DemoStage.Opening&&!g.Playing,"Side selection starts a protected opening sequence");
        Vector3 player=g.player.position,camera=g.view.transform.position,enemy=g.enemies[0].Position;
        g.controls.Move=Vector2.up;g.controls.Attack=g.controls.Primary=g.controls.PrimaryDown=g.controls.Confirm=true;
        yield return new WaitForSeconds(.35f);g.controls.Confirm=false;
        Check(g.stage==DemoStage.Opening&&!g.opening.CanSkip,"Selection input cannot immediately skip the opening");
        yield return new WaitForSeconds(1);
        Check(Vector3.Distance(player,g.player.position)<.001f&&g.attackTimer==0&&g.energy==100&&g.playTime==0,"Movement, combat, resources and gameplay timer stay frozen");
        Check(Vector3.Distance(enemy,g.enemies[0].Position)<.001f,"Enemies stay frozen during the opening");
        Check(Vector3.Distance(camera,g.view.transform.position)>.05f&&!g.orbit.atmosphere.menu,"Opening camera moves with clear cinematic rendering");
        g.controls.Move=Vector2.zero;g.controls.Attack=g.controls.Primary=g.controls.PrimaryDown=false;
        yield return Shot("01-valley");
        while(g.opening.Elapsed<5.6f)yield return null;
        Check(g.opening.Subtitle.Contains("Auren")&&g.visuals.BladeExtension==0,"Light opening uses Auren's dialogue and delays blade reveal");yield return Shot("02-master");
        while(g.opening.Elapsed<9.8f)yield return null;
        Check(g.visuals.BladeExtension==1,"Hero's blade is revealed in the portrait shot");yield return Shot("03-hero");
        while(g.opening.Elapsed<13.93f)yield return null;
        Vector3 lastPosition=g.view.transform.position;Quaternion lastRotation=g.view.transform.rotation;
        while(g.stage==DemoStage.Opening)yield return null;
        Check(g.stage==DemoStage.Tutorial&&!g.opening.Active&&g.visuals.weapon.gameObject.activeSelf,"Opening finishes automatically and restores tutorial and weapon");
        Check(Vector3.Distance(lastPosition,g.view.transform.position)<.08f&&Quaternion.Angle(lastRotation,g.view.transform.rotation)<1,"Final shot joins the gameplay camera without a jump");
        yield return new WaitForSeconds(.6f);yield return Shot("04-tutorial");recording=false;File.WriteAllText(output+"/times.txt",times.ToString());
        g.Begin(ForceSide.Dark);yield return new WaitForSeconds(.9f);g.opening.Tick(4.5f);g.opening.RenderCamera();
        Check(g.opening.Subtitle.Contains("Veyr"),"Dark opening has its own mentor dialogue");yield return Shot("05-dark-master");
        var keyboard=InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter));InputSystem.Update();g.controls.Read();g.opening.Tick(.01f);InputSystem.RemoveDevice(keyboard);g.controls.Confirm=false;
        Check(g.stage==DemoStage.Tutorial&&!g.paused,"Enter skips to tutorial without pausing");
        g.Begin(ForceSide.Dark);g.opening.Tick(1);
        var pad=InputSystem.AddDevice<Gamepad>();InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));InputSystem.Update();g.controls.Read();g.opening.Tick(.01f);InputSystem.RemoveDevice(pad);g.controls.Confirm=false;
        Check(g.stage==DemoStage.Tutorial&&!g.opening.Active,"Controller confirm skips the opening");
        g.Begin(ForceSide.Dark);g.opening.Tick(1);g.controls.Pause=true;g.opening.Tick(.01f);g.controls.Pause=false;
        Check(g.stage==DemoStage.Tutorial&&!g.paused,"Pause/back action skips rather than leaving a paused cinematic");
        g.Begin(ForceSide.Light);g.ReturnToMenu();
        Check(g.stage==DemoStage.Choice&&!g.opening.Active&&g.visuals.weapon.gameObject.activeSelf,"Returning to menu cancels timeline and restores the weapon");
        g.Begin(ForceSide.Dark);g.Begin(ForceSide.Light,false);
        Check(g.stage==DemoStage.Tutorial&&!g.opening.Active&&g.visuals.weapon.gameObject.activeSelf,"Restart replaces the opening without stale actors or hidden weapons");
        g.controls.Attack=true;yield return new WaitForSeconds(.5f);g.controls.Attack=false;
        Check(g.basicPractice>0,"Combat input works after leaving the opening");
        File.WriteAllText(output+"/result.txt","PASS: "+checks+" opening checks. Both paths, camera continuity, protected simulation, automatic completion, keyboard/controller skip and cleanup. Controller input simulated.");Application.Quit(0);
    }
}
}
