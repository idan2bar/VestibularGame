// MediaPipe Face Landmarker bridge. Independent of Unity: Unity polls window.FaceTrackerBridge.
// Buffer layout must match FaceBufferLayout in Assets/Scripts/FaceTracking/FaceFrame.cs.

const MP_VERSION = "1.0.1";
const MP_BASE = `https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@${MP_VERSION}`;
const MP_MODULE_URL = `${MP_BASE}/vision_bundle.mjs`;
const MP_WASM_URL = `${MP_BASE}/wasm`;
const MODEL_URL =
  "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task";

const STATUS_IDLE = 0;
const STATUS_STARTING = 1;
const STATUS_RUNNING = 2;
const STATUS_ERROR = 3;
const STATUS_UNSUPPORTED = 4;

const LANDMARK_COUNT = 478;
const BLENDSHAPE_COUNT = 52;
const OFF_HAS_FACE = 0;
const OFF_TIMESTAMP = 1;
const OFF_FRAME_ID = 2;
const OFF_WIDTH = 3;
const OFF_HEIGHT = 4;
const OFF_MATRIX = 5;
const OFF_BLENDSHAPES = 21;
const OFF_LANDMARKS = 73;
const BUFFER_LENGTH = OFF_LANDMARKS + LANDMARK_COUNT * 3; // 1507

const MAX_CONSECUTIVE_ERRORS = 30;

const video = document.getElementById("ft-video");

let mp = null;              // { FaceLandmarker, FilesetResolver }
let vision = null;          // WasmFileset
let landmarker = null;
let delegate = null;
let stream = null;
let running = false;
let startPromise = null;
let session = 0;            // incremented by stop() to cancel an in-flight start()
let lastVideoTime = -1;
let lastTimestamp = 0;
let consecutiveErrors = 0;
let rvfcHandle = 0;
let rafHandle = 0;

function setError(message) {
  bridge.error = message;
  bridge.status = STATUS_ERROR;
}

function describeError(e, fallback) {
  const detail = e && (e.message || e.name) ? ` (${e.message || e.name})` : "";
  return fallback + detail;
}

function describeCameraError(e) {
  switch (e && e.name) {
    case "NotAllowedError":
    case "PermissionDeniedError":
    case "SecurityError":
      return "Camera permission was denied. Allow camera access in the browser and try again.";
    case "NotFoundError":
    case "DevicesNotFoundError":
    case "OverconstrainedError":
      return "No camera was found on this device.";
    case "NotReadableError":
    case "TrackStartError":
      return "The camera is in use by another application or could not be started.";
    case "AbortError":
      return "Camera start was interrupted.";
    default:
      return describeError(e, "Could not access the camera");
  }
}

async function loadMediaPipe() {
  if (mp) return;
  try {
    mp = await import(MP_MODULE_URL);
    vision = await mp.FilesetResolver.forVisionTasks(MP_WASM_URL);
  } catch (e) {
    mp = null;
    vision = null;
    throw new Error(describeError(e, "Failed to load the face tracking library"));
  }
}

function createLandmarker(delegateName) {
  return mp.FaceLandmarker.createFromOptions(vision, {
    baseOptions: { modelAssetPath: MODEL_URL, delegate: delegateName },
    runningMode: "VIDEO",
    numFaces: 1,
    outputFaceBlendshapes: true,
    outputFacialTransformationMatrixes: true,
  });
}

async function ensureLandmarker() {
  if (landmarker) return;
  await loadMediaPipe();
  try {
    landmarker = await createLandmarker("GPU");
    delegate = "GPU";
  } catch (gpuError) {
    console.warn("FaceTracker: GPU delegate failed, falling back to CPU.", gpuError);
    try {
      landmarker = await createLandmarker("CPU");
      delegate = "CPU";
    } catch (cpuError) {
      throw new Error(describeError(cpuError, "Failed to load the face tracking model"));
    }
  }
}

function stopStream() {
  if (stream) {
    for (const track of stream.getTracks()) track.stop();
    stream = null;
  }
  video.srcObject = null;
}

function cancelLoop() {
  running = false;
  if (rvfcHandle && video.cancelVideoFrameCallback) video.cancelVideoFrameCallback(rvfcHandle);
  if (rafHandle) cancelAnimationFrame(rafHandle);
  rvfcHandle = 0;
  rafHandle = 0;
}

function scheduleNext() {
  if (!running) return;
  if (typeof video.requestVideoFrameCallback === "function") {
    rvfcHandle = video.requestVideoFrameCallback(onFrame);
  } else {
    rafHandle = requestAnimationFrame(onFrame);
  }
}

function onFrame() {
  rvfcHandle = 0;
  rafHandle = 0;
  if (!running) return;

  if (video.readyState >= 2 && video.currentTime !== lastVideoTime) {
    lastVideoTime = video.currentTime;
    try {
      // VIDEO mode requires strictly increasing timestamps.
      const ts = Math.max(performance.now(), lastTimestamp + 1);
      lastTimestamp = ts;
      writeResult(landmarker.detectForVideo(video, ts), ts);
      consecutiveErrors = 0;
    } catch (e) {
      consecutiveErrors++;
      console.warn("FaceTracker: detectForVideo failed.", e);
      if (delegate === "GPU" && consecutiveErrors >= 3) {
        recoverWithCpu();
        return;
      }
      if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) {
        cancelLoop();
        setError(describeError(e, "Face tracking failed"));
        return;
      }
    }
  }
  scheduleNext();
}

