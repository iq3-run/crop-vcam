#pragma once

// Layout shared with the C# writer side:
// src/CropVCam.App/VirtualCamera/SharedFrameProtocol.cs
// Keep the two definitions in sync when changing anything here - in
// particular kSharedRegionBytes MUST match exactly, since
// MemoryMappedFile.CreateOrOpen (C#) fixes the underlying section's size
// and MapViewOfFile (C++) cannot map a view larger than that section.
namespace CropVCam {

// "CVC1" as a little-endian uint32.
constexpr unsigned long kFrameMagic = 0x31435643UL;

constexpr int kPixelFormatBgr24 = 1;

// Header layout (bytes, little-endian):
//   0  uint32 magic
//   4  int32  width
//   8  int32  height
//  12  int32  strideBytes
//  16  int32  pixelFormat
//  20  uint64 sequence
//  28  (4 bytes reserved/padding)
constexpr int kHeaderSize = 32;

// Upper bound the shared region is sized for. The app writes frames sized to
// the physical camera's own resolution (clamped to this if a camera exceeds
// it) rather than a fixed canvas - see CropVCam.App's MainViewModel.OnFrameCaptured.
// Must stay in lockstep with MaxWidth/MaxHeight on the C# side: the region
// size is fixed at creation time and cannot be resized later, and a past
// drift between the two sides' sizes made the filter unable to open the
// mapping at all.
constexpr int kMaxWidth = 3840;
constexpr int kMaxHeight = 2160;
constexpr long long kMaxPayloadBytes = static_cast<long long>(kMaxWidth) * kMaxHeight * 3;
constexpr long long kSharedRegionBytes = kHeaderSize + kMaxPayloadBytes;

// The virtual camera negotiates a single fixed output format per pin
// connection (DirectShow doesn't renegotiate mid-stream) - see
// CCropVCamStream::EnsureFormatResolved. This is what it reports when
// queried before any real frame has been observed in shared memory (e.g.
// CropVCam.App has never run yet).
constexpr int kDefaultWidth = 1280;
constexpr int kDefaultHeight = 720;

inline const wchar_t* MapName() { return L"Local\\CropVCam_Data"; }
inline const wchar_t* MutexName() { return L"Local\\CropVCam_Mutex"; }
inline const wchar_t* ReadyEventName() { return L"Local\\CropVCam_Ready"; }

}  // namespace CropVCam
