// Instantiates storage for CLSID_CropVCamFilter (DEFINE_GUID only declares
// `extern const GUID` unless INITGUID is defined before the declaration).
// Every other translation unit just links against the symbol defined here.
#include <initguid.h>

#include "CropVCamFilter.h"
