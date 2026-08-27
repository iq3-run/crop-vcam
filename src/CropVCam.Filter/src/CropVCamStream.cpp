#include "CropVCamStream.h"

#include <cstring>

#include "CropVCamFilter.h"  // full CCropVCamSource definition, needed for the static_cast below
#include "SharedFrameProtocol.h"

namespace {
// How long FillBuffer waits for a fresh frame before re-delivering the
// last one it has. Also caps how often we re-check when the app hasn't
// started streaming yet.
constexpr DWORD kWaitTimeoutMs = 100;
constexpr REFERENCE_TIME kUnitsPerSecond = 10'000'000;
constexpr int kTargetFps = 30;

// latestFrame_ is sized for the largest frame the protocol allows so it
// never needs reallocating once the pin's actual (smaller-or-equal) format
// is resolved.
constexpr long long kMaxFrameBufferBytes = static_cast<long long>(CropVCam::kMaxWidth) * CropVCam::kMaxHeight * 3;
}  // namespace

CCropVCamStream::CCropVCamStream(HRESULT* phr, CCropVCamSource* pParent, LPCWSTR pPinName)
    : CSourceStream(NAME("CropVCam Output Stream"), phr, static_cast<CSource*>(pParent), pPinName),
      latestFrame_(new BYTE[static_cast<size_t>(kMaxFrameBufferBytes)]),
      streamPosition_(0),
      frameLengthUnits_(kUnitsPerSecond / kTargetFps),
      width_(CropVCam::kDefaultWidth),
      height_(CropVCam::kDefaultHeight),
      formatResolved_(false) {
  std::memset(latestFrame_, 0, static_cast<size_t>(kMaxFrameBufferBytes));
}

CCropVCamStream::~CCropVCamStream() { delete[] latestFrame_; }

HRESULT CCropVCamStream::OnThreadCreate() {
  streamPosition_ = 0;
  return S_OK;
}

