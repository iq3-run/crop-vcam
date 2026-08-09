using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CropVCam.App.VirtualCamera;

namespace CropVCam.App.Camera;

/// <summary>
/// Lists capture devices via DirectShow's video input category - the same
/// enumeration OpenCvSharp's DSHOW backend walks, so the returned
/// <see cref="CameraDevice.Index"/> can be passed straight to
/// <c>VideoCapture(index, VideoCaptureAPIs.DSHOW)</c>.
/// </summary>
internal static class CameraEnumerator
{
    public static IReadOnlyList<CameraDevice> EnumerateCameras()
    {
        var devices = new List<CameraDevice>();
        var category = DirectShowCategories.VideoInputDevice;
        var systemDeviceEnum = (ICreateDevEnum)new SystemDeviceEnum();

        var hr = systemDeviceEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0);
        if (hr != 0 || enumMoniker is null)
        {
            return devices; // S_FALSE: no capture devices installed at all
        }

        var index = 0;
        var fetched = new IMoniker[1];
        while (enumMoniker.Next(1, fetched, IntPtr.Zero) == 0)
        {
            var moniker = fetched[0];
            try
            {
                var (name, clsid) = ReadFriendlyNameAndClsid(moniker);
                if (name is null || clsid == FilterRegistrar.FilterClsid)
                {
                    continue; // skip our own virtual camera to avoid a feedback loop
                }

                devices.Add(new CameraDevice(index, name));
                index++;
            }
            finally
            {
                Marshal.ReleaseComObject(moniker);
            }
        }

        return devices;
    }

    private static (string? Name, Guid Clsid) ReadFriendlyNameAndClsid(IMoniker moniker)
    {
        if (NativeMethods.CreateBindCtx(0, out var bindCtx) != 0)
        {
            return (null, Guid.Empty);
        }

        try
        {
            var propertyBagIid = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(bindCtx, null, ref propertyBagIid, out var propertyBagObj);
            if (propertyBagObj is not IPropertyBag propertyBag)
            {
                return (null, Guid.Empty);
            }

            object nameValue = string.Empty;
            propertyBag.Read("FriendlyName", ref nameValue, IntPtr.Zero);

            object clsidValue = string.Empty;
            propertyBag.Read("CLSID", ref clsidValue, IntPtr.Zero);

            var clsid = clsidValue is string clsidString && Guid.TryParse(clsidString, out var parsed)
                ? parsed
                : Guid.Empty;

            return (nameValue as string, clsid);
        }
        finally
        {
            Marshal.ReleaseComObject(bindCtx);
        }
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll")]
        public static extern int CreateBindCtx(int reserved, out IBindCtx bindCtx);
    }
}
