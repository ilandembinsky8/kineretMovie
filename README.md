# kineretMovie

Click-through video presentation player for the Kineret exhibition, built in Unity 6 (6000.6.0f1).

The movie is a sequence of video clips. Each click plays the next clip. When a clip ends it holds its last frame (or loops, e.g. the opening) until the next click. After the last clip, the next click goes back to the start.

## Controls

The presentation clicker sends PageDown / PageUp.

| Action | Input |
|---|---|
| Next clip | Left mouse, Right Arrow, PageDown |
| Previous clip | Right mouse, Left Arrow, PageUp |
| Next scene | Period (`.`), only when `Movie.json` uses scenes |
| Restart from the opening | Space, or F5 while Period is held (re-reads the config and JSON) |

A click in the middle of a clip skips to the next one right away.

## Config

`KineretMovieProject/Assets/StreamingAssets/KineretMovieConfig.ini`
In a build: `<Game>_Data/StreamingAssets/KineretMovieConfig.ini`

```ini
MoviesPath=C:\media\KineretMovieProject
VideoFolder=Part 01 - finals
```

| Key | Meaning |
|---|---|
| `MoviesPath` | Folder that holds the video folders. Empty = `StreamingAssets`. A relative path is relative to `StreamingAssets`. |
| `VideoFolder` | Which video folder inside `MoviesPath` to play. |

To switch the movie, change `VideoFolder` and restart the app or press Space. No rebuild needed.

## Video folders

Videos are not in the repo. Each video folder holds the `.mp4` clips and a `Movie.json`:

```
C:\media\KineretMovieProject\
  Part 01 - finals\
  Part 01 - Eng - Final\
  part 02 finals\
  part 02-Eng-finals\
```

### Movie.json

Lists the clips in play order. This project uses a flat list:

```json
{
  "movies": [
    { "movName": "Opening", "loopAtTheEnd": true,  "loopStartTime": "00:00:00:00" },
    { "movName": "02",      "loopAtTheEnd": false, "loopStartTime": "00:00:00:00" }
  ]
}
```

| Field | Meaning |
|---|---|
| `movName` | Clip file name without `.mp4`. Must match exactly. |
| `loopAtTheEnd` | `true` = loop until the next click. `false` = hold the last frame. |
| `loopStartTime` | Where a loop jumps back to, `hh:mm:ss:ms`. `00:00:00:00` = seamless loop from the start. |

Grouping clips into scenes is also supported (not used here). Replace `movies` with `scenes`; Period then jumps to the first clip of the next scene:

```json
{
  "scenes": [
    { "sceneName": "Opening", "movies": [ { "movName": "Opening", "loopAtTheEnd": true } ] },
    { "sceneName": "06", "movies": [ { "movName": "06-01" }, { "movName": "06-02" } ] }
  ]
}
```

Clips play in JSON order. A clip listed in the JSON but missing on disk is skipped and logged. Files not listed in the JSON are ignored.

To add a new movie: create a folder in `MoviesPath`, put the clips and a `Movie.json` in it, and set `VideoFolder` to its name.

## Video format

H.264 `.mp4`. The current clips are 1920x1080, 25 fps. Resolution is read from each clip, so 4K clips play at 4K with no settings change. Avoid H.265/HEVC unless the playback PC has the HEVC Video Extensions installed.

## Scene setup

In `KineretMovieProject/Assets/Scenes`:

- Main Camera: Solid Color background, black.
- Canvas (Screen Space - Overlay, Canvas Scaler: Scale With Screen Size).
- A RawImage stretched to fill the Canvas, with a **Video Player** and a **VideoHandler** component on it.
- VideoHandler fields: **Video Player** and **Screen** (the RawImage).

The script sets the Video Player's options itself (URL source, render texture, looping) and creates the render texture at the clip's resolution, so no render texture asset is needed.

## Scripts

`KineretMovieProject/Assets/Scripts`

| Script | Role |
|---|---|
| `VideoHandler.cs` | Reads the config and JSON, builds the playlist, handles input, plays and loops clips. |
| `VideoDataReaderFromJson.cs` | Loads `Movie.json`. |
| `KineretConfigReader.cs` | Reads `key=value` lines from the `.ini`. |

The project uses the new Input System only (`Active Input Handling: Input System Package`).

## Troubleshooting

Errors go to the Unity Console, or in a build to `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log`.

| Message | Fix |
|---|---|
| `Config file not found` | The `.ini` is missing from `StreamingAssets`. |
| `Video folder not found` | Check `MoviesPath` and `VideoFolder` spelling. |
| `Show JSON not found` | The video folder has no `Movie.json`. |
| `Video listed in JSON not found` | `movName` doesn't match a file name. |
| `Video error` | Clip can't be decoded (codec not supported on this PC). |