STDMETHODIMP CCropVCamStream::NonDelegatingQueryInterface(REFIID riid, void** ppv) {
  if (riid == IID_IKsPropertySet) {
    return GetInterface(static_cast<IKsPropertySet*>(this), ppv);
  }
  if (riid == IID_IAMStreamConfig) {
    return GetInterface(static_cast<IAMStreamConfig*>(this), ppv);
  }
  return CSourceStream::NonDelegatingQueryInterface(riid, ppv);
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::Set(REFGUID /*guidPropSet*/, DWORD /*dwPropID*/,
                                                LPVOID /*pInstanceData*/, DWORD /*cbInstanceData*/,
                                                LPVOID /*pPropData*/, DWORD /*cbPropData*/) {
  return E_NOTIMPL;  // we only need to answer "what category is this pin", never set one
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::Get(REFGUID guidPropSet, DWORD dwPropID, LPVOID /*pInstanceData*/,
                                                DWORD /*cbInstanceData*/, LPVOID pPropData, DWORD cbPropData,
                                                DWORD* pcbReturned) {
  CheckPointer(pcbReturned, E_POINTER);
  if (guidPropSet != AMPROPSETID_Pin || dwPropID != AMPROPERTY_PIN_CATEGORY) {
    return E_PROP_SET_UNSUPPORTED;
  }

  *pcbReturned = sizeof(GUID);
  if (pPropData == nullptr) {
    return S_OK;  // caller is only asking how large a buffer it needs
  }
  if (cbPropData < sizeof(GUID)) {
    return E_UNEXPECTED;
  }

  *static_cast<GUID*>(pPropData) = PIN_CATEGORY_CAPTURE;
  return S_OK;
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::QuerySupported(REFGUID guidPropSet, DWORD dwPropID, DWORD* pTypeSupport) {
  if (guidPropSet != AMPROPSETID_Pin || dwPropID != AMPROPERTY_PIN_CATEGORY) {
    return E_PROP_SET_UNSUPPORTED;
  }

  *pTypeSupport = KSPROPERTY_SUPPORT_GET;
  return S_OK;
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::SetFormat(AM_MEDIA_TYPE* pmt) {
  // Only one fixed format is ever offered (see GetStreamCaps), so accepting
  // a request for anything else would be a lie - reject it instead.
  CMediaType ourType;
  HRESULT hr = GetMediaType(&ourType);
  if (FAILED(hr)) {
    return hr;
  }

  return (pmt != nullptr && CMediaType(*pmt) == ourType) ? S_OK : E_INVALIDARG;
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::GetFormat(AM_MEDIA_TYPE** ppmt) {
  CheckPointer(ppmt, E_POINTER);

  CMediaType mt;
  HRESULT hr = GetMediaType(&mt);
  if (FAILED(hr)) {
    return hr;
  }

  *ppmt = CreateMediaType(&mt);
  return *ppmt != nullptr ? S_OK : E_OUTOFMEMORY;
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::GetNumberOfCapabilities(int* piCount, int* piSize) {
  CheckPointer(piCount, E_POINTER);
  CheckPointer(piSize, E_POINTER);

  *piCount = 1;
  *piSize = sizeof(VIDEO_STREAM_CONFIG_CAPS);
  return S_OK;
}

HRESULT STDMETHODCALLTYPE CCropVCamStream::GetStreamCaps(int iIndex, AM_MEDIA_TYPE** ppmt, BYTE* pSCC) {
  CheckPointer(ppmt, E_POINTER);
  CheckPointer(pSCC, E_POINTER);
  if (iIndex != 0) {
    return S_FALSE;  // only one capability, and the caller has already seen it
  }

  CMediaType mt;
  HRESULT hr = GetMediaType(&mt);
  if (FAILED(hr)) {
    return hr;
  }

  *ppmt = CreateMediaType(&mt);
  if (*ppmt == nullptr) {
    return E_OUTOFMEMORY;
  }

  auto* caps = reinterpret_cast<VIDEO_STREAM_CONFIG_CAPS*>(pSCC);
  ZeroMemory(caps, sizeof(VIDEO_STREAM_CONFIG_CAPS));
  caps->guid = FORMAT_VideoInfo;
  caps->InputSize.cx = width_;
  caps->InputSize.cy = height_;
  caps->MinCroppingSize = caps->InputSize;
  caps->MaxCroppingSize = caps->InputSize;
  caps->CropGranularityX = 1;
  caps->CropGranularityY = 1;
  caps->CropAlignX = 1;
  caps->CropAlignY = 1;
  caps->MinOutputSize = caps->InputSize;
  caps->MaxOutputSize = caps->InputSize;
  caps->OutputGranularityX = 1;
  caps->OutputGranularityY = 1;
  caps->MinFrameInterval = frameLengthUnits_;
  caps->MaxFrameInterval = frameLengthUnits_;
  // MinBitsPerSecond/MaxBitsPerSecond are LONG (32-bit); a 4K frame's bitrate
  // overflows that, so clamp rather than let the multiplication wrap. (Not
  // std::min: windows.h's min/max macros make that unusable here.)
  constexpr long long kMaxLong = 0x7FFFFFFFLL;
  const long long bitsPerSecond = static_cast<long long>(PaddedFrameBytes()) * 8 * kTargetFps;
  caps->MinBitsPerSecond = static_cast<LONG>(bitsPerSecond < kMaxLong ? bitsPerSecond : kMaxLong);
  caps->MaxBitsPerSecond = caps->MinBitsPerSecond;

  return S_OK;
}

// Resolves the pin's format from the physical camera's actual resolution
// (peeked out of shared memory) the first time it's available, then never
// again - DirectShow negotiates a pin's format once at connect time and
// doesn't support changing it mid-stream, so whatever's resolved here has to
// stay correct for the rest of this connection's lifetime. Until a real
// frame has been observed (e.g. CropVCam.App hasn't started yet), callers
// see the fallback default instead.
void CCropVCamStream::EnsureFormatResolved() {
  if (formatResolved_) {
    return;
  }

  int width = 0;
  int height = 0;
  if (reader_.TryPeekFrameSize(&width, &height)) {
    width_ = width;
    height_ = height;
    formatResolved_ = true;
  }
}

long CCropVCamStream::PackedRowBytes() const { return static_cast<long>(width_) * 3; }

// BI_RGB rows are padded to a 4-byte boundary per the DIB spec - a camera
// width that isn't a multiple of 4 needs more bytes per row here than the
// shared-memory payload (which stays tightly packed - see PackedRowBytes).
long CCropVCamStream::PaddedRowBytes() const { return (PackedRowBytes() + 3) & ~3; }

// Size of the packed copy read out of shared memory into latestFrame_ - not
// what's delivered to DirectShow, see PaddedFrameBytes for that.
long CCropVCamStream::PackedFrameBytes() const { return PackedRowBytes() * height_; }

// The actual frame size DirectShow sees: biSizeImage, allocator buffer size,
// bitrate, and SetActualDataLength all need the padded (not packed) size, or
// a non-multiple-of-4 width delivers an undersized/misaligned frame.
long CCropVCamStream::PaddedFrameBytes() const { return PaddedRowBytes() * height_; }

HRESULT CCropVCamStream::GetMediaType(CMediaType* pMediaType) {
  CheckPointer(pMediaType, E_POINTER);
  EnsureFormatResolved();

  VIDEOINFOHEADER vih;
  ZeroMemory(&vih, sizeof(vih));
  vih.AvgTimePerFrame = frameLengthUnits_;
  vih.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
  vih.bmiHeader.biWidth = width_;
  // Negative: top-down DIB. SharedFrameWriter (C#) writes rows top-down, so
  // this must stay negative or the delivered picture is vertically flipped.
  vih.bmiHeader.biHeight = -height_;
  vih.bmiHeader.biPlanes = 1;
  vih.bmiHeader.biBitCount = 24;
  vih.bmiHeader.biCompression = BI_RGB;
  vih.bmiHeader.biSizeImage = PaddedFrameBytes();

  pMediaType->SetType(&MEDIATYPE_Video);
  pMediaType->SetSubtype(&MEDIASUBTYPE_RGB24);
  pMediaType->SetFormatType(&FORMAT_VideoInfo);
  pMediaType->SetTemporalCompression(FALSE);
  pMediaType->SetSampleSize(vih.bmiHeader.biSizeImage);
  pMediaType->SetFormat(reinterpret_cast<BYTE*>(&vih), sizeof(VIDEOINFOHEADER));

  return S_OK;
}

HRESULT CCropVCamStream::DecideBufferSize(IMemAllocator* pAlloc, ALLOCATOR_PROPERTIES* pProperties) {
  CheckPointer(pAlloc, E_POINTER);
  CheckPointer(pProperties, E_POINTER);

  CAutoLock lock(m_pFilter->pStateLock());
  // Not EnsureFormatResolved() here: DirectShow always calls GetMediaType
  // (which resolves the format) before DecideBufferSize within one Connect()
  // cycle, so width_/height_ are already settled. Re-peeking here too would
  // open a window where a frame arriving between the two peeks could resolve
  // a different size in each, sizing this buffer for a format GetMediaType
  // never actually reported to the downstream consumer.
  pProperties->cBuffers = 2;
  pProperties->cbBuffer = PaddedFrameBytes();

  ALLOCATOR_PROPERTIES actual;
  HRESULT hr = pAlloc->SetProperties(pProperties, &actual);
  if (FAILED(hr)) {
    return hr;
  }

  return actual.cbBuffer < pProperties->cbBuffer ? E_FAIL : S_OK;
}

HRESULT CCropVCamStream::FillBuffer(IMediaSample* pSample) {
  CheckPointer(pSample, E_POINTER);

  BYTE* pData = nullptr;
  HRESULT hr = pSample->GetPointer(&pData);
  if (FAILED(hr)) {
    return hr;
  }

  const long paddedBytes = PaddedFrameBytes();
  if (pSample->GetSize() < paddedBytes) {
    return E_FAIL;
  }

  RefreshLatestFrame(PackedFrameBytes());
  CopyRowsWithPadding(pData);
  pSample->SetActualDataLength(paddedBytes);

  StampSampleTime(pSample);
  return S_OK;
}

// latestFrame_ holds the packed copy read out of shared memory (stride
// PackedRowBytes()); this expands it into pSample's buffer at the padded DIB
// stride DirectShow expects (PaddedRowBytes()), zero-filling each row's
// padding bytes rather than leaving them as whatever the allocator handed
// back. A no-op expansion (plain equal-size copy) when width_ is already a
// multiple of 4, since the two strides are then equal.
void CCropVCamStream::CopyRowsWithPadding(BYTE* dest) const {
  const long packedRowBytes = PackedRowBytes();
  const long paddedRowBytes = PaddedRowBytes();
  const long paddingBytes = paddedRowBytes - packedRowBytes;

  for (int row = 0; row < height_; ++row) {
    const BYTE* srcRow = latestFrame_ + static_cast<size_t>(row) * packedRowBytes;
    BYTE* destRow = dest + static_cast<size_t>(row) * paddedRowBytes;
    std::memcpy(destRow, srcRow, packedRowBytes);
    if (paddingBytes > 0) {
      std::memset(destRow + packedRowBytes, 0, paddingBytes);
    }
  }
}

void CCropVCamStream::RefreshLatestFrame(long frameBytes) {
  // If no fresh frame arrived (app not running yet, or a hiccup, or the
  // physical camera's resolution no longer matches what this pin negotiated),
  // latestFrame_ already holds the last one we have (or the initial black
  // frame) - WaitAndCopyFrame only touches the buffer when it has a real,
  // matching frame to hand over - so the stream stays alive either way.
  reader_.WaitAndCopyFrame(latestFrame_, frameBytes, kWaitTimeoutMs, width_, height_);
}

void CCropVCamStream::StampSampleTime(IMediaSample* pSample) {
  const REFERENCE_TIME start = streamPosition_;
  const REFERENCE_TIME stop = start + frameLengthUnits_;
  streamPosition_ = stop;
  pSample->SetTime(const_cast<REFERENCE_TIME*>(&start), const_cast<REFERENCE_TIME*>(&stop));
  pSample->SetSyncPoint(TRUE);
}
