using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace ForceDemo {
// All device bindings live here. Combat and spells consume device-independent intent.
public class ForceInput {
    public Vector2 Move, Aim;
    public bool Primary,PrimaryDown,Secondary,SecondaryDown,Attack,Block,Dodge,Confirm,Back,Pause,Light,Dark,Restart,LockOn,Sprint;
    public int Navigate, Adjust;
    public bool UsingPad;bool menuStickHeld;
    public bool PlayStation => Gamepad.current is DualShockGamepad;
    public void Read(){
        var k=Keyboard.current;var m=Mouse.current;var p=Gamepad.current;
        Move=Aim=Vector2.zero;Navigate=Adjust=0;
        Primary=PrimaryDown=Secondary=SecondaryDown=Attack=Block=Dodge=Confirm=Back=Pause=Light=Dark=Restart=LockOn=Sprint=false;
        if(k!=null){
            Move=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            Primary=k.qKey.isPressed;PrimaryDown=k.qKey.wasPressedThisFrame;Secondary=k.eKey.isPressed;SecondaryDown=k.eKey.wasPressedThisFrame;
            Attack=k.jKey.wasPressedThisFrame;Block=k.kKey.isPressed;Dodge=k.spaceKey.wasPressedThisFrame;Sprint=k.leftShiftKey.isPressed;
            Confirm=k.enterKey.wasPressedThisFrame;Back=k.escapeKey.wasPressedThisFrame;Pause=Back;LockOn=k.tabKey.wasPressedThisFrame;
            Light=k.digit1Key.wasPressedThisFrame;Dark=k.digit2Key.wasPressedThisFrame;Restart=k.rKey.wasPressedThisFrame;
            Navigate=(k.downArrowKey.wasPressedThisFrame?1:0)-(k.upArrowKey.wasPressedThisFrame?1:0);
            Adjust=(k.rightArrowKey.wasPressedThisFrame?1:0)-(k.leftArrowKey.wasPressedThisFrame?1:0);
            if(Move.sqrMagnitude>0||Primary||Secondary||Attack||Block||Dodge||Confirm||Pause||Navigate!=0||Adjust!=0||Light||Dark)UsingPad=false;
        }
        if(m!=null){
            Attack|=m.leftButton.wasPressedThisFrame;Block|=m.rightButton.isPressed;LockOn|=m.middleButton.wasPressedThisFrame;
            if(m.delta.ReadValue().sqrMagnitude>.5f||Attack||Block)UsingPad=false;
            if(!UsingPad)Aim=m.delta.ReadValue();
        }
        if(p!=null){
            var move=p.leftStick.ReadValue();var look=p.rightStick.ReadValue();
            if(move.sqrMagnitude>.02f||look.sqrMagnitude>.02f||p.rightShoulder.isPressed||p.leftShoulder.isPressed||p.rightTrigger.isPressed||p.leftTrigger.isPressed||p.buttonSouth.wasPressedThisFrame||p.buttonEast.wasPressedThisFrame||p.buttonNorth.wasPressedThisFrame||p.startButton.wasPressedThisFrame||p.dpad.ReadValue().sqrMagnitude>0)UsingPad=true;
            if(UsingPad){Move=move;Aim=look;}
            Attack|=p.rightShoulder.wasPressedThisFrame;Block|=p.leftShoulder.isPressed;
            Primary|=p.rightTrigger.isPressed;PrimaryDown|=p.rightTrigger.wasPressedThisFrame;
            Secondary|=p.leftTrigger.isPressed;SecondaryDown|=p.leftTrigger.wasPressedThisFrame;
            Dodge|=p.buttonEast.wasPressedThisFrame;Back|=p.buttonEast.wasPressedThisFrame;Confirm|=p.buttonSouth.wasPressedThisFrame;
            LockOn|=p.buttonNorth.wasPressedThisFrame;Sprint|=p.leftStickButton.isPressed;Pause|=p.startButton.wasPressedThisFrame;
            Navigate+=(p.dpad.down.wasPressedThisFrame?1:0)-(p.dpad.up.wasPressedThisFrame?1:0);
            Adjust+=(p.dpad.right.wasPressedThisFrame?1:0)-(p.dpad.left.wasPressedThisFrame?1:0);
            if(Mathf.Abs(move.y)>.65f && !menuStickHeld){Navigate+=move.y>0?-1:1;menuStickHeld=true;}
            if(Mathf.Abs(move.y)<.3f)menuStickHeld=false;
        }
        Move=Vector2.ClampMagnitude(Move,1);
    }
    // Camera-relative direction; mouse and right stick rotate the shoulder camera.
    public Vector3 AimWorld(Camera camera,Vector3 origin,Vector3 previous){
        Vector3 direction=camera.transform.forward;direction.y=0;return direction.sqrMagnitude>.01f?direction.normalized:previous;
    }
}
}
