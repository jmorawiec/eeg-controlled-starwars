using UnityEngine;

namespace ForceDemo {
public enum MenuPage { Home, Side, Controls, Settings }

public class ForceHUD : MonoBehaviour {
    public ForceGame game;
    public MenuPage page;
    public int selection;
    bool wasPaused;
    MenuTheater theater;
    Font regular, semibold;
    GUIStyle textStyle;
    Texture2D white, shade, bottomShade, disc;
    float scale;
    Vector2 offset;
    readonly Color text = new Color(.93f, .94f, .95f);
    readonly Color muted = new Color(.59f, .63f, .68f);
    readonly Color panel = new Color(.008f, .012f, .018f, .64f);
    string Primary => game.controls.UsingPad ? "R2" : "Q";
    string Secondary => game.controls.UsingPad ? "L2" : "E";
    string Confirm => game.controls.UsingPad ? "×" : "Enter";

    void Awake() {
        white = Texture2D.whiteTexture;
        regular = Resources.Load<Font>("Fonts/Barlow-Regular");
        semibold = Resources.Load<Font>("Fonts/Barlow-SemiBold");
        if (!regular) regular = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!semibold) semibold = regular;
        shade = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        bottomShade = new Texture2D(1, 256, TextureFormat.RGBA32, false);
        for (int i = 0; i < 256; i++) {
            float t = i / 255f;
            shade.SetPixel(i, 0, new Color(.006f, .009f, .014f, Mathf.SmoothStep(.94f, 0, t)));
            bottomShade.SetPixel(0, i, new Color(.006f, .009f, .014f, Mathf.Pow(1 - t, 1.4f) * .94f));
        }
        shade.Apply(); bottomShade.Apply();
        disc = new Texture2D(48, 48, TextureFormat.RGBA32, false);
        for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++)
            disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(23.5f - Vector2.Distance(new Vector2(x,y), new Vector2(23.5f,23.5f)))));
        disc.Apply();
    }
    void Start() { theater = gameObject.AddComponent<MenuTheater>(); theater.game = game; }
    void Update() {
        if (!game || (!Application.isFocused && !game.SkipInput)) return;
        if(game.neuro.TrainingOpen)return;
        if (game.paused != wasPaused) { SetPage(MenuPage.Home); wasPaused = game.paused; }
        if (game.stage != DemoStage.Choice && !game.paused) return;
        if (game.controls.Light) { game.Begin(ForceSide.Light); return; }
        if (game.controls.Dark) { game.Begin(ForceSide.Dark); return; }
        int count = page == MenuPage.Home ? 4 : page == MenuPage.Side ? 2 : page == MenuPage.Settings ? 8 : 1;
        if (game.controls.Navigate != 0) selection = (selection + game.controls.Navigate + count) % count;
        if (page == MenuPage.Side && game.controls.Adjust != 0) selection = Mathf.Clamp(selection + game.controls.Adjust, 0, 1);
        if (page == MenuPage.Settings && game.controls.Adjust != 0) Adjust(game.controls.Adjust);
        if (game.controls.Confirm) Activate();
        if ((game.controls.Back || game.controls.Pause) && page != MenuPage.Home) SetPage(MenuPage.Home);
    }
    public void SetPage(MenuPage target) { page = target; selection = 0; }
    public void Activate() {
        if (page == MenuPage.Home) {
            if (selection == 0) { if (game.paused) { game.paused = false; Time.timeScale = 1; } else SetPage(MenuPage.Side); }
            else if (selection == 1) SetPage(MenuPage.Controls);
            else if (selection == 2) SetPage(MenuPage.Settings);
            else if (game.paused) game.ReturnToMenu(); else Application.Quit();
        } else if (page == MenuPage.Side) game.Begin(selection == 0 ? ForceSide.Light : ForceSide.Dark);
        else if (page == MenuPage.Controls) SetPage(MenuPage.Home);
        else if (selection == 7) SetPage(MenuPage.Home);
        else if(selection==5) game.neuro.OpenTraining();
        else if(selection==6) game.neuro.ConnectService();
        else Adjust(1);
    }
    public void Adjust(int amount) {
        if (selection == 0) { game.MasterVolume = Mathf.Clamp01(game.MasterVolume + amount * .1f); PlayerPrefs.SetFloat("MasterVolume", game.MasterVolume); }
        else if (selection == 1) { game.orbit.sensitivity = Mathf.Clamp(game.orbit.sensitivity + amount * .1f, .3f, 2); PlayerPrefs.SetFloat("CameraSensitivity", game.orbit.sensitivity); }
        else if (selection == 2) { game.orbit.atmosphere.bloom = !game.orbit.atmosphere.bloom; PlayerPrefs.SetInt("Bloom", game.orbit.atmosphere.bloom ? 1 : 0); }
        if(selection==3)game.neuro.SetMode(game.neuro.Mode==NeuroMode.Simulation?NeuroMode.EEG:NeuroMode.Simulation);
        if(selection==4)game.neuro.NextProfile();
        PlayerPrefs.Save();
    }
    void Box(Rect r, Color c) { Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }
    void Label(Rect r, string value, int size = 20, Color? color = null, bool bold = false, TextAnchor align = TextAnchor.UpperLeft) {
        if (textStyle == null) textStyle = new GUIStyle();
        textStyle.font = bold ? semibold : regular; textStyle.fontSize = size; textStyle.fontStyle = FontStyle.Normal;
        textStyle.normal.textColor = color ?? text; textStyle.wordWrap = true; textStyle.richText = false; textStyle.alignment = align;
        GUI.Label(r, value, textStyle);
    }
    void Dot(float x, float y, float size, Color c) { Color old = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x-size/2,y-size/2,size,size),disc); GUI.color = old; }
    bool Row(Rect r, string value, int index) {
        bool hover = !game.controls.UsingPad && r.Contains(Event.current.mousePosition);
        if (hover && Event.current.type == EventType.MouseMove) selection = index;
        bool active = selection == index;
        if (active) Dot(r.x+3,r.center.y,4,text);
        Label(new Rect(r.x+18,r.y+13,r.width-36,r.height-13),value,22,active?text:muted,active);
        return GUI.Button(r,GUIContent.none,GUIStyle.none);
    }
    bool Button(Rect r, string value, bool active = true) {
        bool hover = r.Contains(Event.current.mousePosition);
        Box(r,new Color(.006f,.009f,.015f,hover?.72f:.46f));
        Label(r,value,19,active?text:muted,true,TextAnchor.MiddleCenter);
        return GUI.Button(r,GUIContent.none,GUIStyle.none);
    }
    Rect Full => new Rect(-offset.x/scale,-offset.y/scale,Screen.width/scale,Screen.height/scale);
    void OnGUI() {
        if (!game || !game.view) return;
        scale = Mathf.Min(Screen.width/1600f,Screen.height/900f);
        offset = new Vector2((Screen.width-1600*scale)/2,(Screen.height-900*scale)/2);
        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x,offset.y,0),Quaternion.identity,Vector3.one*scale);
        if(game.neuro.TrainingOpen)Training();
        else if (game.stage == DemoStage.Choice) Menu();
        else if (game.stage == DemoStage.Opening) Opening();
        else if (game.paused) Menu();
        else if (game.stage == DemoStage.BossIntro) BossIntro();
        else if (game.stage == DemoStage.Victory || game.stage == DemoStage.Defeat) End();
        else { WorldLabels(); HUD(); }
        GUI.matrix = Matrix4x4.identity;
    }
    void Opening() {
        var intro = game.opening;
        Rect full = Full;
        Box(new Rect(full.x,full.y,full.width,82-full.y),Color.black);
        Box(new Rect(full.x,818,full.width,full.yMax-818),Color.black);
        float opacity = intro.SubtitleOpacity;
        if(opacity>0) {
            // A soft shadow keeps the subtitle readable across moving scenery.
            Label(new Rect(251,752,1100,48),intro.Subtitle,25,new Color(0,0,0,opacity),false,TextAnchor.MiddleCenter);
            Label(new Rect(250,750,1100,48),intro.Subtitle,25,new Color(.94f,.95f,.96f,opacity),false,TextAnchor.MiddleCenter);
        }
        if(intro.CanSkip) Label(new Rect(1260,843,280,30),Confirm+"  Skip",17,muted,false,TextAnchor.MiddleRight);
        if(intro.Fade>0) Box(full,new Color(0,0,0,intro.Fade));
    }
    void Menu() {
        if (page == MenuPage.Side && !game.paused) { Sides(); return; }
        Box(Full,new Color(.006f,.009f,.015f,.25f));
        if (page == MenuPage.Home) {
            if (!game.paused && theater && theater.lightPortrait) GUI.DrawTexture(new Rect(850,0,750,938),theater.lightPortrait,ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(new Rect(Full.x,Full.y,1330-offset.x/scale,Full.height),shade);
            Label(new Rect(92,210,780,220),game.paused?"Paused":"Echoes\nof the Force",74,null,true);
            string[] items = game.paused ? new[]{"Resume","Controls","Settings","Main menu"} : new[]{"Play","Controls","Settings","Quit"};
            for(int i=0;i<4;i++) if(Row(new Rect(92,495+i*58,360,54),items[i],i)){selection=i;Activate();}

        } else {
            Box(Full,new Color(.006f,.009f,.015f,.72f));
            if(page==MenuPage.Controls) Controls(); else Settings();
        }
    }
    void Sides() {
        Rect full=Full;
        for(int i=0;i<2;i++){
            Rect half=new Rect(i==0?full.x:800,full.y,full.width/2,full.height);
            if(!game.controls.UsingPad && half.Contains(Event.current.mousePosition) && Event.current.type==EventType.MouseMove) selection=i;
            bool active=selection==i;
            Box(half,i==0?new Color(.018f,.051f,.085f):new Color(.082f,.012f,.019f));
            Texture image=theater?(i==0?theater.lightPortrait:theater.darkPortrait):null;
            if(image) GUI.DrawTexture(half,image,ScaleMode.ScaleAndCrop);
            Box(half,new Color(0,0,0,active?0:.70f));
            GUI.DrawTexture(new Rect(half.x,490,half.width,full.yMax-490),bottomShade);
            float x=i*800+72;
            if(GUI.Button(new Rect(half.x,105,half.width,655),GUIContent.none,GUIStyle.none)) selection=i;
            Label(new Rect(x,624,640,76),i==0?"Jedi":"Sith",54,active?text:muted,true);
            Label(new Rect(x,707,640,32),i==0?"Force Push · Force Heal":"Force Lightning · Force Choke",19,active?text:muted);
            if(active && Button(new Rect(x,785,225,52),"Choose")) game.Begin(i==0?ForceSide.Light:ForceSide.Dark);
        }
        if(Button(new Rect(40,35,130,42),"Back")) SetPage(MenuPage.Home);
    }
    void Controls() {
        Label(new Rect(100,145,1200,70),"Controls",44,null,true);
        Label(new Rect(100,280,500,32),"Action",17,muted);
        Label(new Rect(650,280,430,32),"Keyboard & mouse",17,muted);
        Label(new Rect(1120,280,370,32),"PlayStation",17,muted);
        string[,] rows={{"Movement / camera","WASD / mouse","Left / right stick"},{"Attack","LMB or J","R1"},{"Block","RMB or K","L1"},{"Dodge","Space","○"},{"Force powers","Q / E","R2 / L2"},{"Lock-on","Tab / middle mouse","△"},{"Sprint / pause","Shift / Escape","L3 / Options"}};
        for(int i=0;i<7;i++){
            float y=335+i*47;
            Label(new Rect(100,y,510,36),rows[i,0]);Label(new Rect(650,y,450,36),rows[i,1],19,muted);Label(new Rect(1120,y,380,36),rows[i,2]);
        }
        if(Row(new Rect(82,740,300,54),"Back",0)) SetPage(MenuPage.Home);
    }
    public void AfterTraining(){wasPaused=game.paused;SetPage(MenuPage.Settings);}
    void Settings() {
        var n=game.neuro;
        Label(new Rect(100,100,1200,70),"Settings",44,null,true);
        string[] labels={"Volume","Camera sensitivity","Bloom"};
        string[] values={Mathf.RoundToInt(game.MasterVolume*100)+"%",game.orbit.sensitivity.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"×",game.orbit.atmosphere.bloom?"On":"Off"};
        for(int i=0;i<3;i++){
            float y=245+i*89;
            if(Row(new Rect(82,y,520,62),labels[i],i)){selection=i;Adjust(1);}
            Label(new Rect(390,y+15,200,36),values[i],22,null,false,TextAnchor.UpperRight);
            if(Button(new Rect(635,y+6,46,46),"−",selection==i)){selection=i;Adjust(-1);}
            if(Button(new Rect(691,y+6,46,46),"+",selection==i)){selection=i;Adjust(1);}
        }
        if(Row(new Rect(852,245,640,62),"Input mode",3)){selection=3;Adjust(1);}
        Label(new Rect(1110,260,380,36),n.Mode==NeuroMode.Simulation?"Simulation":"EEG + gestures",22,game.Accent,false,TextAnchor.UpperRight);
        if(Row(new Rect(852,334,180,62),"Profile",4)){selection=4;Adjust(1);}
        n.Profile.name=Field(new Rect(1050,343,250,42),n.Profile.name);if(GUI.changed)n.SaveProfiles();
        if(Button(new Rect(1315,340,48,46),"→"))n.NextProfile();
        if(Button(new Rect(1380,340,90,46),"+ New"))n.NewProfile();
        if(Row(new Rect(852,423,640,62),"Start training",5)){selection=5;n.OpenTraining();}
        if(Row(new Rect(852,512,640,62),n.Connected?"Input service connected":"Connect EEG input",6)){selection=6;n.ConnectService();}
        Label(new Rect(870,585,610,64),n.Mode==NeuroMode.Simulation?"No headset needed. Practice gestures and imagination inputs together.":n.ServiceHint,18,muted);
        if(n.Mode==NeuroMode.EEG){
            Label(new Rect(870,665,200,30),"Python interpreter",17,muted);n.PythonPath=Field(new Rect(870,700,600,40),n.PythonPath);
            Label(new Rect(870,750,170,30),"Headset serial",17,muted);n.HeadsetSerial=Field(new Rect(1055,744,415,40),n.HeadsetSerial);
            if(GUI.changed)n.SaveConnection();
        }else Label(new Rect(100,590,625,105),"Simulation: hold Z / X / C / V for Push / Heal / Choke / Lightning. Add Ctrl to simulate imagination. Q / E remain available for normal play.",19,muted);
        if(Row(new Rect(82,785,300,54),"Back",7))SetPage(MenuPage.Home);
    }
    string Field(Rect r,string value){
        var style=new GUIStyle(GUI.skin.textField){font=regular,fontSize=18,padding=new RectOffset(12,12,8,8)};style.normal.textColor=text;return GUI.TextField(r,value,240,style);
    }
    void Training(){
        var n=game.neuro;Box(Full,new Color(.006f,.009f,.015f,.37f));GUI.DrawTexture(new Rect(Full.x,Full.y,1120,Full.height),shade);
        string phase=n.Phase;bool finished=phase=="complete"||phase=="failed";
        Label(new Rect(80,60,950,70),n.Mode==NeuroMode.Simulation?"Training practice":"Personal training",42,null,true);
        Label(new Rect(82,140,900,40),n.Profile.name+"  ·  "+(n.Mode==NeuroMode.Simulation?"Simulated input":n.PersonalModel?"Personal EEG model":"New EEG profile"),19,muted);
        string title,copy;
        if(n.TrainingPaused){title="Session paused";copy="Return to a comfortable position, then resume. The current trial will restart.";}
        else if(finished){title=phase=="failed"?"Try this session again":n.Mode==NeuroMode.Simulation?"Practice complete":"Training complete";copy=n.Mode==NeuroMode.Simulation?"No EEG model was trained. Switch to EEG to record your own headset data.":n.TrainingMessage;}
        else if(phase=="fitting"){title="Learning your signal…";copy="Keep the headset connected. Your recordings are being fitted to a personal model.";}
        else if(n.Mode==NeuroMode.EEG&&(!n.SignalReady||phase=="ready")){title="Check your headset";copy="Connect the camera and EEG service. Adjust electrode contact, then begin.";}
        else if(phase=="rest"){title="Relax  "+Mathf.CeilToInt(n.Countdown)+" s";copy="Lower your hands and let go of the imaginary action.\nNext: "+n.TargetAbility+(n.TargetImagined?" with imagination.":" without imagination.");}
        else {title=n.TargetAbility+(n.TargetImagined?" + imagination":" · gesture only");var scenario=System.Array.Find(n.Definitions.scenarios,x=>x.ability==n.TargetAbility);copy=(scenario?.gesture??"Perform the requested gesture.")+"\n"+(n.TargetImagined?(scenario?.trigger??"Imagine the effect."):"Do not imagine the spell. Make only the hand gesture.")+" Hold 3–4 s, then release.";}
        Label(new Rect(80,212,875,100),title,38,null,true);
        Label(new Rect(82,314,800,110),copy,23);
        if(n.TrialTotal>0){if(phase=="act"&&!n.TrainingPaused)Label(new Rect(82,400,790,30),n.TrainingMessage,16,muted);
        Label(new Rect(82,438,790,30),"Trial "+Mathf.Min(n.TrialIndex+1,n.TrialTotal)+" / "+n.TrialTotal+"  ·  Recorded "+n.Completed+"  ·  Skipped "+n.Missed,17,muted);Bar(82,480,720,3,n.TrialIndex/(float)n.TrialTotal,game.Accent);}
        string[] names={"Push","Heal","Choke","Lightning"};
        Label(new Rect(82,515,220,30),"Detected input",17,muted);Label(new Rect(360,515,360,30),n.Mode==NeuroMode.Simulation?"Simulated imagination":"EEG score / threshold",17,muted);
        for(int i=0;i<4;i++){
            float y=554+i*43;bool detected=n.Gesture==names[i];float cv=detected?1:0,eeg=n.Mode==NeuroMode.Simulation?n.SimulatedScore:.0f,threshold=.5f;
            var score=n.Live?.scores==null?null:System.Array.Find(n.Live.scores,x=>x.ability==names[i]);if(score!=null){cv=score.cv;eeg=score.eeg;threshold=score.threshold;}
            Label(new Rect(82,y,160,30),names[i],19,detected?text:muted,detected);Bar(245,y+10,75,3,cv,game.Accent);Bar(360,y+8,280,7,eeg,detected?game.Accent:muted);Box(new Rect(360+Mathf.Clamp01(threshold)*280,y+4,2,15),text);
            Label(new Rect(667,y-1,120,30),Mathf.RoundToInt(eeg*100)+"%",18,muted);if(n.IsActive(names[i]))Label(new Rect(795,y-1,145,30),"Active",18,game.Accent);
        }
        string footer=n.Mode==NeuroMode.Simulation?"Hold Z / X / C / V · add Ctrl for imagination\nPad: hold R1 for the requested gesture · add L1 for imagination":n.ServiceHint;
        Label(new Rect(82,747,860,58),footer,17,muted);
        if(n.TrainingPaused){if(Button(new Rect(80,827,230,48),"Resume"))n.ResumeTraining();}
        else if(n.Mode==NeuroMode.EEG&&!n.Connected){if(Button(new Rect(80,827,250,48),"Connect input"))n.ConnectService();}
        else if(finished||n.Mode==NeuroMode.EEG&&phase=="ready"){if(Button(new Rect(80,827,250,48),finished?"Start again":"Begin session"))n.StartSession();}
        if(Button(new Rect(350,827,230,48),"Back to settings"))n.CloseTraining();
        if(n.Mode==NeuroMode.EEG&&n.Live?.contacts!=null){int row=0;foreach(var c in n.Live.contacts)Label(new Rect(1050,570+row++*34,450,35),c.channel+" · "+c.status,18,c.status=="OK"?text:ArenaArt.Red);}
    }
    void Bar(float x,float y,float width,float height,float value,Color color){
        Box(new Rect(x-1,y-1,width+2,height+2),new Color(.006f,.009f,.015f,.85f));
        Box(new Rect(x,y,width*Mathf.Clamp01(value),height),color);
    }
    void HUD() {
        if(!game.Playing)return;
        if(game.stage==DemoStage.Tutorial) Tutorial();
        Sensei();
        if(game.stage==DemoStage.Waves) Label(new Rect(500,40,600,34),"Wave "+game.wave+"/2 · Remaining "+game.enemies.Count,18,text,false,TextAnchor.UpperCenter);
        if(game.stage==DemoStage.Boss){
            var enemy=game.enemies.Find(x=>x.boss);
            if(enemy!=null){Label(new Rect(500,35,600,34),"Rift Warden",22,text,false,TextAnchor.UpperCenter);Bar(500,81,600,6,enemy.hp/enemy.maxHP,text);Bar(500,94,600,2,enemy.stagger,game.Accent);}
        }
        if(game.lockedTarget==null) Dot(800,450,3,new Color(1,1,1,.65f));
        if(game.hitMarker>0) Label(new Rect(786,432,28,32),"×",26,new Color(1,1,1,Mathf.Clamp01(game.hitMarker*6)),false,TextAnchor.MiddleCenter);

        Color health=game.health<30||game.hurtFlash>0?new Color(.92f,.29f,.27f):text;
        Bar(48,802,294,8,game.health/100,health);
        Bar(48,823,294,4,game.stamina/100,game.guardBroken>0?ArenaArt.Red:new Color(.56f,.68f,.53f));
        Bar(48,840,294,4,game.energy/100,game.Accent);
        Ability(1240,784,Primary,game.side==ForceSide.Light?"Force Push":"Force Lightning",game.primaryCooldown,1.25f);
        Ability(1240,831,Secondary,game.side==ForceSide.Light?"Force Heal":"Force Choke",game.secondaryCooldown,7);
        if(game.guardBroken>0) Label(new Rect(500,620,600,40),"Guard broken",22,ArenaArt.Red,false,TextAnchor.MiddleCenter);
        else if(game.noticeTime>0&&game.notice.Contains("PARRIED")) Label(new Rect(500,620,600,40),"Parried",20,text,false,TextAnchor.MiddleCenter);
    }
    void Ability(float x,float y,string key,string name,float cooldown,float duration){
        Box(new Rect(x,y,31,31),new Color(.008f,.012f,.018f,.66f));
        Label(new Rect(x,y,31,31),key,15,text,true,TextAnchor.MiddleCenter);
        Label(new Rect(x+44,y+3,205,31),name,18,cooldown>0?muted:text);
        if(cooldown>0){Label(new Rect(x+242,y+3,70,30),cooldown.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)+" s",16,muted,false,TextAnchor.UpperRight);Bar(x+44,y+32,266,2,1-cooldown/duration,game.Accent);}
    }
    void Sensei(){
        var d=game.sensei;if(d.Time<=0||string.IsNullOrEmpty(d.Text))return;
        float alpha=Mathf.Clamp01(d.Time*2);Color old=GUI.color;GUI.color=new Color(1,1,1,alpha);
        Box(new Rect(330,668,940,96),panel);Box(new Rect(330,668,3,96),game.Accent);
        Label(new Rect(352,678,900,26),d.Speaker,18,game.Accent,true);
        Label(new Rect(352,704,900,56),d.Text,20,text);
        GUI.color=old;
    }
    void Tutorial(){
        string copy;
        if(game.tutorialStep==0) copy="Lock on to a guard ["+(game.controls.UsingPad?"△":"Tab")+"].\n"+(game.side==ForceSide.Light?"Use Force Push":"Hold Force Lightning")+" ["+Primary+"].";
        else if(game.tutorialStep==1) copy=game.side==ForceSide.Light?"Heal yourself ["+Secondary+"].\nBlock a projectile ["+(game.controls.UsingPad?"L1":"RMB")+"].":"Hold Force Choke ["+Secondary+"].";
        else copy=Confirm+" — begin combat";
        Box(new Rect(32,33,370,game.tutorialStep==2?56:88),panel);
        Label(new Rect(48,47,338,65),copy,18);
    }
    void WorldLabels(){
        if(!game.Playing)return;
        foreach(var enemy in game.enemies){
            if(!enemy.body||enemy.boss||(enemy.hit<=0&&game.lockedTarget!=enemy))continue;
            Vector3 p=game.view.WorldToScreenPoint(enemy.Position+Vector3.up*2.2f);if(p.z<=0)continue;
            float x=(p.x-offset.x)/scale,y=(Screen.height-p.y-offset.y)/scale;if(x<20||x>1580||y<100||y>740)continue;
            Bar(x-30,y,60,3,enemy.hp/enemy.maxHP,text);
            if(game.lockedTarget==enemy) Dot(x,y-16,5,text);
        }
        if(game.lockedTarget!=null&&game.lockedTarget.boss&&game.lockedTarget.body){var p=game.view.WorldToScreenPoint(game.lockedTarget.Position+Vector3.up*1.7f);if(p.z>0)Dot((p.x-offset.x)/scale,(Screen.height-p.y-offset.y)/scale,5,text);}
    }
    void Overlay(){Box(Full,new Color(.006f,.009f,.015f,.90f));}
    void BossIntro(){
        Overlay();Label(new Rect(300,260,1000,90),"Rift Warden",48,null,true);
        Label(new Rect(300,385,930,100),"Dodge out of the red circle.\nBlock slashes and projectiles.",23,muted);
        if(Button(new Rect(300,570,260,54),"Fight")) game.StartBoss();
    }
    void End(){
        Overlay();bool win=game.stage==DemoStage.Victory;
        Label(new Rect(300,260,1100,90),win?"Victory":"Try again",48,null,true);
        string time=Mathf.FloorToInt(game.playTime/60)+":"+((int)game.playTime%60).ToString("00");
        Label(new Rect(300,390,1000,40),"Time "+time+" · Defeated "+game.kills,21,muted);
        if(Button(new Rect(300,550,300,54),"Choose a side")) game.ReturnToMenu();
    }
    void OnDestroy(){if(shade)Destroy(shade);if(bottomShade)Destroy(bottomShade);if(disc)Destroy(disc);}
}
}
