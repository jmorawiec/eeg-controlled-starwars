using UnityEngine;
namespace ForceDemo {
[DefaultExecutionOrder(120)]
public class ShoulderCamera:MonoBehaviour {
    public ForceGame game;public float yaw,pitch=12,sensitivity=1;public ForceAtmosphere atmosphere;public float punch;
    public void Punch(float amount){punch=Mathf.Max(punch,amount);}
    Vector3 followVelocity;float distance=4.25f;
    public Vector3 Forward=>Quaternion.Euler(0,yaw,0)*Vector3.forward;
    public void ResetView(){yaw=0;pitch=12;followVelocity=Vector3.zero;distance=4.25f;transform.rotation=Quaternion.Euler(pitch,yaw,0);transform.position=game.player.position+Vector3.up*1.4f+transform.rotation*new Vector3(.4f,.35f,-distance);}
    void LateUpdate(){
        if(!game||!game.player)return;
        if(game.neuro&&game.neuro.TrainingOpen){atmosphere.menu=false;game.view.fieldOfView=46;Vector3 hero=game.player.position;transform.position=hero+new Vector3(-2.1f,1.6f,4.1f);transform.LookAt(hero+new Vector3(1.65f,1.12f,.1f));return;}
        if(game.stage==DemoStage.Opening){atmosphere.menu=false;game.opening.RenderCamera();return;}
        bool menu=game.stage==DemoStage.Choice;atmosphere.menu=game.paused||menu;
        game.view.fieldOfView=Mathf.Lerp(game.view.fieldOfView,(menu?50f:62f)+(game.dodgeTime>0?3.5f:0)-punch*5.5f,1-Mathf.Exp(-Time.unscaledDeltaTime*20));punch=Mathf.MoveTowards(punch,0,Time.unscaledDeltaTime*8);
        if(menu){float t=Time.unscaledTime*Mathf.PI*2/30;Vector3 hero=game.player.position;transform.position=hero+new Vector3(-2.7f+Mathf.Sin(t)*.55f,1.25f+Mathf.Cos(t)*.07f,-3.1f+Mathf.Cos(t)*.3f);transform.LookAt(hero+new Vector3(1.2f,1.7f,7f));return;}
        if(game.paused)return;
        float dt=Mathf.Min(Time.deltaTime,.05f);
        if(game.Playing&&game.lockedTarget==null){
            Vector2 look=game.controls.Aim;float rate=game.controls.UsingPad?105*dt:.075f;
            yaw+=look.x*rate*sensitivity;pitch=Mathf.Clamp(pitch-look.y*rate*sensitivity,-18,48);
        }
        Vector3 focus=game.player.position+Vector3.up*1.4f;
        if(game.lockedTarget!=null&&game.lockedTarget.body&&game.lockedTarget.hp>0){
            Vector3 toward=game.lockedTarget.Position-game.player.position;toward.y=0;
            if(toward.sqrMagnitude>.01f)yaw=Mathf.LerpAngle(yaw,Quaternion.LookRotation(toward).eulerAngles.y,1-Mathf.Exp(-dt*4));
            pitch=Mathf.Lerp(pitch,10,1-Mathf.Exp(-dt*2.5f));
        }
        Quaternion rotation=Quaternion.Euler(pitch,yaw,0);
        Vector3 boom=rotation*new Vector3(.4f,.35f,-4.25f);float targetDistance=boom.magnitude;
        if(Physics.SphereCast(focus,.18f,boom.normalized,out RaycastHit hit,targetDistance,~(1<<8),QueryTriggerInteraction.Ignore))targetDistance=Mathf.Max(.65f,hit.distance-.15f);
        distance=Mathf.Lerp(distance,targetDistance,1-Mathf.Exp(-dt*(targetDistance<distance?35:6)));
        Vector3 desired=focus+boom.normalized*distance;
        transform.position=Vector3.SmoothDamp(transform.position,desired,ref followVelocity,.085f,100,dt);
        transform.rotation=rotation;
        transform.position+=transform.forward*punch*.1f;
        if(game.shake>0)transform.position+=Random.insideUnitSphere*game.shake*.018f;
    }
}
}
