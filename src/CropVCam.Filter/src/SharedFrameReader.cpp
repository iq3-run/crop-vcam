#include "SharedFrameReader.h"

#include <cstring>

#include "SharedFrameProtocol.h"

namespace {
// How long TryPeekFrameSize waits for the writer's mutex before giving up -
// this runs during format negotiation, not the streaming loop, so it must
// not block the DirectShow graph-building thread for long.
constexpr DWORD kPeekMutexWaitMs = 20;
}  // namespace

namespace CropVCam {

SharedFrameReader::~SharedFrameReader() { Close(); }

void SharedFrameReader::Close() {
  if (view_) {
    UnmapViewOfFile(view_);
    view_ = nullptr;
  }
  if (mapping_) {
    CloseHandle(mapping_);
    mapping_ = nullptr;
  }
  if (mutex_) {
    CloseHandle(mutex_);
    mutex_ = nullptr;
  }
  if (readyEvent_) {
    CloseHandle(readyEvent_);
    readyEvent_ = nullptr;
  }
}

bool SharedFrameReader::EnsureOpen() {
  if (view_) {
    return true;
  }

  // The writer (CropVCam.App) may not have started yet; failing to open
  // here just means "no frame available right now", not an error.
  mapping_ = OpenFileMappingW(FILE_MAP_READ, FALSE, MapName());
  if (!mapping_) {
    return false;
  }

  view_ = static_cast<BYTE*>(
      MapViewOfFile(mapping_, FILE_MAP_READ, 0, 0, static_cast<SIZE_T>(kSharedRegionBytes)));
  // MUTEX_MODIFY_STATE is required to call ReleaseMutex below - SYNCHRONIZE
  // alone is only enough to wait on it.
  mutex_ = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, MutexName());
  readyEvent_ = OpenEventW(SYNCHRONIZE, FALSE, ReadyEventName());

  if (!view_ || !mutex_ || !readyEvent_) {
    Close();
    return false;
  }

  return true;
}

bool SharedFrameReader::TryReadValidHeader(int* outWidth, int* outHeight) {
  const auto magic = *reinterpret_cast<const unsigned long*>(view_ + 0);
  const int width = *reinterpret_cast<const int*>(view_ + 4);
  const int height = *reinterpret_cast<const int*>(view_ + 8);
  const int stride = *reinterpret_cast<const int*>(view_ + 12);

  if (magic != kFrameMagic || width < 1 || width > kMaxWidth || height < 1 || height > kMaxHeight ||
      stride != width * 3) {
    return false;
  }

  *outWidth = width;
  *outHeight = height;
  return true;
}

bool SharedFrameReader::TryPeekFrameSize(int* outWidth, int* outHeight) {
  if (!EnsureOpen()) {
    return false;
  }

  const DWORD waitResult = WaitForSingleObject(mutex_, kPeekMutexWaitMs);
  if (waitResult != WAIT_OBJECT_0 && waitResult != WAIT_ABANDONED) {
    return false;  // writer is mid-write right now; the caller will ask again
  }

  const bool valid = TryReadValidHeader(outWidth, outHeight);
  ReleaseMutex(mutex_);
  return valid;
}

bool SharedFrameReader::WaitAndCopyFrame(BYTE* destBuffer, long destCapacityBytes, DWORD timeoutMs,
                                          int expectedWidth, int expectedHeight) {
  if (!EnsureOpen()) {
    return false;
  }

  const long long expectedPayloadBytes = static_cast<long long>(expectedWidth) * expectedHeight * 3;
  if (destCapacityBytes < expectedPayloadBytes) {
    return false;
  }

  const ULONGLONG deadline = GetTickCount64() + timeoutMs;
  if (WaitForSingleObject(readyEvent_, timeoutMs) != WAIT_OBJECT_0) {
    return false;
  }

  const ULONGLONG now = GetTickCount64();
  const DWORD remainingMs = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
  const DWORD mutexWait = WaitForSingleObject(mutex_, remainingMs);
  if (mutexWait != WAIT_OBJECT_0 && mutexWait != WAIT_ABANDONED) {
    return false;
  }
  // WAIT_ABANDONED: the previous owner (almost certainly CropVCam.App,
  // possibly mid-write) died while holding this mutex. We still get
  // ownership - the frame data underneath may be stale/torn, so the magic
  // check below is what actually decides whether to trust it, and we make
  // sure to release the mutex before returning either way so it doesn't
  // stay stuck forever once the writer restarts.

  bool copied = false;
  int width = 0;
  int height = 0;
  if (TryReadValidHeader(&width, &height) && width == expectedWidth && height == expectedHeight) {
    std::memcpy(destBuffer, view_ + kHeaderSize, static_cast<size_t>(expectedPayloadBytes));
    copied = true;
  }

  ReleaseMutex(mutex_);
  return copied;
}

}  // namespace CropVCam
