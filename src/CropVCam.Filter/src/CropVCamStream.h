#pragma once

#include <streams.h>

#include "SharedFrameReader.h"

class CCropVCamSource;

// The filter's single output pin. Runs on its own worker thread (owned by
// the CSourceStream base class); each FillBuffer call pulls the newest
// frame out of shared memory and hands it to the DirectShow pipeline.
class CCropVCamStream : public CSourceStream {
 public:
  CCropVCamStream(HRESULT* phr, CCropVCamSource* pParent, LPCWSTR pPinName);
  ~CCropVCamStream();

  HRESULT FillBuffer(IMediaSample* pSample) override;
  HRESULT GetMediaType(CMediaType* pMediaType) override;
  HRESULT DecideBufferSize(IMemAllocator* pAlloc, ALLOCATOR_PROPERTIES* pProperties) override;
  HRESULT OnThreadCreate() override;

 private:
  void RefreshLatestFrame(long frameBytes);
  void StampSampleTime(IMediaSample* pSample);

  CropVCam::SharedFrameReader reader_;
  BYTE* latestFrame_;
  REFERENCE_TIME streamPosition_;
  REFERENCE_TIME frameLengthUnits_;
};
