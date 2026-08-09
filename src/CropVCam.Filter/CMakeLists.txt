cmake_minimum_required(VERSION 3.20)
project(CropVCamFilter LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_compile_definitions(_UNICODE UNICODE WIN32_LEAN_AND_MEAN)

# --- Microsoft DirectShow base classes (vendored, MIT licensed) ---
add_library(baseclasses STATIC
    baseclasses/amextra.cpp
    baseclasses/amfilter.cpp
    baseclasses/amvideo.cpp
    baseclasses/arithutil.cpp
    baseclasses/combase.cpp
    baseclasses/cprop.cpp
    baseclasses/ctlutil.cpp
    baseclasses/ddmm.cpp
    baseclasses/dllentry.cpp
    baseclasses/dllsetup.cpp
    baseclasses/mtype.cpp
    baseclasses/outputq.cpp
    baseclasses/perflog.cpp
    baseclasses/pstream.cpp
    baseclasses/pullpin.cpp
    baseclasses/refclock.cpp
    baseclasses/renbase.cpp
    baseclasses/schedule.cpp
    baseclasses/seekpt.cpp
    baseclasses/source.cpp
    baseclasses/strmctl.cpp
    baseclasses/sysclock.cpp
    baseclasses/transfrm.cpp
    baseclasses/transip.cpp
    baseclasses/videoctl.cpp
    baseclasses/vtrans.cpp
    baseclasses/winctrl.cpp
    baseclasses/winutil.cpp
    baseclasses/wxdebug.cpp
    baseclasses/wxlist.cpp
    baseclasses/wxutil.cpp
)
target_include_directories(baseclasses PUBLIC baseclasses)
target_compile_definitions(baseclasses PUBLIC _WIN32_WINNT=0x0601)

# --- CropVCam source filter DLL ---
add_library(CropVCamFilter SHARED
    src/Guids.cpp
    src/CropVCamFilter.cpp
    src/CropVCamStream.cpp
    src/SharedFrameReader.cpp
    src/Registration.cpp
    CropVCamFilter.def
)

target_link_libraries(CropVCamFilter PRIVATE baseclasses strmiids winmm)
set_target_properties(CropVCamFilter PROPERTIES OUTPUT_NAME "CropVCamFilter")
