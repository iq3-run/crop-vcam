#include "CropVCamFilter.h"

#include <new>

#include "CropVCamStream.h"

CUnknown* WINAPI CCropVCamSource::CreateInstance(LPUNKNOWN pUnk, HRESULT* phr) {
  CCropVCamSource* pNewFilter = new (std::nothrow) CCropVCamSource(pUnk, phr);
  if (pNewFilter == nullptr && phr != nullptr) {
    *phr = E_OUTOFMEMORY;
  }
  return pNewFilter;
}

CCropVCamSource::CCropVCamSource(LPUNKNOWN pUnk, HRESULT* phr)
    : CSource(NAME("CropVCam Source"), pUnk, CLSID_CropVCamFilter) {
  // The stream registers itself with this filter (via CSourceStream's
  // constructor calling CSource::AddPin) and is owned by the base class
  // from that point on.
  HRESULT hrPin = S_OK;
  new CCropVCamStream(&hrPin, this, L"Output");
  if (phr != nullptr) {
    *phr = hrPin;
  }
}
