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

int OutputFrameBytes() { return CropVCam::kOutputWidth * CropVCam::kOutputHeight * 3; }
}  // namespace

CCropVCamStream::CCropVCamStream(HRESULT* phr, CCropVCamSource* pParent, LPCWSTR pPinName)
    : CSourceStream(NAME("CropVCam Output Stream"), phr, static_cast<CSource*>(pParent), pPinName),
      latestFrame_(new BYTE[OutputFrameBytes()]),
      streamPosition_(0),
      frameLengthUnits_(kUnitsPerSecond / kTargetFps) {
  std::memset(latestFrame_, 0, OutputFrameBytes());
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
  caps->InputSize.cx = CropVCam::kOutputWidth;
  caps->InputSize.cy = CropVCam::kOutputHeight;
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
  caps->MinBitsPerSecond = OutputFrameBytes() * 8 * kTargetFps;
  caps->MaxBitsPerSecond = caps->MinBitsPerSecond;

  return S_OK;
}

HRESULT CCropVCamStream::GetMediaType(CMediaType* pMediaType) {
  CheckPointer(pMediaType, E_POINTER);

  VIDEOINFOHEADER vih;
  ZeroMemory(&vih, sizeof(vih));
  vih.AvgTimePerFrame = frameLengthUnits_;
  vih.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
  vih.bmiHeader.biWidth = CropVCam::kOutputWidth;
  // Negative: top-down DIB. SharedFrameWriter (C#) writes rows top-down, so
  // this must stay negative or the delivered picture is vertically flipped.
  vih.bmiHeader.biHeight = -CropVCam::kOutputHeight;
  vih.bmiHeader.biPlanes = 1;
  vih.bmiHeader.biBitCount = 24;
  vih.bmiHeader.biCompression = BI_RGB;
  vih.bmiHeader.biSizeImage = OutputFrameBytes();

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

  pProperties->cBuffers = 2;
  pProperties->cbBuffer = OutputFrameBytes();

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

  const long neededBytes = OutputFrameBytes();
  if (pSample->GetSize() < neededBytes) {
    return E_FAIL;
  }

  RefreshLatestFrame(neededBytes);
  std::memcpy(pData, latestFrame_, neededBytes);
  pSample->SetActualDataLength(neededBytes);

  StampSampleTime(pSample);
  return S_OK;
}

void CCropVCamStream::RefreshLatestFrame(long frameBytes) {
  int width = 0;
  int height = 0;
  int stride = 0;
  // If no fresh frame arrived (app not running yet, or a hiccup),
  // latestFrame_ already holds the last one we have (or the initial black
  // frame) - WaitAndCopyFrame only touches the buffer when it has a real
  // frame to hand over - so the stream stays alive either way.
  reader_.WaitAndCopyFrame(latestFrame_, frameBytes, kWaitTimeoutMs, &width, &height, &stride);
}

void CCropVCamStream::StampSampleTime(IMediaSample* pSample) {
  const REFERENCE_TIME start = streamPosition_;
  const REFERENCE_TIME stop = start + frameLengthUnits_;
  streamPosition_ = stop;
  pSample->SetTime(const_cast<REFERENCE_TIME*>(&start), const_cast<REFERENCE_TIME*>(&stop));
  pSample->SetSyncPoint(TRUE);
}
