# CSE165 Project 2: Hand-Tracked VR Drone Race

> **Note:** This repository is a fork of the original group project repository that I contributed to as part of a team. The fork is maintained here to showcase the project and my work on it.

This Unity project is a VR drone racing experience built for CSE 165. The player pilots a drone through a generated checkpoint course over a Machu Picchu environment using hand tracking gestures. The race system loads checkpoint coordinates from `.xyz` track files, builds the course at runtime, times the run, handles crashes and respawns, and saves a best-run ghost racer.

## Project Overview

- Hand-tracked drone movement using Unity XR Hands.
- Runtime race course generation from `.xyz` checkpoint files.
- Machu Picchu scene asset used as the race environment.
- Countdown, race timer, checkpoint progress, best time, and distance HUD.
- Crash detection with respawn at the last cleared checkpoint.
- Procedural engine and race sound effects.
- Ghost champion mode that records the best run and replays it on future attempts.
- Multiple view modes: pilot, cockpit, and chase camera.

## Requirements

- Unity `6000.4.1f1`
- Android build support for deployment to Meta Quest
- Meta Quest headset with hand tracking enabled
- Unity packages are managed through `Packages/manifest.json`

Important packages include:

- Meta XR SDK `201.0.0`
- Unity XR Hands `1.7.3`
- XR Interaction Toolkit `3.4.1`
- XR Management `4.6.0`
- OpenXR `1.16.1`
- Input System `1.19.0`

## Running the Project

1. Open the repository root in Unity Hub.
2. Use Unity version `6000.4.1f1`.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Let Unity restore packages from `Packages/manifest.json`.
5. Connect a Meta Quest device with developer mode enabled.
6. Switch the build target to Android.
7. Build and run to the headset.

The project is configured for OpenXR and Meta Quest support. The race manager is created automatically at runtime if one is not already present in the scene.

## Controls

### Movement

- Right hand fist: move forward.
- Right hand fist with thumb up: move upward.
- Right hand fist with thumb down: move downward.
- Left thumb direction: rotate/yaw the drone.

### View Mode

Hold both hands open, palms up, and separated for about one second to cycle view modes:

1. Pilot view
2. Cockpit view
3. Chase view

### Restart

Press `R` in the editor or any build where the legacy input manager shortcut is available to restart the race.

## Track Files

Track files live in `Assets/StreamingAssets/`:

- `competition.xyz`
- `sample_track.xyz`

Each `.xyz` file contains one checkpoint per line:

```text
x y z
```

Example:

```text
4452.389 1869.995 -7471.981
4757.987 2061.995 -9101.977
4757.987 2645.993 -10835.97
```

The race loader looks for `competition.xyz` first, then `sample_track.xyz`, then other `.xyz` files in known locations such as StreamingAssets, persistent data, the project root, and external media. Track coordinates are interpreted as local coordinates relative to the `machu_picchu_2` object and converted to world space at runtime.

## Main Files

- `Assets/Scenes/SampleScene.unity`: main race scene.
- `Assets/Scripts/RaceTrackManager.cs`: loads tracks, generates checkpoints, manages timer, HUD, crashes, respawn, and race completion.
- `Assets/Scripts/RaceTrackBootstrap.cs`: ensures the race manager exists during play mode.
- `Assets/Scripts/Travel.cs`: hand-tracking drone movement and rotation.
- `Assets/Scripts/RaceCheckpoint.cs`: checkpoint visuals and state changes.
- `Assets/Scripts/DroneViewModeController.cs`: pilot, cockpit, and chase camera modes.
- `Assets/Scripts/DroneRaceAudio.cs`: generated engine, countdown, checkpoint, crash, and finish audio.
- `Assets/Scripts/GhostChampion.cs`: records and replays the best run.
- `Assets/Scripts/Parse.cs`: validates and parses `.xyz` track files.
- `Assets/machu_picchu/`: Machu Picchu model and textures.

## Gameplay Flow

1. The scene starts and the race manager finds the main camera/player rig.
2. The manager prepares the drone collision, movement, audio, view modes, and HUD.
3. The track file is loaded and converted into world-space checkpoints.
4. The player is spawned at checkpoint 1.
5. A 3-second countdown starts.
6. The player races through checkpoints in order.
7. Colliding with the environment respawns the player at the last cleared checkpoint.
8. Reaching the final checkpoint stops the timer and updates the best time.
9. Ghost champion mode saves the best run to `Application.persistentDataPath`.

## Repository Notes

This repository contains Unity project source files and assets. Unity-generated folders such as `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, and logs are excluded by `.gitignore`.

If generated build output folders appear locally, they do not need to be committed.
