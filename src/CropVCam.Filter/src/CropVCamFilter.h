#pragma once

#include <streams.h>

// {C7B5659C-5708-4525-83E4-16DD3FAE90E2}
// Keep in sync with Registration.cpp's registry strings.
DEFINE_GUID(CLSID_CropVCamFilter, 0xc7b5659c, 0x5708, 0x4525, 0x83, 0xe4, 0x16, 0xdd, 0x3f, 0xae,
            0x90, 0xe2);

// The DirectShow source filter itself. It owns a single CCropVCamStream
// output pin; all the actual frame production happens there.
class CCropVCamSource : public CSource {
 public:
  static CUnknown* WINAPI CreateInstance(LPUNKNOWN pUnk, HRESULT* phr);

 private:
  CCropVCamSource(LPUNKNOWN pUnk, HRESULT* phr);
};
