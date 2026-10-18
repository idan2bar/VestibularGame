// Thin wrappers over window.FaceTrackerBridge (see WebGLTemplates/FaceTracking/facetracker.js).
mergeInto(LibraryManager.library, {
  FaceTracker_Start: function (width, height) {
    var b = window.FaceTrackerBridge;
    if (b) b.start(width, height);
  },

  FaceTracker_Stop: function () {
    var b = window.FaceTrackerBridge;
    if (b) b.stop();
  },

  FaceTracker_GetStatus: function () {
    var b = window.FaceTrackerBridge;
    return b ? b.status : 4;
  },

  FaceTracker_SetPreviewVisible: function (visible) {
    var b = window.FaceTrackerBridge;
    if (b) b.setPreviewVisible(visible !== 0);
  },

  FaceTracker_GetError: function () {
    var b = window.FaceTrackerBridge;
    var msg = b ? (b.error || "") : "FaceTrackerBridge is not available (missing WebGL template).";
    var size = lengthBytesUTF8(msg) + 1;
    var buffer = _malloc(size);
    stringToUTF8(msg, buffer, size);
    return buffer;
  },

  FaceTracker_CopyLatest: function (dst, length) {
    var b = window.FaceTrackerBridge;
    if (!b || !b.latest || b.frameId <= 0) return -1;
    var n = Math.min(length, b.latest.length);
    HEAPF32.set(b.latest.subarray(0, n), dst >> 2);
    return b.frameId;
  }
});
