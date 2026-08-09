#include "CropVCamStream.h"

#include <cstring>

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
    : CSourceStream(NAME("CropVCam Output Stream"), phr, reinterpret_cast<CSource*>(pParent), pPinName),
      latestFrame_(new BYTE[OutputFrameBytes()]),
      hasFrame_(false),
      streamPosition_(0),
      frameLengthUnits_(kUnitsPerSecond / kTargetFps) {
  std::memset(latestFrame_, 0, OutputFrameBytes());
}

CCropVCamStream::~CCropVCamStream() { delete[] latestFrame_; }

HRESULT CCropVCamStream::OnThreadCreate() {
  streamPosition_ = 0;
  hasFrame_ = false;
  return S_OK;
}

HRESULT CCropVCamStream::GetMediaType(CMediaType* pMediaType) {
  CheckPointer(pMediaType, E_POINTER);

  VIDEOINFOHEADER vih;
  ZeroMemory(&vih, sizeof(vih));
  vih.AvgTimePerFrame = frameLengthUnits_;
  vih.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
  vih.bmiHeader.biWidth = CropVCam::kOutputWidth;
  vih.bmiHeader.biHeight = CropVCam::kOutputHeight;  // positive: bottom-up DIB, standard RGB24 layout
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

  int width = 0;
  int height = 0;
  int stride = 0;
  const bool gotFrame =
      reader_.WaitAndCopyFrame(latestFrame_, neededBytes, kWaitTimeoutMs, &width, &height, &stride);
  if (gotFrame && width == CropVCam::kOutputWidth && height == CropVCam::kOutputHeight) {
    hasFrame_ = true;
  }
  // If no fresh frame arrived (app not running yet, or a hiccup), we keep
  // delivering the last frame we have (or the initial black one) so the
  // stream stays alive instead of stalling the downstream consumer.

  std::memcpy(pData, latestFrame_, neededBytes);
  pSample->SetActualDataLength(neededBytes);

  const REFERENCE_TIME start = streamPosition_;
  const REFERENCE_TIME stop = start + frameLengthUnits_;
  streamPosition_ = stop;
  pSample->SetTime(const_cast<REFERENCE_TIME*>(&start), const_cast<REFERENCE_TIME*>(&stop));
  pSample->SetSyncPoint(TRUE);

  return S_OK;
}
