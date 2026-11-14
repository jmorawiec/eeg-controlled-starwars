using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.DualShock.LowLevel;
using UnityEngine.InputSystem.Utilities;
namespace ForceDemo {
public class ForceVerification:MonoBehaviour {
    string output;ForceGame g;int passed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfRequested(){
        var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--verify-demo")<0)return;
        var q=new GameObject("Demo integration verification").AddComponent<ForceVerification>();
        foreach(var a in args)if(a.StartsWith("--qa-output="))q.output=a.Substring("--qa-output=".Length);
        if(string.IsNullOrEmpty(q.output))q.output=Path.Combine(Application.persistentDataPath,"Verification");Directory.CreateDirectory(q.output);q.StartCoroutine(q.Guarded(q.Run()));
    }
    IEnumerator Guarded(IEnumerator routine){
        while(true){object current;try{if(!routine.MoveNext())break;current=routine.Current;}catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL: "+e);Application.Quit(1);yield break;}yield return current;}
    }
    void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;Debug.Log("QA PASS: "+message);}
    void Capture(string file){ScreenCapture.CaptureScreenshot(Path.Combine(output,file));}
    IEnumerator Run(){
        yield return null;yield return null;g=FindFirstObjectByType<ForceGame>();g.SkipInput=true;g.muted=true;var hud=g.GetComponent<ForceHUD>();
        Check(!g.view.orthographic && g.view.fieldOfView>40,"Third-person perspective camera");
        Check(g.visuals.animator && g.visuals.animator.isHuman,"Imported humanoid character with valid rig");
        Check(Resources.Load<GameObject>("Generated/coastal_cliff_01")&&Resources.Load<GameObject>("Generated/fern_02"),"Photogrammetry environment assets included");
        Check(g.WeaponName=="Glowing Stick" && g.visuals.weapon.name==g.WeaponName,"Named weapon is attached to character");
        yield return new WaitForSeconds(1);yield return new WaitForEndOfFrame();Capture("01-menu.png");Vector3 menuCamera=g.view.transform.position;
        yield return new WaitForSeconds(.6f);Check(Vector3.Distance(menuCamera,g.view.transform.position)>.01f && g.orbit.atmosphere.menu,"Animated menu background is softly blurred");
        hud.selection=0;hud.Activate();Check(hud.page==MenuPage.Side,"Main menu opens side selection");
        yield return new WaitForEndOfFrame();Capture("02-side-selection.png");
        hud.SetPage(MenuPage.Home);hud.selection=2;hud.Activate();Check(hud.page==MenuPage.Settings,"Settings accessible from menu");float sensitivity=g.orbit.sensitivity;hud.selection=1;hud.Adjust(1);Check(g.orbit.sensitivity!=sensitivity,"Camera setting can be adjusted");g.orbit.sensitivity=sensitivity;PlayerPrefs.SetFloat("CameraSensitivity",sensitivity);
        hud.SetPage(MenuPage.Home);g.Begin(ForceSide.Light,false);g.player.rotation=Quaternion.identity;g.facing=Vector3.forward;
        g.enemies[0].body.position=g.player.position+Vector3.forward*4;Enemy e=g.enemies[0];float hp=e.hp;
        Check(g.Push()&&e.hp<hp&&e.velocity.magnitude>0,"Force Push damages and displaces a target");Check(!g.Push(),"Force Push respects cooldown");
        hp=g.health;Check(g.Heal()&&g.health>hp,"Force Heal restores health");Check(!g.Heal(),"Force Heal respects cooldown");
        yield return null;Check(g.tutorialStep==2,"Light tutorial recognizes both powers");
        g.AdvanceTutorial();Check(g.stage==DemoStage.Waves&&g.wave==1&&g.enemies.Count==4,"Tutorial advances into first wave");
        foreach(var enemy in g.enemies)g.Hit(enemy,10000);yield return new WaitForSeconds(2.2f);
        Check(g.wave==2&&g.enemies.Count==5,"First wave advances to second wave");
        foreach(var enemy in g.enemies)g.Hit(enemy,10000);yield return new WaitForSeconds(2.2f);Check(g.stage==DemoStage.BossIntro,"Second wave advances to boss introduction");
        g.StartBoss();Check(g.enemies.Count==1&&g.enemies[0].boss,"Boss encounter starts");
        var boss=g.enemies[0];boss.attack=99;boss.telegraph=.1f;boss.pattern=0;boss.attackPoint=g.player.position;hp=g.health;
        yield return new WaitForSeconds(.2f);Check(g.health<hp,"Boss area attack deals damage");
        g.dodgeTime=.4f;hp=g.health;g.DamagePlayer(20);Check(g.health==hp,"Roll invulnerability window prevents damage");g.dodgeTime=0;
        g.Hit(boss,10000);yield return null;Check(g.stage==DemoStage.Victory,"Boss defeat reaches victory");g.ReturnToMenu();Check(g.enemies.Count==0&&g.bolts.Count==0,"Menu cleans up combat");
        g.Begin(ForceSide.Dark,false);g.player.rotation=Quaternion.identity;g.facing=Vector3.forward;g.enemies[0].body.position=g.player.position+Vector3.forward*4;
        e=g.enemies[0];hp=e.hp;Check(g.Lightning(.8f)&&e.hp<hp,"Force Lightning deals channelled damage");hp=e.hp;Check(g.Choke(.8f)&&e.hp<hp&&e.lift>0,"Force Choke lifts and damages target");
        g.energy=0;hp=e.hp;Check(!g.Lightning(.2f)&&!g.Choke(.2f)&&e.hp==hp,"Empty energy blocks dark powers");yield return null;Check(g.tutorialStep==2,"Dark tutorial recognizes both powers");
        g.Begin(ForceSide.Light,false);g.energy=0;Check(!g.Push()&&!g.Heal(),"Empty energy blocks light powers");
        e=g.enemies[0];e.body.position=g.player.position+Vector3.forward*1.9f;g.player.rotation=Quaternion.identity;hp=e.hp;
        Check(g.BasicAttack(),"Basic weapon attack works with zero Force energy");yield return new WaitForSeconds(.38f);Check(e.hp<hp,"Weapon swing applies damage during its hit window");
        yield return new WaitForSeconds(.50f);g.stage=DemoStage.Waves;g.health=g.stamina=100;g.controls.Block=true;hp=g.health;
        g.DamagePlayer(20,g.player.position+g.player.forward*3);Check(g.health==hp&&g.stamina<100,"Frontal block absorbs hit and consumes stamina");
        g.DamagePlayer(20,g.player.position-g.player.forward*3);Check(g.health==hp-20,"Block does not protect the back");hp=g.health;
        g.DamagePlayer(20,g.player.position+g.player.forward*3,true);Check(g.health==hp-20,"Unblockable attack bypasses guard");
        g.stamina=3;g.DamagePlayer(20,g.player.position+g.player.forward*3);Check(g.guardBroken>0&&g.stamina==0,"Insufficient stamina breaks guard");g.controls.Block=false;g.guardBroken=0;
        Check(!g.BasicAttack()&&!g.Roll(Vector3.right),"Stamina gates attacks and rolls independently of Force");
        g.stamina=100;Vector3 before=g.player.position;Check(g.Roll(Vector3.right)&&g.stamina==75,"Roll consumes stamina");yield return new WaitForSeconds(.25f);Check(Vector3.Distance(before,g.player.position)>.4f,"Roll moves the character progressively");
        yield return new WaitForSeconds(.65f);Check(g.dodgeTime==0&&g.visuals.roll==0,"Roll animation returns to locomotion");
        g.Begin(ForceSide.Dark,false);g.enemies[0].body.position=g.player.position+Vector3.forward*5;g.ToggleLock();Check(g.lockedTarget!=null,"Lock-on selects a visible target");g.ToggleLock();Check(g.lockedTarget==null,"Lock-on toggles off");
        g.stage=DemoStage.Waves;g.health=1;g.DamagePlayer(10);yield return null;Check(g.stage==DemoStage.Defeat,"Zero health reaches defeat");
        g.Begin(ForceSide.Dark,false);Check(g.health==100&&g.energy==100&&g.stamina==100&&g.guardBroken==0,"Restart resets combat resources");
        var pad=InputSystem.AddDevice<Gamepad>();try{
            InputSystem.QueueStateEvent(pad,new GamepadState{leftStick=new Vector2(.5f,.8f),rightStick=Vector2.right,rightTrigger=1,leftTrigger=1}.WithButton(GamepadButton.RightShoulder).WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.East).WithButton(GamepadButton.North));InputSystem.Update();var input=new ForceInput();input.Read();
            Check(input.UsingPad&&input.Attack&&input.Block&&input.Dodge&&input.LockOn,"Gamepad attack, block, circle roll and lock-on");Check(input.Primary&&input.Secondary&&input.Move.y>.5f&&input.Aim.x>.5f,"Gamepad Force triggers, movement and camera");
        }finally{InputSystem.RemoveDevice(pad);}
        var ds=InputSystem.AddDevice<DualSenseGamepadHID>();try{
            InputSystem.QueueStateEvent(ds,new DualSenseHIDInputReport{leftStickX=128,leftStickY=0,rightStickX=255,rightStickY=128,leftTrigger=255,rightTrigger=255,buttons0=200,buttons1=3});InputSystem.Update();var input=new ForceInput();input.Read();
            Check(input.PlayStation&&input.Attack&&input.Block&&input.Dodge&&input.LockOn&&input.Primary&&input.Secondary,"DualSense HID mapping: R1 L1 circle triangle R2 L2");
        }finally{InputSystem.RemoveDevice(ds);}
        var ds4=InputSystem.AddDevice<DualShock4GamepadHID>();try{
            InputSystem.QueueStateEvent(ds4,new DS4State{lx=128,ly=0,rx=255,ry=128,buttons=200,shoulders=3,lt=255,rt=255});InputSystem.Update();var input=new ForceInput();input.Read();
            Check(input.PlayStation&&input.Attack&&input.Block&&input.Dodge&&input.LockOn&&input.Primary&&input.Secondary,"DualShock 4 HID mapping: R1 L1 circle triangle R2 L2");
        }finally{InputSystem.RemoveDevice(ds4);}
        var keyboard=InputSystem.AddDevice<Keyboard>();try{
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.Q,Key.E,Key.J,Key.K,Key.Space,Key.Tab));InputSystem.Update();var input=new ForceInput();input.Read();
            Check(input.Move.y==1&&input.Primary&&input.Secondary&&input.Attack&&input.Block&&input.Dodge&&input.LockOn,"Keyboard bindings for movement, Force and all combat actions");
        }finally{InputSystem.RemoveDevice(keyboard);}
        var mouse=InputSystem.AddDevice<Mouse>();try{
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(800,450),delta=new Vector2(10,2)}.WithButton(MouseButton.Left).WithButton(MouseButton.Right));InputSystem.Update();var input=new ForceInput();input.Read();Check(input.Attack&&input.Block&&!input.Primary&&!input.Secondary&&input.Aim.x>0,"Mouse combat buttons are separate from Force bindings");
        }finally{InputSystem.RemoveDevice(mouse);}
        g.Begin(ForceSide.Light,false);g.controls.UsingPad=false;yield return new WaitForSeconds(.7f);Check(Vector3.Distance(g.view.transform.position,g.player.position)>3,"Shoulder camera follows behind character");
        Check(Physics.Raycast(g.player.position+Vector3.up*2,Vector3.down,5,~(1<<8)),"Valley ground has physical collision");
        var obstruction=new GameObject("Camera collision verification");obstruction.transform.position=g.player.position+new Vector3(.25f,1.9f,-2.2f);obstruction.AddComponent<BoxCollider>().size=new Vector3(1,2,1);
        yield return new WaitForSeconds(.4f);Check(Vector3.Distance(g.view.transform.position,g.player.position+Vector3.up*1.45f)<3.6f,"Camera pulls forward in front of an obstruction");Destroy(obstruction);yield return new WaitForSeconds(.4f);
        yield return new WaitForEndOfFrame();Capture("03-third-person.png");
        g.Begin(ForceSide.Dark,false);g.StartBoss();g.Teleport(new Vector3(0,0,-2));boss=g.enemies[0];boss.body.position=new Vector3(0,0,5);boss.attack=99;g.lockedTarget=boss;g.facing=Vector3.forward;g.controls.UsingPad=true;g.controls.Primary=true;
        yield return new WaitForSeconds(.7f);yield return new WaitForEndOfFrame();Capture("04-dark-boss.png");g.controls.Primary=false;g.controls.Block=true;yield return new WaitForSeconds(.15f);yield return new WaitForEndOfFrame();Capture("05-guard.png");
        yield return new WaitForSeconds(.3f);File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: "+passed+" integration checks. Third-person camera, imported assets, menu, four Force powers, weapon combat, directional block, guard break, stamina, roll, lock-on, progression and synthetic DualSense / DualShock 4 HID input. Physical controllers not tested.");Debug.Log("FORCE_DEMO_QA_OK "+passed);Application.Quit(0);
    }
    // Publicly documented Input System virtual HID state; verifies PS4 device layout.
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct DS4State:IInputStateTypeInfo {
        public byte lx,ly,rx,ry,buttons,shoulders,system,lt,rt;public FourCC format=>new FourCC('D','4','V','S');
    }
}
}
