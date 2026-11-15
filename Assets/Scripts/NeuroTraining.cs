using System;
using System.Collections.Generic;
using UnityEngine;
namespace ForceDemo {
[Serializable]public class TrainingScenario {public string ability,spell,trigger,gesture;public float minimumSeconds;}
[Serializable]public class TrainingScenarios {public string source;public int repeats;public float relaxSeconds,timeoutSeconds;public TrainingScenario[] scenarios;}
[Serializable]public class NeuroProfile {public string id,name;public bool practiceComplete;}
[Serializable]public class NeuroProfiles {public List<NeuroProfile> items=new List<NeuroProfile>();public int selected;}
// Simulation adapts the upstream Calibration protocol, including negative trials and release.
// It never records fake EEG or marks a personal model as trained.
public class SimulationTraining {
    public class Trial {public int ability;public bool imagine;}
    public readonly List<Trial> plan=new List<Trial>();
    public string phase="ready",message="";public int index,completed,missed;public float since;
    string previous="";float onset,lastSeen;bool tracking;readonly TrainingScenarios definitions;
    public SimulationTraining(TrainingScenarios definitions){this.definitions=definitions;}
    public Trial Current=>index<plan.Count?plan[index]:null;
    public bool Running=>phase=="rest"||phase=="act";
    public void Start(float now,int seed){
        plan.Clear();for(int a=0;a<definitions.scenarios.Length;a++)for(int r=0;r<definitions.repeats;r++){plan.Add(new Trial{ability=a,imagine=true});plan.Add(new Trial{ability=a,imagine=false});}
        var random=new System.Random(seed);for(int i=plan.Count-1;i>0;i--){int j=random.Next(i+1);var t=plan[i];plan[i]=plan[j];plan[j]=t;}
        phase="rest";index=completed=missed=0;since=now;tracking=false;previous="";message="";
    }
    public void Tick(float now,string gesture,bool imagined){
        if(!Running)return;
        if(phase=="rest") {if(now-since>=definitions.relaxSeconds){phase="act";since=now;previous="";tracking=false;}return;}
        string expected=definitions.scenarios[Current.ability].ability;
        if(!string.IsNullOrEmpty(gesture)){
            if(gesture!=previous){onset=now;previous=gesture;tracking=true;conditionMatched=imagined==Current.imagine;}
            lastSeen=now;
            if(gesture!=expected)message="Detected "+gesture+". Use "+expected+".";
            else if(imagined!=Current.imagine)message=Current.imagine?"Add imagination to the gesture.":"Release the imagination input; keep the gesture.";
            else message="Hold, then release both inputs.";
        }else if(tracking&&now-lastSeen>=1.35f){
            float length=lastSeen-onset;tracking=false;
            if(previous==expected&&onset>=since&&length>=definitions.scenarios[Current.ability].minimumSeconds&&conditionMatched){completed++;Advance(now,"Trial recorded.");}
            else message="Try again. Match the prompt, hold, then release.";
            previous="";
        }
        if(tracking&&gesture==expected)conditionMatched&=imagined==Current.imagine;
        if(!tracking&&now-since>=definitions.timeoutSeconds){missed++;Advance(now,"No matching gesture. Trial skipped.");}
    }
    bool conditionMatched;
    void Advance(float now,string text){index++;since=now;tracking=false;conditionMatched=false;message=text;phase=index>=plan.Count?"complete":"rest";}
    public float Countdown(float now)=>phase=="rest"?Mathf.Max(0,definitions.relaxSeconds-(now-since)):Mathf.Max(0,definitions.timeoutSeconds-(now-since));
    public void Resume(float now){if(Running){since=now;phase="rest";tracking=false;previous="";}}
}
}
