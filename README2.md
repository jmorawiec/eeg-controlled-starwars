# Echoes of the Force

**What if using the Force took more than pressing a button?**

*Echoes of the Force* is a Star Wars-inspired game prototype that turns Force abilities into something you **perform**, not just select. A camera recognizes your hand gestures, while an EEG headset looks for a trained pattern of mental intent. To cast a power, you need both.

Choose the path of a Jedi or a Sith, learn to control your abilities, and put them to the test in a short, playable adventure. Our goal is simple: make a moment of gameplay feel a little closer to the way we've always imagined using the Force.

**Your hands choose the power. Your mind completes the action.**

## Feel the Force

Most games ask you to press a button to use a special ability. We wanted to try something more physical and personal.

With our experimental control system, casting a Force ability takes two steps:

1. **Make the gesture.** A webcam and computer-vision model recognize which power you're trying to use.
2. **Focus your intent.** An EEG headset measures brain activity, and a model calibrated to you checks for the learned signal associated with imagining that action.

Only when the gesture and EEG decision agree does the game activate the power. The camera tells the game *what* you're trying to do; the EEG signal helps decide *whether* it happens.

This isn't mind reading, and it doesn't recognize arbitrary thoughts. It's an experiment in combining recognizable movements with individually trained brain-signal patterns to create a more immersive way to play.

## Choose your side

Your choice changes the powers available to you:

| Jedi - Light Side | Sith - Dark Side |
| --- | --- |
| **Force Push** - Send a burst of energy at your enemies. | **Force Lightning** - Channel electricity at your enemies. |
| **Force Heal** - Recover when the fight turns against you. | **Force Choke** - Hold an enemy in your grasp. |

Each ability has its own gesture and trained EEG response. Together with regular movement and lightsaber combat, they give the two paths a different feel.

## What's playable

The hackathon prototype is a small but complete gameplay experience, built in Unity. It includes:

- **Jedi or Sith selection** with two Force powers for each side.
- **An introductory tutorial** that teaches the unconventional controls.
- **Two waves of enemies** where you can put your abilities into practice.
- **A boss fight** that serves as the final challenge.
- **Third-person combat** with movement, attacks, blocking, dodging, and lock-on.
- **Two input modes:** a self-contained simulation for trying the game without hardware, and an experimental camera + EEG mode.

The experience is designed to be approachable for someone picking it up at a demo, even if they've never worked with EEG or gesture controls before.

## How it works

The prototype connects three parts:

**Computer vision - recognizing the gesture**

A webcam tracks both hands. Using MediaPipe hand landmarks and a gesture classifier, the system identifies Push, Heal, Choke, or Lightning.

**EEG - recognizing trained intent**

A BCI Core-8 headset records brain activity. During personal calibration, the player practices gestures both with and without imagining the corresponding action. A model trained on that person's recordings learns to distinguish the two conditions.

**Unity - bringing the power into the game**

The input bridge combines the camera and EEG decisions. Once both conditions are satisfied, it sends an ability event to the Unity game, which handles the effect, animation, and gameplay rules.

The two signals serve different purposes. A gesture alone isn't enough to cast a power in real EEG mode.

For the implementation details, see [Gesture + EEG integration](Documentation/NEURO-INPUT.md) and the [gesture-recognition source](External/GestureInput/README.md).

## Built for fans - and for the crowd

We see this as more than an unusual control experiment. Imagine a Star Wars fan stepping up at a convention, choosing a side, raising their hands, concentrating, and seeing a Force power come to life on a large screen. The player gets a personal challenge; the audience gets a moment they can follow and react to.

Our longer-term idea is a **bookable, staff-guided experience** for fan events and conventions, with the game shown on a large display so spectators can share in the action. We'd like to develop the prototype alongside event organizers and eventually test it in a real event setting.

That is a direction we want to explore, **not an event-ready service today**.

## Where we are today

This project began as a hackathon prototype, and the biggest challenge wasn't building a gesture or EEG pipeline in isolation - it was getting enough reliable, person-specific EEG training data in the time available.

