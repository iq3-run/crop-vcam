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

// The virtual camera negotiates a single fixed output format so the
// DirectShow pin connection never has to renegotiate mid-stream. The app
// always resizes its cropped frame to fill this canvas before writing it,
// regardless of the physical camera's native resolution.
constexpr int kOutputWidth = 1280;
constexpr int kOutputHeight = 720;
constexpr long long kOutputPayloadBytes = static_cast<long long>(kOutputWidth) * kOutputHeight * 3;
constexpr long long kSharedRegionBytes = kHeaderSize + kOutputPayloadBytes;

inline const wchar_t* MapName() { return L"Local\\CropVCam_Data"; }
inline const wchar_t* MutexName() { return L"Local\\CropVCam_Mutex"; }
inline const wchar_t* ReadyEventName() { return L"Local\\CropVCam_Ready"; }

}  // namespace CropVCam
