using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CropVCam.App.VirtualCamera;

/// <summary>
/// Registers the CropVCam DirectShow filter so it shows up as a webcam.
/// Everything is written under HKEY_CURRENT_USER only - see
/// src/CropVCam.Filter/src/Registration.cpp for why (creating a new key
/// under HKEY_CLASSES_ROOT defaults to HKLM, which needs admin rights).
/// </summary>
internal static class FilterRegistrar
{
    // Must match src/CropVCam.Filter/src/CropVCamFilter.h's CLSID_CropVCamFilter.
    public static readonly Guid FilterClsid = new("C7B5659C-5708-4525-83E4-16DD3FAE90E2");

    // CLSID_VideoInputDeviceCategory
    private const string VideoInputCategoryClsid = "{860BB310-5D01-11d0-BD3B-00A0C911CE86}";

    private static string FilterClsidString => $"{{{FilterClsid}}}";

    public static void EnsureRegistered(string filterDllPath)
    {
        if (IsRegistered())
        {
            return;
        }

        if (!File.Exists(filterDllPath))
        {
            throw new FileNotFoundException($"仮想カメラフィルタが見つかりません: {filterDllPath}", filterDllPath);
        }

        var module = NativeMethods.LoadLibrary(filterDllPath);
        if (module == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"フィルタDLLの読み込みに失敗しました: {filterDllPath}");
        }

        try
        {
            RunDllRegisterServer(module);
        }
        finally
        {
            NativeMethods.FreeLibrary(module);
        }
    }

    public static void SetFriendlyName(string friendlyName)
    {
        using var clsidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{FilterClsidString}");
        clsidKey.SetValue(null, friendlyName);

        using var instanceKey = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\CLSID\{VideoInputCategoryClsid}\Instance\{FilterClsidString}");
        instanceKey.SetValue("FriendlyName", friendlyName);
    }

    private static void RunDllRegisterServer(IntPtr module)
    {
        var procAddress = NativeMethods.GetProcAddress(module, "DllRegisterServer");
        if (procAddress == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException("DllRegisterServer が見つかりません。");
        }

        var register = Marshal.GetDelegateForFunctionPointer<DllRegisterServerDelegate>(procAddress);
        var hr = register();
        if (hr != 0)
        {
            throw new COMException("仮想カメラの登録に失敗しました。", hr);
        }
    }

    // Checks both the base CLSID and the video-capture-category registration
    // DllRegisterServer writes, so a run that failed partway through (e.g.
    // it registered the CLSID but crashed before the category instance) is
    // retried instead of being mistaken for a complete registration.
    private static bool IsRegistered()
    {
        using var clsidKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\CLSID\{FilterClsidString}\InprocServer32");
        using var instanceKey = Registry.CurrentUser.OpenSubKey(
            $@"Software\Classes\CLSID\{VideoInputCategoryClsid}\Instance\{FilterClsidString}");
        return clsidKey is not null && instanceKey is not null;
    }

    private delegate int DllRegisterServerDelegate();

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        public static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(IntPtr module);
    }
}