Recording useful examples takes time and attention. The hackathon schedule, a small pool of available volunteers, and participant fatigue made extended data collection difficult. As a result, **real EEG play currently requires individual calibration, taking roughly 20 minutes per person**. It is not yet a put-on-the-headset-and-play experience.

We chose to make that limitation explicit rather than promise universal detection we haven't demonstrated. Simulation mode is available immediately; real EEG mode is experimental and depends on hardware, signal quality, and successful personal training.

### What we'd like to improve next

- Collect a larger, more diverse set of carefully recorded EEG sessions.
- Study how well the trained model works across people and repeated sessions.
- Reduce calibration time and improve the reliability of recognizing intent.
- Make the hardware setup easier for staff and first-time players.
- Pilot a guided experience with an event organizer once the controls are dependable enough.

The long-term goal is a more general solution that needs little or no calibration for each new player. That's a research and development goal, not something the current prototype can do yet.

## Try the prototype

The Unity game runs in **Simulation** mode by default, so you can explore the tutorial, combat, Force powers, enemy waves, and boss fight **without an EEG headset, camera, or Python setup**.

### Requirements

- Unity **6000.6.9f1** (use this exact editor version)
- Git and **Git LFS** for the large Unity assets

### Open in Unity

```bash
git lfs install
git clone https://github.com/jmorawiec/eeg-controlled-starwars.git
cd eeg-controlled-starwars
git lfs pull
```

Open the project folder in Unity Hub with **Unity 6000.6.9f1**, let the assets import, and open the **Temple** scene if it isn't already open. Press **Play** to begin.

### Basic controls

| Action | Keyboard / mouse | PlayStation controller |
| --- | --- | --- |
| Move / look | WASD / mouse | Left / right stick |
| Attack / block | Left mouse or J / hold right mouse or K | R1 / hold L1 |
| Roll | Space | Circle |
| Force ability shortcuts (Simulation) | Q / E | R2 / L2 |
| Lock on | Tab / middle mouse | Triangle |
| Sprint / pause | Left Shift / Escape | L3 / Options |

To rehearse Force gestures in Simulation mode, hold **Z / X / C / V** for **Push / Heal / Choke / Lightning**. Add **Ctrl** to simulate the imagination condition. Under **Settings → Start training**, you can try the 32-trial practice session. This is a simulation of the interaction, not an actual EEG recording or calibration.

### Real camera + EEG input

Real-input mode additionally requires a **BCI Core-8 EEG headset**, a webcam, Python **3.11**, appropriate system/device access, and a trained personal profile. The supplied baseline model does not unlock casting for a new player; they must complete their own calibration.

See [EEG setup and personal training](Documentation/NEURO-INPUT.md) for installation, training, device setup, and troubleshooting. Personal recordings and trained models are stored outside the repository in Unity's persistent player-data directory.

## Inside the repository

- [`Assets/`](Assets/) - Unity scenes, scripts, characters, animation, effects, and game content.
- [`External/GestureInput/`](External/GestureInput/) - hand-gesture and EEG processing source included with the project.
- [`tools/NeuroBridge/`](tools/NeuroBridge/) - the Python adapter connecting real inputs to Unity.
- [`Documentation/NEURO-INPUT.md`](Documentation/NEURO-INPUT.md) - hardware setup, calibration, and technical details.
- [`ART-DIRECTION.md`](ART-DIRECTION.md) - the visual and gameplay direction.
- [`ASSET-CREDITS.md`](ASSET-CREDITS.md) - asset sources and third-party credits.

This repository contains a playable Unity project and its integration code. Hardware-dependent behavior still needs to be tested with a real headset and camera; passing simulated integration checks should not be treated as proof of real-world EEG accuracy.

---

*Echoes of the Force* is an unofficial, Star Wars-inspired fan prototype. It is not affiliated with or endorsed by Lucasfilm or Disney. Any public or commercial use of protected Star Wars intellectual property would require appropriate permissions.
