using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
namespace ForceDemo {
public class NeuroVerification:MonoBehaviour {
    ForceGame g;NeuroInput n;string output,profiles;NeuroMode originalMode;int checks;TcpClient client;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--verify-neuro")<0)return;
        var q=new GameObject("Neuro verification").AddComponent<NeuroVerification>();q.output=Path.Combine(Application.persistentDataPath,"NeuroVerification");foreach(var arg in args)if(arg.StartsWith("--qa-output="))q.output=arg.Substring(12);
        Directory.CreateDirectory(q.output);q.StartCoroutine(q.Guarded(q.Run()));
    }
    void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Debug.Log("NEURO PASS: "+text);}
    IEnumerator Guarded(IEnumerator r){while(true){object value;try{if(!r.MoveNext())break;value=r.Current;}catch(Exception e){Cleanup();File.WriteAllText(output+"/result.txt","FAIL: "+e);Debug.LogException(e);Application.Quit(1);yield break;}yield return value;}}
    void Cleanup(){client?.Dispose();if(n){if(n.TrainingOpen)n.CloseTraining();n.SetMode(originalMode);if(profiles!=null){n.Profiles=JsonUtility.FromJson<NeuroProfiles>(profiles);n.SaveProfiles();}}}
    void Send(NeuroMessage m){m.profile=n.Profile.id;var bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(m)+"\n");client.GetStream().Write(bytes,0,bytes.Length);}
    NeuroMessage Snapshot(bool available=true,bool personal=true)=>new NeuroMessage{@event="telemetry",source="eeg",profile=n.Profile.id,available=available,personal_model=personal,gesture="Push",eeg_score=.85f,eeg_threshold=.5f,training_phase="ready",message="Synthetic EEG transport fixture",scores=new[]{new NeuroScore{ability="Push",cv=.95f,eeg=.85f,threshold=.5f}}};
    IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(output+"/"+name+".png");yield return null;}
    IEnumerator Run(){
        yield return null;yield return null;g=FindAnyObjectByType<ForceGame>();g.SkipInput=true;g.muted=true;n=g.neuro;originalMode=n.Mode;profiles=JsonUtility.ToJson(n.Profiles);n.NewProfile();n.SetMode(NeuroMode.Simulation);n.SetSimulationInput("",false);
        Check(n.Definitions.scenarios.Length==4&&n.Definitions.repeats==4,"Four upstream spell scenarios with four positive and negative repeats");
        var training=new SimulationTraining(n.Definitions);training.Start(0,42);
        Check(training.plan.Count==32&&Enumerable.Range(0,4).All(a=>training.plan.Count(t=>t.ability==a&&t.imagine)==4&&training.plan.Count(t=>t.ability==a&&!t.imagine)==4),"Balanced 32-trial plan covers every spell and both conditions");
        float clock=0;while(training.Running){clock+=5.1f;training.Tick(clock,"",false);var trial=training.Current;string gesture=n.Definitions.scenarios[trial.ability].ability;for(int i=0;i<25;i++){clock+=.1f;training.Tick(clock,gesture,trial.imagine);}clock+=1.5f;training.Tick(clock,"",false);}
        Check(training.phase=="complete"&&training.completed==32&&training.missed==0,"Full simulated training requires matched gestures, conditions and release");
        var negative=new SimulationTraining(n.Definitions);negative.Start(0,1);negative.Tick(5.1f,"",false);var first=negative.Current;string expected=n.Definitions.scenarios[first.ability].ability;
        for(int i=0;i<25;i++)negative.Tick(5.2f+i*.1f,expected,!first.imagine);negative.Tick(9,"",false);
        Check(negative.index==0,"Wrong imagination condition cannot complete a practice trial");
        negative.Tick(30,"",false);Check(negative.missed==1,"Missing gestures time out and are reported as skipped");
        var hud=g.GetComponent<ForceHUD>();hud.SetPage(MenuPage.Settings);yield return Shot("01-settings");
        n.OpenTraining();float elapsed=g.playTime;Vector3 player=g.player.position;g.controls.Move=Vector2.up;g.controls.Attack=true;yield return new WaitForSeconds(.4f);
        Check(g.playTime==elapsed&&Vector3.Distance(g.player.position,player)<.001f&&g.basicPractice==0,"Training freezes combat, movement and run timer");
        yield return Shot("02-practice-rest");g.controls.Move=Vector2.zero;g.controls.Attack=false;
        n.Practice.since=Time.realtimeSinceStartup-6;yield return null;n.SetSimulationInput(n.TargetAbility,n.TargetImagined);yield return new WaitForSeconds(.3f);yield return Shot("03-practice-input");
        n.FocusChanged(false);Check(n.TrainingPaused,"Focus loss pauses the training session");n.ResumeTraining();Check(!n.TrainingPaused&&n.Practice.phase=="rest","Resume restarts the current trial from rest");
        n.SetSimulationInput("",false);g.controls.Back=true;yield return null;yield return null;Check(!n.TrainingOpen&&hud.page==MenuPage.Settings,"Escape returns from training to Settings without closing the menu");
        n.OpenTraining();n.Practice.plan.Clear();n.Practice.plan.AddRange(training.plan);n.Practice.index=32;n.Practice.completed=32;n.Practice.phase="complete";yield return Shot("04-practice-complete");n.CloseTraining();
        g.Begin(ForceSide.Light,false);g.enemies[0].body.position=g.player.position+Vector3.forward*4;g.enemies[0].hp=500;
        n.SetSimulationInput("Push",false);yield return new WaitForSeconds(2.8f);Check(g.primaryPractice==0&&g.energy==100,"Gesture without imagined input cannot cast in simulation");
        n.SetSimulationInput("Push",true);yield return new WaitForSeconds(.6f);Check(g.primaryPractice>0,"Gesture plus imagined input casts Force Push");float practice=g.primaryPractice;
        yield return new WaitForSeconds(1.6f);Check(g.primaryPractice==practice,"A held Push fires only once per gesture");
        n.SetSimulationInput("",false);yield return new WaitForSeconds(1.5f);
        g.Begin(ForceSide.Dark,false);g.enemies[0].body.position=g.player.position+Vector3.forward*4;g.enemies[0].hp=1000;n.SetSimulationInput("Lightning",true);
        yield return new WaitForSeconds(2.9f);Check(g.energy<100&&g.primaryPractice>0,"Sustained simulated Lightning channels through normal combat rules");n.SetSimulationInput("",false);yield return new WaitForSeconds(.5f);float released=g.energy;yield return new WaitForSeconds(.4f);Check(g.energy>released,"Releasing a gesture ends the simulated channel");
        n.SetMode(NeuroMode.EEG);g.Begin(ForceSide.Light,false);yield return null;client=new TcpClient();client.Connect("127.0.0.1",n.Port);yield return new WaitForSeconds(.15f);Check(n.Connected,"TCP bridge accepts a localhost client");
        Send(Snapshot());yield return new WaitForSeconds(.15f);Check(n.SignalReady&&n.PersonalModel,"Live telemetry includes signal quality and personal model status");
        string json=JsonUtility.ToJson(new NeuroMessage{@event="ability_triggered",profile=n.Profile.id,ability="Push",mode="eeg"})+"\n";var data=Encoding.UTF8.GetBytes(json);client.GetStream().Write(data,0,data.Length/2);yield return null;client.GetStream().Write(data,data.Length/2,data.Length-data.Length/2);yield return new WaitForSeconds(.15f);
        Check(g.primaryCooldown>0,"Fragmented newline JSON produces a real Unity ability cast");
        g.primaryCooldown=0;g.energy=100;Send(new NeuroMessage{@event="ability_triggered",ability="Push",mode="cv"});yield return new WaitForSeconds(.1f);Check(g.primaryCooldown==0,"EEG mode rejects camera-only fallback events");
        Send(Snapshot(true,false));Send(new NeuroMessage{@event="ability_triggered",ability="Push",mode="eeg"});yield return new WaitForSeconds(.1f);Check(g.primaryCooldown==0,"An untrained baseline model cannot activate gameplay powers");
        Send(Snapshot());Send(new NeuroMessage{@event="gesture_started",ability="Push",mode="eeg"});yield return new WaitForSeconds(.1f);Check(g.primaryCooldown==0,"Raw camera gesture events do not bypass EEG gating");
        g.Begin(ForceSide.Dark,false);yield return null;Send(Snapshot());Send(new NeuroMessage{@event="ability_started",ability="Lightning",mode="eeg"});yield return new WaitForSeconds(.3f);Check(g.energy<100,"EEG sustained-start event controls a real channeled power");
        Send(Snapshot(false));yield return new WaitForSeconds(.15f);Check(!n.SignalReady,"Bad electrode contact immediately blocks EEG control");released=g.energy;yield return new WaitForSeconds(.3f);Check(!g.controls.Primary&&!g.controls.Secondary&&g.energy>=released,"Contact loss releases active powers");
        Send(Snapshot());Send(new NeuroMessage{@event="ability_started",ability="Lightning",mode="eeg"});yield return new WaitForSeconds(.3f);client.Dispose();client=null;yield return new WaitForSeconds(.2f);Check(!n.Connected&&!n.SignalReady,"Disconnect clears held channels and signal availability");
        client=new TcpClient();client.Connect("127.0.0.1",n.Port);yield return new WaitForSeconds(.15f);Send(Snapshot());Send(new NeuroMessage{@event="ability_started",ability="Lightning",mode="eeg"});yield return new WaitForSeconds(1.9f);Check(!n.SignalReady&&!g.controls.Primary,"Stale telemetry releases held input even while TCP remains open");client.Dispose();client=null;yield return new WaitForSeconds(.1f);
        n.OpenTraining();yield return Shot("05-eeg-disconnected");n.CloseTraining();
        Check(!File.Exists(Path.Combine(n.DataRoot,n.Profile.id,"model.joblib")),"Simulation never creates a trained personal EEG model");
        Cleanup();File.WriteAllText(output+"/result.txt","PASS: "+checks+" neuro integration checks. Balanced scenarios, no-gesture/negative examples, simulated casting, training isolation, pause/resume, fragmented TCP JSON, strict EEG gating, untrained profiles, contact loss and disconnect. EEG and controller inputs simulated; physical headset/camera not tested.");Application.Quit(0);
    }
}
}
