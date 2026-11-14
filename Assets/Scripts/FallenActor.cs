using UnityEngine;
namespace ForceDemo {
public class FallenActor:MonoBehaviour {
 public CharacterVisual visual;float age;
 void Start(){if(visual){visual.dead=true;visual.attack=visual.roll=visual.channel=0;visual.hitReaction=0;}foreach(var l in GetComponentsInChildren<Light>())l.enabled=false;}
 void Update(){age+=Time.deltaTime;if(age>3.5f)Destroy(gameObject);}
}
}
