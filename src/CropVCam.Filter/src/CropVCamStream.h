#pragma once

#include <streams.h>

#include "SharedFrameReader.h"

class CCropVCamSource;

// The filter's single output pin. Runs on its own worker thread (owned by
// the CSourceStream base class); each FillBuffer call pulls the newest
// frame out of shared memory and hands it to the DirectShow pipeline.
//
// Also implements two interfaces capture apps expect from a real capture
// pin, neither of which CSourceStream provides on its own:
//   - IKsPropertySet: answers "what pin category are you" (PIN_CATEGORY_CAPTURE).
//   - IAMStreamConfig: lets the caller enumerate/negotiate the (single,
//     fixed) format this pin offers.
// Without both, some capture apps (confirmed: ffmpeg's dshow input) can see
// the device but conclude it has no usable pin and refuse to open it.
class CCropVCamStream : public CSourceStream, public IKsPropertySet, public IAMStreamConfig {
 public:
  CCropVCamStream(HRESULT* phr, CCropVCamSource* pParent, LPCWSTR pPinName);
  ~CCropVCamStream();

  DECLARE_IUNKNOWN
  STDMETHODIMP NonDelegatingQueryInterface(REFIID riid, void** ppv) override;

  HRESULT FillBuffer(IMediaSample* pSample) override;
  HRESULT GetMediaType(CMediaType* pMediaType) override;
  HRESULT DecideBufferSize(IMemAllocator* pAlloc, ALLOCATOR_PROPERTIES* pProperties) override;
  HRESULT OnThreadCreate() override;

  // IKsPropertySet
  HRESULT STDMETHODCALLTYPE Set(REFGUID guidPropSet, DWORD dwPropID, LPVOID pInstanceData,
                                 DWORD cbInstanceData, LPVOID pPropData, DWORD cbPropData) override;
  HRESULT STDMETHODCALLTYPE Get(REFGUID guidPropSet, DWORD dwPropID, LPVOID pInstanceData, DWORD cbInstanceData,
                                 LPVOID pPropData, DWORD cbPropData, DWORD* pcbReturned) override;
  HRESULT STDMETHODCALLTYPE QuerySupported(REFGUID guidPropSet, DWORD dwPropID, DWORD* pTypeSupport) override;

  // IAMStreamConfig
  HRESULT STDMETHODCALLTYPE SetFormat(AM_MEDIA_TYPE* pmt) override;
  HRESULT STDMETHODCALLTYPE GetFormat(AM_MEDIA_TYPE** ppmt) override;
  HRESULT STDMETHODCALLTYPE GetNumberOfCapabilities(int* piCount, int* piSize) override;
  HRESULT STDMETHODCALLTYPE GetStreamCaps(int iIndex, AM_MEDIA_TYPE** ppmt, BYTE* pSCC) override;

 private:
  void EnsureFormatResolved();
  long FrameBytes() const;
  void RefreshLatestFrame(long frameBytes);
  void StampSampleTime(IMediaSample* pSample);

  CropVCam::SharedFrameReader reader_;
  BYTE* latestFrame_;
  REFERENCE_TIME streamPosition_;
  REFERENCE_TIME frameLengthUnits_;

  // The format negotiated for this pin connection - resolved once (from the
  // physical camera's actual resolution, via reader_.TryPeekFrameSize) and
  // then held fixed for the connection's lifetime, since DirectShow doesn't
  // renegotiate mid-stream. Starts at the fallback default and is only ever
  // updated by EnsureFormatResolved, before the first real value sticks.
  int width_;
  int height_;
  bool formatResolved_;
};
