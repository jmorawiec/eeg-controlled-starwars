using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace ForceDemo {
public enum NeuroMode { Simulation, EEG }
[Serializable]public class NeuroScore {public string ability;public float cv,eeg,threshold;}
[Serializable]public class NeuroContact {public string channel,status;}
[Serializable]public class NeuroMessage {
    public string @event,ability,mode,source,profile,gesture,message,training_phase,training_ability,training_condition,training_message,command;
    public bool available,personal_model,training_active,simulated;public float cv_confidence,eeg_score,eeg_threshold,countdown;
    public int trial,total,done_trials,missed;public NeuroScore[] scores;public NeuroContact[] contacts;
}
public class NeuroInput:MonoBehaviour {
    public ForceGame game;public NeuroMode Mode{get;private set;}
    public bool TrainingOpen{get;private set;}public bool TrainingPaused{get;private set;}
    public TrainingScenarios Definitions;public SimulationTraining Practice;
    public NeuroProfiles Profiles;public NeuroProfile Profile=>Profiles.items[Profiles.selected];
    public string PythonPath,HeadsetSerial,Status="Simulation ready",LastAbility="";public NeuroMessage Live;
    public string Gesture="";public bool Imagining;public float SimulatedScore=.1f;
    public bool Connected=>transport!=null&&transport.connected;
    public bool SignalReady=>Mode==NeuroMode.Simulation||(Connected&&Live!=null&&Live.available&&Time.realtimeSinceStartup-lastTelemetry<1.5f);
    public bool PersonalModel=>Mode==NeuroMode.EEG&&Live!=null&&Live.personal_model;
    public string Phase=>Mode==NeuroMode.Simulation?Practice.phase:Live?.training_phase??"ready";
    public int TrialIndex=>Mode==NeuroMode.Simulation?Practice.index:Live?.trial??0;
    public int TrialTotal=>Mode==NeuroMode.Simulation?Practice.plan.Count:Live?.total??32;
    public int Completed=>Mode==NeuroMode.Simulation?Practice.completed:Live?.done_trials??0;
    public int Missed=>Mode==NeuroMode.Simulation?Practice.missed:Live?.missed??0;
    public float Countdown=>Mode==NeuroMode.Simulation?Practice.Countdown(Time.realtimeSinceStartup):Live?.countdown??0;
    public string TargetAbility=>Mode==NeuroMode.Simulation?(Practice.Current==null?"":Definitions.scenarios[Practice.Current.ability].ability):Live?.training_ability??"";
    public bool TargetImagined=>Mode==NeuroMode.Simulation?(Practice.Current!=null&&Practice.Current.imagine):Live?.training_condition=="imagination";
    public string TrainingMessage=>Mode==NeuroMode.Simulation?Practice.message:Live?.training_message??Status;
    public string DataRoot=>Path.Combine(Application.persistentDataPath,"InputProfiles");
    public int Port=5555;NeuroTransport transport;Process worker;float lastTelemetry=-100,lastClock,lastFeedback;bool wasInteractive,wasPaused,cleanP,cleanPD,cleanS,cleanSD;
    Quaternion savedRotation;Color savedAccent;readonly bool[] held=new bool[4];readonly bool[] pending=new bool[4];
    string raw="",episode="",overrideGesture="";bool overrideImagination,simStarted,simFired;float rawSince,episodeSince,lastSeen,belowSince;
    static readonly string[] Abilities={"Push","Heal","Choke","Lightning"};
    public void Initialize(ForceGame owner){
        game=owner;Definitions=JsonUtility.FromJson<TrainingScenarios>(Resources.Load<TextAsset>("Training/Scenarios").text);Practice=new SimulationTraining(Definitions);
        Directory.CreateDirectory(DataRoot);try{Profiles=JsonUtility.FromJson<NeuroProfiles>(File.ReadAllText(Path.Combine(DataRoot,"profiles.json")));}catch{Profiles=new NeuroProfiles();}
        if(Profiles==null||Profiles.items==null)Profiles=new NeuroProfiles();if(Profiles.items.Count==0)NewProfile();Profiles.selected=Mathf.Clamp(Profiles.selected,0,Profiles.items.Count-1);
        PythonPath=PlayerPrefs.GetString("NeuroPython","");if(string.IsNullOrWhiteSpace(PythonPath)||PythonPath=="python3")PythonPath=FindPython();HeadsetSerial=PlayerPrefs.GetString("NeuroSerial","U8-2025.08.21");
        Mode=(NeuroMode)Mathf.Clamp(PlayerPrefs.GetInt("NeuroMode",0),0,1);foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--neuro-port="))int.TryParse(arg.Substring(13),out Port);
        transport=new NeuroTransport();transport.Start(Port);lastClock=Time.realtimeSinceStartup;
    }
    static string FindPython(){
        // Prefer this checkout's isolated runtime when the app is beside the source project.
        var directory=new DirectoryInfo(Application.dataPath);
        string executable=Application.platform==RuntimePlatform.WindowsEditor||Application.platform==RuntimePlatform.WindowsPlayer?"Scripts/python.exe":"bin/python";
        for(int i=0;i<5&&directory!=null;i++,directory=directory.Parent){
            foreach(string folder in new[]{".venv","ForceAwakening/.venv"}){string candidate=Path.Combine(directory.FullName,folder,executable);if(File.Exists(candidate))return candidate;}
        }
        return "python3";
    }
    public void SaveProfiles(){Directory.CreateDirectory(DataRoot);File.WriteAllText(Path.Combine(DataRoot,"profiles.json"),JsonUtility.ToJson(Profiles,true));}
    public void NewProfile(){if(Profiles==null)Profiles=new NeuroProfiles();Profiles.items.Add(new NeuroProfile{id=Guid.NewGuid().ToString("N"),name="Player "+(Profiles.items.Count+1)});Profiles.selected=Profiles.items.Count-1;SaveProfiles();Live=null;ClearInput();if(transport!=null&&Mode==NeuroMode.EEG)Send(new NeuroMessage{command="configure",profile=Profile.id});}
    public void NextProfile(){if(TrainingOpen)return;Profiles.selected=(Profiles.selected+1)%Profiles.items.Count;SaveProfiles();Live=null;ClearInput();if(Mode==NeuroMode.EEG)Send(new NeuroMessage{command="configure",profile=Profile.id});}
    public void SetMode(NeuroMode mode){if(TrainingOpen)return;StopWorker();Mode=mode;Live=null;ClearInput();PlayerPrefs.SetInt("NeuroMode",(int)Mode);PlayerPrefs.Save();Status=mode==NeuroMode.Simulation?"Simulation ready":"Connect the EEG input service.";}
    public void SaveConnection(){PlayerPrefs.SetString("NeuroPython",PythonPath.Trim());PlayerPrefs.SetString("NeuroSerial",HeadsetSerial.Trim());PlayerPrefs.Save();}
    public void SetSimulationInput(string gesture,bool imagination){overrideGesture=gesture;overrideImagination=imagination;}
    public void Tick(){
        if(game.SkipInput){if(cleanP)game.controls.Primary=false;if(cleanPD)game.controls.PrimaryDown=false;if(cleanS)game.controls.Secondary=false;if(cleanSD)game.controls.SecondaryDown=false;}cleanP=cleanPD=cleanS=cleanSD=false;
        float now=Time.realtimeSinceStartup,dt=Mathf.Min(.1f,now-lastClock);lastClock=now;
        if(transport!=null)for(int i=0;i<256&&transport.inbox.TryDequeue(out string line);i++)Receive(line,now);
        bool interactive=game.Playing&&!game.paused&&!TrainingOpen&&(Application.isFocused||game.SkipInput);
        if(interactive!=wasInteractive){ClearInput();wasInteractive=interactive;}
        if(Mode==NeuroMode.Simulation){
            ReadSimulation();SimulatedScore=Mathf.MoveTowards(SimulatedScore,Imagining?.9f:.1f,dt*2.8f);
            if(Application.isFocused||game.SkipInput){Simulate(now);if(TrainingOpen&&!TrainingPaused){Practice.Tick(now,Gesture,Imagining);if(Practice.phase=="complete"&&!Profile.practiceComplete){Profile.practiceComplete=true;SaveProfiles();}}}
        }else if(!SignalReady){ClearInput();if(Connected&&Live!=null)Status="Waiting for a fresh, usable EEG signal.";}
        if(TrainingOpen){
            game.visuals.channel=string.IsNullOrEmpty(Gesture)?0:.7f;game.visuals.blocking=false;
            if(!string.IsNullOrEmpty(Gesture))game.visuals.SetAccent(Gesture=="Lightning"||Gesture=="Choke"?ArenaArt.Red:ArenaArt.Cyan);
            if(game.controls.Back||game.controls.Pause){game.controls.Back=game.controls.Pause=false;CloseTraining();return;}
            if(game.controls.Confirm){if(TrainingPaused)ResumeTraining();else if(Mode==NeuroMode.EEG&&!Connected)ConnectService();else if(Phase=="complete"||Phase=="failed"||Phase=="ready")StartSession();}
        }
        if(Mode==NeuroMode.EEG){game.controls.Primary=game.controls.PrimaryDown=game.controls.Secondary=game.controls.SecondaryDown=false;}
        bool p=game.controls.Primary,pd=game.controls.PrimaryDown,s=game.controls.Secondary,sd=game.controls.SecondaryDown;
        if(interactive){
            if(game.side==ForceSide.Light){game.controls.Primary|=pending[0];game.controls.PrimaryDown|=pending[0];game.controls.Secondary|=held[1];game.controls.SecondaryDown|=pending[1]||(held[1]&&game.secondaryCooldown<=0&&game.health<100);}
            else{game.controls.Primary|=held[3];game.controls.PrimaryDown|=pending[3];game.controls.Secondary|=held[2];game.controls.SecondaryDown|=pending[2];}
        }
        cleanP=!p&&game.controls.Primary;cleanPD=!pd&&game.controls.PrimaryDown;cleanS=!s&&game.controls.Secondary;cleanSD=!sd&&game.controls.SecondaryDown;
        Array.Clear(pending,0,pending.Length);
    }
    void ReadSimulation(){
        Gesture="";Imagining=false;if(game.SkipInput){Gesture=overrideGesture;Imagining=overrideImagination;return;}
        if(!Application.isFocused)return;var k=Keyboard.current;
        if(k!=null){if(k.zKey.isPressed)Gesture="Push";else if(k.xKey.isPressed)Gesture="Heal";else if(k.cKey.isPressed)Gesture="Choke";else if(k.vKey.isPressed)Gesture="Lightning";Imagining=k.leftCtrlKey.isPressed||k.rightCtrlKey.isPressed;}
        var pad=Gamepad.current;if(TrainingOpen&&pad!=null){if(pad.rightShoulder.isPressed)Gesture=TargetAbility;Imagining|=pad.leftShoulder.isPressed;}
    }
    void Simulate(float now){
        if(Gesture!=raw){raw=Gesture;rawSince=now;}
        if(!string.IsNullOrEmpty(raw)&&now-rawSince>=.15f&&raw!=episode){EndSim();episode=raw;episodeSince=rawSince;lastSeen=now;simFired=false;}
        if(raw==episode&&!string.IsNullOrEmpty(episode))lastSeen=now;
        if(string.IsNullOrEmpty(episode))return;
        if(now-lastSeen>.35f){EndSim();if(now-lastSeen>1f){episode="";simFired=false;}return;}
        if(SimulatedScore>=.5f){belowSince=0;float delay=episode=="Push"?1.7f:2.4f;
            if(now-episodeSince>=delay&&!simStarted&&!(episode=="Push"&&simFired)){simStarted=true;simFired=true;Ability(episode,episode=="Push"?"ability_triggered":"ability_started");}}
        else {if(belowSince==0)belowSince=now;if(now-belowSince>=1.5f)EndSim();}
    }
    void EndSim(){if(simStarted&&episode!="Push")Ability(episode,"ability_ended");simStarted=false;}
    void Ability(string ability,string kind){
        int id=Array.IndexOf(Abilities,ability);if(id<0)return;
        if(kind=="ability_ended"){held[id]=false;return;}
        LastAbility=ability;lastFeedback=Time.realtimeSinceStartup;
        if(TrainingOpen){if(game.visuals)game.visuals.channel=1;return;}
        if(!wasInteractive)return;
        if(kind=="ability_started")held[id]=true;pending[id]=true;
    }
    public void Receive(string json,float now){
        NeuroMessage m;try{m=JsonUtility.FromJson<NeuroMessage>(json);}catch{return;}if(m==null)return;
        if(m.@event=="bridge_connected"){ClearInput();Send(new NeuroMessage{command="configure",profile=Profile.id});return;}
        if(m.@event=="bridge_disconnected"||m.@event=="bridge_overflow"){ClearInput();Live=null;Status="Input service disconnected.";return;}
        if(Mode!=NeuroMode.EEG||m.profile!=Profile.id)return;
        if(m.@event=="telemetry"){
            Live=m;lastTelemetry=now;Gesture=m.gesture??"";Imagining=m.eeg_score>=m.eeg_threshold;
            Status=m.message??"EEG connected";if(!m.available)ClearInput();return;
        }
        if(m.@event=="error"){Status=m.message??"Input service error.";ClearInput();return;}
        if(m.@event=="ability_ended"){Ability(m.ability,m.@event);return;}
        if(m.mode=="eeg"&&SignalReady&&PersonalModel&&(m.@event=="ability_started"||m.@event=="ability_triggered"))Ability(m.ability,m.@event);
    }
    public void ResetControls(){ClearInput();wasInteractive=false;LastAbility="";}
    void ClearInput(){Array.Clear(held,0,held.Length);Array.Clear(pending,0,pending.Length);raw=episode="";simStarted=simFired=false;belowSince=0;}
    public bool IsActive(string ability){int i=Array.IndexOf(Abilities,ability);return i>=0&&(held[i]||(LastAbility==ability&&Time.realtimeSinceStartup-lastFeedback<.3f));}
    public void OpenTraining(){
        if(TrainingOpen)return;TrainingOpen=true;TrainingPaused=false;wasPaused=game.paused;game.paused=false;Time.timeScale=1;
        savedRotation=game.player.rotation;savedAccent=game.visuals.accent;game.player.rotation=Quaternion.identity;game.visuals.ResetMotion();ClearInput();
        if(Mode==NeuroMode.Simulation)Practice.Start(Time.realtimeSinceStartup,Environment.TickCount);
        else if(SignalReady)StartSession();else Status="Connect the input service and check electrode contact.";
    }
    public void StartSession(){if(Mode==NeuroMode.Simulation)Practice.Start(Time.realtimeSinceStartup,Environment.TickCount);else if(SignalReady)Send(new NeuroMessage{command="start_training",profile=Profile.id});}
    public void CloseTraining(){
        if(!TrainingOpen)return;if(Mode==NeuroMode.EEG)Send(new NeuroMessage{command="cancel_training",profile=Profile.id});
        TrainingOpen=TrainingPaused=false;game.player.rotation=savedRotation;game.visuals.ResetMotion();game.visuals.SetAccent(savedAccent);game.paused=wasPaused;Time.timeScale=wasPaused?0:1;ClearInput();game.orbit.ResetView();game.GetComponent<ForceHUD>().AfterTraining();
    }
    public void FocusChanged(bool focused){if(!focused){ClearInput();if(TrainingOpen){TrainingPaused=true;Send(new NeuroMessage{command="pause_training",profile=Profile.id});}}}
    public void ResumeTraining(){TrainingPaused=false;Practice.Resume(Time.realtimeSinceStartup);Send(new NeuroMessage{command="resume_training",profile=Profile.id});}
    void Send(NeuroMessage command){transport?.Send(JsonUtility.ToJson(command));}
    public void ConnectService(){
        if(Mode!=NeuroMode.EEG)return;SaveConnection();
        if(Connected){Send(new NeuroMessage{command="configure",profile=Profile.id});Status="EEG input service connected.";return;}
        if(worker!=null&&!worker.HasExited){Status="Input service is starting…";return;}
        string bridge=Path.Combine(Application.streamingAssetsPath,"NeuroBridge","unity_bridge.py");
        if(!File.Exists(bridge)){Status="Input bridge missing. Run NeuroBridgeBuild.Stage in Unity.";return;}
        try{
            var start=new ProcessStartInfo{FileName=PythonPath.Trim(),UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(bridge)};
            start.Arguments=Quote(bridge)+" --upstream "+Quote(Path.Combine(Application.streamingAssetsPath,"NeuroBridge","upstream"))+" --data-root "+Quote(DataRoot)+" --profile "+Profile.id+" --port "+Port+" --serial "+Quote(HeadsetSerial.Trim());
            worker=new Process{StartInfo=start};string log=Path.Combine(DataRoot,"bridge.log");object logSync=new object();
            DataReceivedEventHandler logger=(s,e)=>{if(e.Data!=null)lock(logSync){try{File.AppendAllText(log,e.Data+"\n");}catch(IOException){}}};
            worker.OutputDataReceived+=logger;worker.ErrorDataReceived+=logger;worker.Start();worker.BeginOutputReadLine();worker.BeginErrorReadLine();Status="Starting camera and EEG…";
        }catch(Exception){worker?.Dispose();worker=null;Status="Cannot start Python. Set its interpreter path and install the EEG requirements.";}
    }
    public string ServiceHint=>worker!=null&&worker.HasExited&&!Connected?"Input service stopped. See InputProfiles/bridge.log; check Python dependencies and device access.":Status;
    static string Quote(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    public void StopWorker(){if(worker==null)return;Send(new NeuroMessage{command="shutdown",profile=Profiles?.items.Count>0?Profile.id:""});try{if(!worker.HasExited&&!worker.WaitForExit(1500))worker.Kill();}catch(InvalidOperationException){}worker.Dispose();worker=null;}
    void OnDestroy(){StopWorker();transport?.Dispose();}
}
}
