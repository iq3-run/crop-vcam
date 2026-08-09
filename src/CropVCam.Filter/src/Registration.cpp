#include "CropVCamFilter.h"

#include <strsafe.h>

// Table the base classes' DllGetClassObject/DllCanUnloadNow (already
// implemented in baseclasses/dllentry.cpp) use to find our filter.
CFactoryTemplate g_Templates[] = {
    {L"CropVCam Output", &CLSID_CropVCamFilter, CCropVCamSource::CreateInstance, nullptr, nullptr},
};
int g_cTemplates = sizeof(g_Templates) / sizeof(g_Templates[0]);

namespace {

// Must match CropVCamFilter.h's CLSID_CropVCamFilter.
constexpr wchar_t kFilterClsidString[] = L"{C7B5659C-5708-4525-83E4-16DD3FAE90E2}";
// CLSID_VideoInputDeviceCategory: the DirectShow category apps enumerate
// to list webcams.
constexpr wchar_t kVideoInputCategoryString[] = L"{860BB310-5D01-11d0-BD3B-00A0C911CE86}";
constexpr wchar_t kDefaultFriendlyName[] = L"CropVCam Output";

// Registration deliberately never touches HKEY_CLASSES_ROOT or
// HKEY_LOCAL_MACHINE: creating a brand-new key under HKCR defaults to
// HKLM\Software\Classes, which a non-admin process cannot write. Writing
// directly under HKEY_CURRENT_USER\Software\Classes needs no elevation and
// is still merged into HKEY_CLASSES_ROOT for any non-elevated process
// (including the consuming app, e.g. Zoom) running in the same session.

HRESULT WriteStringValue(HKEY key, const wchar_t* name, const wchar_t* value) {
  const DWORD sizeBytes = static_cast<DWORD>((wcslen(value) + 1) * sizeof(wchar_t));
  const LONG result =
      RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value), sizeBytes);
  return HRESULT_FROM_WIN32(result);
}

HRESULT RegisterBaseClsid() {
  wchar_t modulePath[MAX_PATH];
  if (GetModuleFileNameW(g_hInst, modulePath, MAX_PATH) == 0) {
    return HRESULT_FROM_WIN32(GetLastError());
  }

  wchar_t clsidKeyPath[256];
  StringCchPrintfW(clsidKeyPath, ARRAYSIZE(clsidKeyPath), L"Software\\Classes\\CLSID\\%s",
                    kFilterClsidString);

  HKEY clsidKey = nullptr;
  LONG result = RegCreateKeyExW(HKEY_CURRENT_USER, clsidKeyPath, 0, nullptr, 0, KEY_WRITE, nullptr,
                                 &clsidKey, nullptr);
  if (result != ERROR_SUCCESS) {
    return HRESULT_FROM_WIN32(result);
  }

  HRESULT hr = WriteStringValue(clsidKey, nullptr, kDefaultFriendlyName);

  if (SUCCEEDED(hr)) {
    HKEY inprocKey = nullptr;
    result = RegCreateKeyExW(clsidKey, L"InprocServer32", 0, nullptr, 0, KEY_WRITE, nullptr,
                              &inprocKey, nullptr);
    if (result == ERROR_SUCCESS) {
      hr = WriteStringValue(inprocKey, nullptr, modulePath);
      if (SUCCEEDED(hr)) {
        hr = WriteStringValue(inprocKey, L"ThreadingModel", L"Both");
      }
      RegCloseKey(inprocKey);
    } else {
      hr = HRESULT_FROM_WIN32(result);
    }
  }

  RegCloseKey(clsidKey);
  return hr;
}

HRESULT RegisterCaptureCategoryInstance() {
  wchar_t instanceKeyPath[320];
  StringCchPrintfW(instanceKeyPath, ARRAYSIZE(instanceKeyPath),
                    L"Software\\Classes\\CLSID\\%s\\Instance\\%s", kVideoInputCategoryString,
                    kFilterClsidString);

  HKEY instanceKey = nullptr;
  LONG result = RegCreateKeyExW(HKEY_CURRENT_USER, instanceKeyPath, 0, nullptr, 0, KEY_WRITE,
                                 nullptr, &instanceKey, nullptr);
  if (result != ERROR_SUCCESS) {
    return HRESULT_FROM_WIN32(result);
  }

  HRESULT hr = WriteStringValue(instanceKey, L"CLSID", kFilterClsidString);
  if (SUCCEEDED(hr)) {
    hr = WriteStringValue(instanceKey, L"FriendlyName", kDefaultFriendlyName);
  }
  RegCloseKey(instanceKey);
  return hr;
}

}  // namespace

STDAPI DllRegisterServer() {
  HRESULT hr = RegisterBaseClsid();
  if (FAILED(hr)) {
    return hr;
  }
  return RegisterCaptureCategoryInstance();
}

STDAPI DllUnregisterServer() {
  wchar_t instanceKeyPath[320];
  StringCchPrintfW(instanceKeyPath, ARRAYSIZE(instanceKeyPath),
                    L"Software\\Classes\\CLSID\\%s\\Instance\\%s", kVideoInputCategoryString,
                    kFilterClsidString);
  RegDeleteKeyW(HKEY_CURRENT_USER, instanceKeyPath);

  wchar_t inprocKeyPath[288];
  StringCchPrintfW(inprocKeyPath, ARRAYSIZE(inprocKeyPath),
                    L"Software\\Classes\\CLSID\\%s\\InprocServer32", kFilterClsidString);
  RegDeleteKeyW(HKEY_CURRENT_USER, inprocKeyPath);

  wchar_t clsidKeyPath[256];
  StringCchPrintfW(clsidKeyPath, ARRAYSIZE(clsidKeyPath), L"Software\\Classes\\CLSID\\%s",
                    kFilterClsidString);
  RegDeleteKeyW(HKEY_CURRENT_USER, clsidKeyPath);

  return S_OK;
}

extern "C" BOOL WINAPI DllEntryPoint(HINSTANCE, ULONG, LPVOID);

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved) {
  return DllEntryPoint(reinterpret_cast<HINSTANCE>(hModule), reason, reserved);
}
