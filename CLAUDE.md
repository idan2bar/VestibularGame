# VestibularGame

Unity 6000.6.0f1 (URP, 2D template). It uses the webcam to detect whether the player is **looking at the centre of the screen**. That is the only goal: accurate gaze at other screen points is not needed. WebGL is the main target, followed by mobile and PC.

## Working rules

- **Delegate implementation.** The user wants code changes split into small tasks, each run by a sonnet or haiku subagent with its own files. The main session designs the work, writes the briefs, coordinates and verifies (compile + tests).
- Commit only when asked. Don't add large native plugins: the user declined a ~290 MB MediaPipe native plugin.
- The user tests WebGL builds in the browser and reports numbers from the debug scene's plot (RMS, drift slope, r²).

## Layout

All code is in `Assets/Scripts/FaceTracking/`, namespace `VestibularGame.FaceTracking`, asmdef `VestibularGame.FaceTracking`.

| File | Role |
|---|---|
| `IFaceTracker.cs`, `FaceFrame.cs` | Tracker contract. `FaceFrame` holds 478 landmarks, 52 blendshapes and the facial transform matrix. `FaceBufferLayout` defines the flat 1507-float buffer shared with JS. |
| `WebGLFaceTracker.cs` | WebGL backend that polls the jslib. On every other platform it reports `Unsupported`. |
| `MockFaceTracker.cs` | Mouse-driven synthetic faces for the Editor and other platforms, in eye mode or head mode. Built with the forward eye model. |
| `EyeModel.cs` | Shared geometry: image space, head axes, the eyeball/iris sphere model, angle↔direction conversions. Used inversely by the estimator and forwardly by the mock and tests. |
| `GazeEstimator.cs` | `FaceFrame` → `GazeSample`. Contains the 3D eyeball gaze, One Euro filtering, centre calibration, head-movement calibration and on-centre hysteresis. Allocation-free per frame. |
| `GazeSample.cs` | Output: head/eye/gaze angles, `DeviationDeg`, `IsOnCenter`, `IsBlinking`, `IsCalibrated`. |
| `HeadPoseMath.cs`, `OneEuroFilter.cs` | Helpers. |
| `FaceTrackingController.cs` | MonoBehaviour that owns the tracker and estimator. It picks the WebGL tracker or the mock and requests 1280x720 from the camera. `CalibrateFull()` runs the centre calibration, then the head calibration. |
| `Debug/FaceTrackingDebugView.cs` | Runtime-built debug UI with the centre dot, readouts, calibration prompts, buttons and the error-vs-head scatter plot (`ScatterPlotGraphic`). |
| `Editor/FaceTrackingSceneCreator.cs` | Menu **Tools/Face Tracking/Create Debug Scene**, which (re)builds `Assets/Scenes/FaceTrackingDebug.unity`. |

WebGL side:
- `Assets/WebGLTemplates/FaceTracking/`: `index.html` + `facetracker.js`. These run MediaPipe Face Landmarker (tasks-vision 1.0.1 from jsDelivr, model from Google storage) on `getUserMedia`. The project's WebGL template is set to `PROJECT:FaceTracking`.
- `Assets/Plugins/WebGL/FaceTrackerBridge.jslib`: the `FaceTracker_*` functions, which call `window.FaceTrackerBridge` and copy the latest frame into a C# `float[]` via `HEAPF32`.

Tests are in `Assets/Tests/EditMode/GazeEstimatorTests.cs` (EditMode, NUnit). They use synthetic frames from the forward model, including an "imperfect tracker" with head-dependent gaze error.

## Conventions

- **Angles** are in degrees. **+yaw = the user's right, +pitch = up.**
- **MediaPipe landmarks** are normalized and unmirrored: y points down, and z is depth scaled like x.
- **Image space** (`EyeModel`) is P = (x·W, y·H, z·W), with x right, y down and z away from the camera. MediaPipe camera space maps to image space as (x, −y, −z).
- **Eyeball model:**
  - eye width = 2D distance between the eye corners;
  - eyeball centre = corner midpoint − head forward · 0.2·width;
  - the iris is lifted onto a sphere of radius 0.42·width;
  - gaze = the average of the two eyes' rays.

## Gaze pipeline and what we learned

1. Per frame, the estimator produces camera-relative gaze angles, which are One Euro filtered.
2. **Centre calibration** (2 s of looking at the dot) stores the baseline angles.
3. **Head-movement calibration** (~12 s, needs a head yaw range ≥ 16° and a pitch range ≥ 10°) works like this:
   - while the user keeps looking at the dot and turns/nods, it collects (head yaw, head pitch) → raw gaze;
   - it fits a quadratic in head pose per axis by ridge least squares, with iterative median-based outlier rejection;
   - it then reports gaze = raw − prediction, with head pose clamped to the collected range.
4. On-centre uses hysteresis: on when deviation < 5°, off when > 8°.

Findings from real browser data:
- **Uncorrected error is head-dependent.** Before head calibration, gaze error vs head angle was almost perfectly linear (r² ≈ 1). The slopes were about −1.26 for yaw and −0.63 for pitch, so a per-user head-movement regression is the right fix.
- **Don't use MediaPipe's depth.** An attempt to project gaze to a screen point using the head distance (matrix m23) made things worse: depth noise grows with head yaw, and the dot drifted down when the user turned their head. It was reverted. Keep gaze angular.
- **r² means nothing when the slope is ≈ 0.** After correction, judge the plot by error RMS and drift per degree instead.

## Building, testing and driving the Editor

A Unity Editor is usually open with the Pipeline package (`com.unity.pipeline`). Drive it with the `unity` CLI (add `--caller plugin --skill unity-cli`):

```bash
unity command recompile --caller plugin --skill unity-cli
unity command console_status --caller plugin --skill unity-cli
unity command run_tests --mode EditMode --caller plugin --skill unity-cli
```

- Main-thread commands time out ("Main thread operation timed out") when the Editor window isn't focused. Bring it to the front first. The Unity PID changes between launches; find it with `unity status`.
  ```bash
  powershell -NoProfile -Command "(New-Object -ComObject WScript.Shell).AppActivate(<unity-pid>)"
  ```
- `capture_game_view` grabs the whole Editor screen and isn't useful. Inspect the UI with `unity command eval` instead.
- The webcam path only works in a WebGL build served over http(s). In the Editor, the mock tracker is used.
