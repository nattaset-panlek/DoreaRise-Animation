# DoreaRise Animation

Unity 6 URP cinematic-animation prototype.

## Open the project

1. Install **Unity 6000.4.8f1** (the exact version, via Unity Hub).
2. Install **Git LFS** once: `git lfs install`
   - Models/textures (`.fbx`, `.png`, `.clip`, ...) are stored in Git LFS.
   - **Do not use "Download ZIP" on GitHub** — the ZIP contains only LFS pointer files, so every model and texture will be broken.
3. Clone:
   ```
   git clone https://github.com/nattaset-panlek/DoreaRise-Animation.git
   ```
   If you already cloned without LFS, run inside the folder: `git lfs pull`
4. Open the folder in Unity Hub (Add → project folder). The first import takes a few minutes.
5. Main scene: **`Assets/Scenes/SampleScene.unity`** (opens automatically on first load). Press Play.

Make sure the drive holding your Unity cache/temp (usually C:) has a few GB free, or the import will fail.
