using UnityEngine;

namespace ForceDemo {
// Mentor lines for each side's two powers, in two versions:
//  Prompt - spoken once when the tutorial step for that power begins.
//  Hint   - spoken when the player holds the right gesture but the imagination
//           input (EEG score, or Ctrl in simulation) stays below threshold.
public class SenseiDialogue {
    struct Line {public string ability,prompt,hint;}
    static readonly Line[] Light={
        new Line{ability="Push",
            prompt="This place is surrounded by the Dark Side. Let go of your anger, let go of your fear, and push it away with your hand!",
            hint="Avoiding a challenge will never bring success, my Padawan. Moving your hand is not enough. You must feel the motion. Picture your enemies being thrown back."},
        new Line{ability="Heal",
            prompt="You are bleeding, Padawan? Place your hand on your chest and feel the Force fill you.",
            hint="Picture the circles of the Force surrounding you, Padawan."},
    };
    static readonly Line[] Dark={
        new Line{ability="Lightning",
            prompt="This wretched lackey stands in the way of your power!!! Strike him with lightning!",
            hint="Too weak, my apprentice! You must feel the anger, the hatred! It gives you strength!"},
        new Line{ability="Choke",
            prompt="Someone fears your strength? Seize him and lift him into the air!",
            hint="Weak! Imagine both your hands crushing your enemy at once, with all your strength! Show him how much you despise him!"},
    };
    const float PromptSeconds=6,HintSeconds=5,HintAfter=1.5f,HintCooldown=8;

    public string Speaker="",Text="";public float Time;
    int promptedStep=-1;string heldGesture="";float heldFor;readonly float[] hintCooldown=new float[2];

    public void Reset(){promptedStep=-1;heldGesture="";heldFor=0;hintCooldown[0]=hintCooldown[1]=0;Time=0;Text="";}

    public void Tick(ForceGame game,float dt){
        Time=Mathf.Max(0,Time-dt);hintCooldown[0]=Mathf.Max(0,hintCooldown[0]-dt);hintCooldown[1]=Mathf.Max(0,hintCooldown[1]-dt);
        if(!game.Playing)return;
        var lines=game.side==ForceSide.Light?Light:Dark;
        Speaker=game.side==ForceSide.Light?"Master Auren":"Darth Veyr";
        if(game.stage==DemoStage.Tutorial&&game.tutorialStep<2&&game.tutorialStep!=promptedStep){
            promptedStep=game.tutorialStep;Say(lines[promptedStep].prompt,PromptSeconds);
        }
        var n=game.neuro;string gesture=n?n.Gesture:"";
        int id=System.Array.FindIndex(lines,l=>l.ability==gesture);
        if(id<0||n.Imagining||n.IsActive(gesture)){heldGesture="";heldFor=0;return;}
        if(gesture!=heldGesture){heldGesture=gesture;heldFor=0;}
        heldFor+=dt;
        if(heldFor>=HintAfter&&hintCooldown[id]<=0){Say(lines[id].hint,HintSeconds);hintCooldown[id]=HintCooldown;heldFor=0;}
    }
    void Say(string text,float seconds){Text=text;Time=seconds;}
}
}
