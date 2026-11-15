using UnityEngine;

namespace ForceDemo {
// Timeline ownership lives here; the game does not simulate combat during Opening.
public class OpeningSequence : MonoBehaviour {
    public ForceGame game;
    public const float Duration = 14f;
    public float Elapsed { get; private set; }
    public bool Active { get; private set; }
    public bool CanSkip => Active && Elapsed >= .8f;
    AudioSource bed;
    AudioClip lightBed, darkBed;
    static readonly float[] Cuts={4.4f,8.5f,11.7f};
    CharacterVisual master;
    float audioFade;bool masterWeaponWasActive;

    public string Subtitle {
        get {
            if(Elapsed<4.1f)return "Valley of Echoes";
            if(Elapsed<8.5f)return game.side==ForceSide.Light ? "Master Auren: Find your balance. Then reach out." : "Darth Veyr: Reach out. Make the Force obey.";
            return game.side==ForceSide.Light ? "Master Auren: Your training begins here." : "Darth Veyr: Show me what you can do.";
        }
    }
    public float SubtitleOpacity {
        get {
            if(!Active)return 0;
            float start=Elapsed<4.1f?.65f:Elapsed<8.5f?4.7f:8.8f;
            float end=Elapsed<4.1f?3.7f:Elapsed<8.5f?8.15f:11.6f;
            return Mathf.Clamp01(Mathf.Min((Elapsed-start)/.4f,(end-Elapsed)/.35f));
        }
    }
    public float Fade {
        get {
            if(!Active)return 0;
            float f=Mathf.Clamp01(1-Elapsed/.6f);
            foreach(float cut in Cuts)f=Mathf.Max(f,1-Mathf.Abs(Elapsed-cut)/.2f);
            return Mathf.Clamp01(f);
        }
    }
    public void Play() {
        Active=true;Elapsed=0;audioFade=1;
        master=game.mentor.GetComponent<CharacterVisual>();
        if(master){master.ResetMotion();master.channel=0;if(master.weapon){masterWeaponWasActive=master.weapon.gameObject.activeSelf;master.weapon.gameObject.SetActive(false);}}
        game.visuals.SetBladeExtension(0);
        if(!bed){bed=gameObject.AddComponent<AudioSource>();bed.spatialBlend=0;bed.playOnAwake=false;bed.loop=false;}
        if(game.side==ForceSide.Light){if(!lightBed)lightBed=CreateBed(false);bed.clip=lightBed;}
        else{if(!darkBed)darkBed=CreateBed(true);bed.clip=darkBed;}
        bed.volume=game.muted?0:game.MasterVolume*.24f;bed.Play();
        RenderCamera();
    }
    public void Tick(float dt) {
        if(!Active)return;
        Elapsed=Mathf.Min(Duration,Elapsed+dt);
        if(master)master.channel=Elapsed>=4.7f&&Elapsed<7.7f?.55f:0;
        game.visuals.SetBladeExtension(Mathf.SmoothStep(0,1,(Elapsed-9.1f)/.38f));
        if(Elapsed>=Duration || (CanSkip && (game.controls.Confirm||game.controls.Back||game.controls.Dodge||game.controls.Pause)))game.FinishOpening();
    }
    public void Cancel() {
        Active=false;
        if(master){master.channel=0;if(master.weapon)master.weapon.gameObject.SetActive(masterWeaponWasActive);}
        if(game&&game.visuals){if(game.visuals.weapon)game.visuals.weapon.gameObject.SetActive(true);game.visuals.SetBladeExtension(1);}
    }
    public void SetFocus(bool focused) {
        if(!bed||!Active)return;
        if(focused)bed.UnPause();else bed.Pause();
    }
    void Update() {
        if(!bed)return;
        if(!Active)audioFade=Mathf.MoveTowards(audioFade,0,Time.unscaledDeltaTime*3);
        bed.volume=(game.muted?0:game.MasterVolume*.24f)*audioFade;
        if(!Active&&audioFade<=0&&bed.isPlaying)bed.Stop();
    }
    static float Ease(float t)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
    public void RenderCamera() {
        if(!Active||!game.view)return;
        Vector3 position,target;float fov;
        if(Elapsed<4.4f){
            float t=Ease(Elapsed/4.4f);
            position=Vector3.Lerp(new Vector3(-3.8f,8,-16),new Vector3(-1.5f,5.2f,-11.5f),t);
            target=Vector3.Lerp(new Vector3(0,2,10),new Vector3(0,1.7f,11),t);fov=54;
        }else if(Elapsed<8.5f){
            float t=Ease((Elapsed-4.4f)/4.1f);
            position=game.mentor.position+Vector3.Lerp(new Vector3(2.4f,1.65f,-3.5f),new Vector3(1.6f,1.55f,-2.9f),t);
            target=game.mentor.position+Vector3.up*1.25f;fov=43;
        }else if(Elapsed<11.7f){
            float t=Ease((Elapsed-8.5f)/3.2f);
            position=game.player.position+Vector3.Lerp(new Vector3(2.1f,1.65f,3.3f),new Vector3(1.55f,1.5f,2.8f),t);
            target=game.player.position+Vector3.up*1.15f;fov=43;
        }else{
            float t=Ease((Elapsed-11.7f)/(Duration-11.7f));
            Quaternion endRotation=Quaternion.Euler(12,0,0);
            Vector3 endPosition=game.player.position+Vector3.up*1.4f+endRotation*new Vector3(.4f,.35f,-4.25f);
            position=Vector3.Lerp(game.player.position+new Vector3(1.5f,2.7f,-5.8f),endPosition,t);
            Quaternion startRotation=Quaternion.LookRotation(game.player.position+new Vector3(0,1.25f,5)-position);
            game.view.transform.SetPositionAndRotation(position,Quaternion.Slerp(startRotation,endRotation,t));
            game.view.fieldOfView=Mathf.Lerp(54,62,t);return;
        }
        game.view.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));game.view.fieldOfView=fov;
    }
    static AudioClip CreateBed(bool dark) {
        const int rate=22050;int count=(int)((Duration+1)*rate);var data=new float[count];
        // Original quiet pad: a low pedal, slow beating harmonics and a rising fifth.
        float root=dark?65.406f:130.813f,third=dark?1.189207f:1.259921f;
        for(int i=0;i<count;i++){
            float t=(float)i/rate;
            float envelope=Mathf.SmoothStep(0,1,t/2)*Mathf.Clamp01((Duration+1-t)/2.5f);
            float chord=Mathf.Sin(t*root*Mathf.PI*2)*.30f+Mathf.Sin(t*root*third*Mathf.PI*2)*.14f+Mathf.Sin(t*root*1.4983f*Mathf.PI*2)*.16f;
            float air=Mathf.Sin(t*root*4.003f*Mathf.PI*2)*.035f*Mathf.Sin(t*.7f);
            data[i]=(chord+air)*envelope*(.7f+.08f*Mathf.Sin(t*.9f));
        }
        var clip=AudioClip.Create(dark?"Veyr - opening ambience":"Auren - opening ambience",count,1,rate,false);clip.SetData(data,0);return clip;
    }
    void OnDestroy(){if(lightBed)Destroy(lightBed);if(darkBed)Destroy(darkBed);}
}
}
