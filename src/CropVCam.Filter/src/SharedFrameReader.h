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

  // Reads the current frame's width/height from the shared header without
  // waiting for a new frame or copying pixel data. Used at format
  // negotiation time (GetMediaType/GetStreamCaps/DecideBufferSize), which
  // happens before the streaming thread (and its WaitAndCopyFrame calls)
  // exists. Returns false if the writer hasn't started yet, the mutex is
  // momentarily held by the writer, or the header doesn't look like a valid
  // frame.
  bool TryPeekFrameSize(int* outWidth, int* outHeight);

  // Waits up to timeoutMs (total, across both the "new frame" event and the
  // mutex acquire) for a new frame. Only a frame matching expectedWidth/
  // expectedHeight - the format this connection negotiated via
  // TryPeekFrameSize - is accepted; a frame of a different size (e.g. the
  // physical camera changed after this pin already connected) is silently
  // dropped rather than delivered, since the pin's format was already
  // committed to the downstream consumer. On success copies it into
  // destBuffer, which must be at least expectedWidth * expectedHeight * 3 bytes.
  bool WaitAndCopyFrame(BYTE* destBuffer, long destCapacityBytes, DWORD timeoutMs, int expectedWidth,
                        int expectedHeight);

 private:
  bool EnsureOpen();
  void Close();

  // Reads magic/width/height/stride out of the header and validates them
  // against the one shape we accept - width/height within [1, kMaxWidth]/
  // [1, kMaxHeight] and stride == width * 3 - rather than trusting them
  // enough to multiply into a copy length. The header is untrusted: any
  // process in this login session could have created this shared section.
  bool TryReadValidHeader(int* outWidth, int* outHeight);

  HANDLE mapping_ = nullptr;
  HANDLE mutex_ = nullptr;
  HANDLE readyEvent_ = nullptr;
  BYTE* view_ = nullptr;
};

}  // namespace CropVCam
