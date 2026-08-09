#include "SharedFrameReader.h"

#include <cstring>

#include "SharedFrameProtocol.h"

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
  mutex_ = OpenMutexW(SYNCHRONIZE, FALSE, MutexName());
  readyEvent_ = OpenEventW(SYNCHRONIZE, FALSE, ReadyEventName());

  if (!view_ || !mutex_ || !readyEvent_) {
    Close();
    return false;
  }

  return true;
}

bool SharedFrameReader::WaitAndCopyFrame(BYTE* destBuffer, long destCapacityBytes, DWORD timeoutMs,
                                          int* outWidth, int* outHeight, int* outStrideBytes) {
  if (!EnsureOpen()) {
    return false;
  }

  if (WaitForSingleObject(readyEvent_, timeoutMs) != WAIT_OBJECT_0) {
    return false;
  }
  if (WaitForSingleObject(mutex_, timeoutMs) != WAIT_OBJECT_0) {
    return false;
  }

  bool copied = false;
  const auto magic = *reinterpret_cast<const unsigned long*>(view_ + 0);
  if (magic == kFrameMagic) {
    const int width = *reinterpret_cast<const int*>(view_ + 4);
    const int height = *reinterpret_cast<const int*>(view_ + 8);
    const int stride = *reinterpret_cast<const int*>(view_ + 12);
    const long payloadBytes = static_cast<long>(stride) * height;

    if (payloadBytes > 0 && payloadBytes <= destCapacityBytes && payloadBytes <= kMaxPayloadBytes) {
      std::memcpy(destBuffer, view_ + kHeaderSize, static_cast<size_t>(payloadBytes));
      *outWidth = width;
      *outHeight = height;
      *outStrideBytes = stride;
      copied = true;
    }
  }

  ReleaseMutex(mutex_);
  return copied;
}

}  // namespace CropVCam
