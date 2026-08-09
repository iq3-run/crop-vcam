#pragma once

#include <windows.h>

namespace CropVCam {

// Opens (lazily, retrying each call since the writer process may not have
// started yet) the shared memory the CropVCam.App process writes cropped
// frames into, and copies out the newest one.
class SharedFrameReader {
 public:
  SharedFrameReader() = default;
  ~SharedFrameReader();

  SharedFrameReader(const SharedFrameReader&) = delete;
  SharedFrameReader& operator=(const SharedFrameReader&) = delete;

  // Waits up to timeoutMs (total, across both the "new frame" event and the
  // mutex acquire) for a new frame. On success copies it into destBuffer
  // (which must be at least kOutputPayloadBytes) and fills in the frame's
  // dimensions/stride. Returns false on timeout or if the writer hasn't
  // started yet.
  bool WaitAndCopyFrame(BYTE* destBuffer, long destCapacityBytes, DWORD timeoutMs,
                        int* outWidth, int* outHeight, int* outStrideBytes);

 private:
  bool EnsureOpen();
  void Close();

  HANDLE mapping_ = nullptr;
  HANDLE mutex_ = nullptr;
  HANDLE readyEvent_ = nullptr;
  BYTE* view_ = nullptr;
};

}  // namespace CropVCam
