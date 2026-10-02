# VR Video Game History Museum

**Group 5 · Assignment 2**

A virtual reality museum for the Meta Quest 3 that walks visitors through the history of home video game consoles. Explore ten galleries in chronological order, from the Magnavox Odyssey to the PlayStation 5.

## Exhibits

| Gallery | Console |
|---|---|
| 01 | Magnavox Odyssey |
| 02 | Atari 2600 |
| 03 | Nintendo Entertainment System |
| 04 | Philips CD-i |
| 05 | PlayStation 2 |
| 06 | Nintendo GameCube |
| 07 | Xbox 360 |
| 08 | Nintendo Wii |
| 09 | Xbox One |
| 10 | PlayStation 5 |

## Features

- **Museum Passport:** a checklist that tracks which exhibits you've visited. A popup appears when you reach a new one, and an award trophy is shown when the museum is complete (`ExhibitTracker.cs`).
- **Proximity displays:** each console appears on its plinth only when you walk up to it (`ProximityAppearance.cs`).
- **Animation screens:** video screens play console intro animations next to their exhibits.
- **VR locomotion:** walk the museum using the Meta Interaction SDK rig with teleport and smooth movement.

## Requirements

- Unity **6000.3.22f1** (Unity 6) with **Android Build Support**
- Meta Quest 3 (or Quest Link for testing in the editor)
- Key packages (installed automatically from `Packages/manifest.json`):
  - Meta XR Interaction SDK (`com.meta.xr.sdk.interaction.ovr`)
  - OpenXR Plugin
  - Universal Render Pipeline (URP)

## Getting Started

1. Clone the repository:
   ```
   git clone https://github.com/wdm5764/vr461-Assignment02.git
   ```
2. Open the project folder in Unity Hub using version 6000.3.22f1.
3. Open the scene `Assets/Scenes/Museum.unity`.
4. **Play in editor:** connect your Quest 3 with Quest Link, then press Play.
5. **Build to headset:** go to *File → Build Profiles*, switch to **Android**, connect the Quest 3 over USB, and choose **Build and Run**.

## Project Structure

```
Assets/
├── Scenes/Museum.unity      # Main museum scene
├── Museum/Consoles/         # Console 3D models and intro videos
├── Images/, Materials/      # Exhibit images and materials
├── Text/                    # Gallery sign prefab
├── ExhibitTracker.cs        # Museum Passport / visit tracking
└── ProximityAppearance.cs   # Shows exhibits when the player is nearby
```

## Credits

Third-party 3D models are licensed under CC Attribution 4.0. See [Assets/Museum/Consoles/CREDITS.md](Assets/Museum/Consoles/CREDITS.md) for the full list.
