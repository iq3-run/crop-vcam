using OpenCvSharp;

namespace CropVCam.App.Processing;

/// <summary>
/// Crops the center of a frame down to size/magnification, then scales the
/// crop back up to fill the output canvas - standard "digital zoom":
/// magnification 2 keeps the center half of each dimension, 3 keeps a
/// third, etc. The center point never moves.
/// </summary>
internal static class CenterCropScaler
{
    private const int MinCropSizePixels = 2;

    public static Mat CropAndScale(Mat source, double magnification, int outputWidth, int outputHeight)
    {
        var cropWidth = ClampCropSize(source.Width, magnification);
        var cropHeight = ClampCropSize(source.Height, magnification);
        var x = (source.Width - cropWidth) / 2;
        var y = (source.Height - cropHeight) / 2;

        using var cropped = new Mat(source, new Rect(x, y, cropWidth, cropHeight));
        var result = new Mat();
        Cv2.Resize(cropped, result, new Size(outputWidth, outputHeight), interpolation: InterpolationFlags.Linear);
        return result;
    }

    private static int ClampCropSize(int sourceLength, double magnification)
    {
        var cropped = (int)Math.Round(sourceLength / magnification);
        return Math.Clamp(cropped, MinCropSizePixels, sourceLength);
    }
}