async function recoverWithCpu() {
  const mySession = session;
  cancelLoop();
  try {
    const old = landmarker;
    landmarker = null;
    try { old.close(); } catch (_) { /* ignore */ }
    landmarker = await createLandmarker("CPU");
    delegate = "CPU";
    consecutiveErrors = 0;
    if (mySession !== session) {
      return;
    }
    running = true;
    scheduleNext();
  } catch (e) {
    if (mySession === session) setError(describeError(e, "Face tracking failed"));
  }
}

function writeResult(result, ts) {
  const out = bridge.latest;
  const landmarks = result.faceLandmarks && result.faceLandmarks[0];

  out[OFF_TIMESTAMP] = ts;
  out[OFF_FRAME_ID] = ++bridge.frameId;
  out[OFF_WIDTH] = video.videoWidth;
  out[OFF_HEIGHT] = video.videoHeight;

  if (!landmarks || landmarks.length === 0) {
    out[OFF_HAS_FACE] = 0;
    return;
  }
  out[OFF_HAS_FACE] = 1;

  const matrix = result.facialTransformationMatrixes && result.facialTransformationMatrixes[0];
  if (matrix && matrix.data) {
    for (let i = 0; i < 16; i++) out[OFF_MATRIX + i] = matrix.data[i];
  }

  const categories =
    result.faceBlendshapes && result.faceBlendshapes[0] && result.faceBlendshapes[0].categories;
  if (categories) {
    for (let i = 0; i < categories.length; i++) {
      const c = categories[i];
      if (c.index >= 0 && c.index < BLENDSHAPE_COUNT) out[OFF_BLENDSHAPES + c.index] = c.score;
    }
  }

  const n = Math.min(landmarks.length, LANDMARK_COUNT);
  let o = OFF_LANDMARKS;
  for (let i = 0; i < n; i++, o += 3) {
    const l = landmarks[i];
    out[o] = l.x;
    out[o + 1] = l.y;
    out[o + 2] = l.z;
  }
}

function waitForPlayback() {
  return new Promise((resolve, reject) => {
    const done = () => { cleanup(); resolve(); };
    const fail = () => { cleanup(); reject(new Error("The camera stream could not be played.")); };
    const cleanup = () => {
      video.removeEventListener("loadeddata", onReady);
      video.removeEventListener("error", fail);
    };
    const onReady = () => {
      video.play().then(done, (e) => { cleanup(); reject(e); });
    };
    video.addEventListener("error", fail);
    if (video.readyState >= 2) onReady();
    else video.addEventListener("loadeddata", onReady);
  });
}

async function doStart(mySession, width, height) {
  if (!window.isSecureContext) {
    throw new Error("Camera access requires a secure context (HTTPS or localhost).");
  }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    bridge.status = STATUS_UNSUPPORTED;
    throw new Error("This browser does not support camera access.");
  }
  if (typeof WebAssembly === "undefined") {
    bridge.status = STATUS_UNSUPPORTED;
    throw new Error("This browser does not support WebAssembly.");
  }

  try {
    stream = await navigator.mediaDevices.getUserMedia({
      video: {
        facingMode: "user",
        width: { ideal: width },
        height: { ideal: height },
        frameRate: { ideal: 30 },
      },
      audio: false,
    });
  } catch (e) {
    throw new Error(describeCameraError(e));
  }
  if (mySession !== session) { stopStream(); return; }

  const videoTrack = stream.getVideoTracks()[0];
  if (videoTrack) {
    const settings = videoTrack.getSettings();
    console.log(`FaceTracker: camera running at ${settings.width}x${settings.height} (requested ${width}x${height}).`);
  }

  video.srcObject = stream;
  try {
    await waitForPlayback();
  } catch (e) {
    throw new Error(describeCameraError(e));
  }
  if (mySession !== session) { stopStream(); return; }

  await ensureLandmarker();
  if (mySession !== session) { stopStream(); return; }

  lastVideoTime = -1;
  consecutiveErrors = 0;
  running = true;
  bridge.status = STATUS_RUNNING;
  scheduleNext();
}

const bridge = {
  status: STATUS_IDLE,
  error: "",
  frameId: 0,
  latest: new Float32Array(BUFFER_LENGTH),

  start(width = 1280, height = 720) {
    if (startPromise) return startPromise;
    if (bridge.status === STATUS_RUNNING) return Promise.resolve();

    const mySession = ++session;
    bridge.error = "";
    bridge.status = STATUS_STARTING;
    const promise = doStart(mySession, width, height)
      .catch((e) => {
        if (mySession !== session) return;
        console.error("FaceTracker: start failed.", e);
        cancelLoop();
        stopStream();
        const unsupported = bridge.status === STATUS_UNSUPPORTED;
        bridge.error = (e && e.message) || String(e);
        bridge.status = unsupported ? STATUS_UNSUPPORTED : STATUS_ERROR;
      })
      .finally(() => { if (startPromise === promise) startPromise = null; });
    startPromise = promise;
    return promise;
  },

  stop() {
    session++;
    cancelLoop();
    stopStream();
    startPromise = null;
    bridge.status = STATUS_IDLE;
    bridge.error = "";
  },

  setPreviewVisible(visible) {
    video.classList.toggle("ft-visible", !!visible);
  },
};

window.FaceTrackerBridge = bridge;
