using OpenCvSharp;

namespace CropVCam.App.Processing;

/// <summary>
/// Crops the center of a frame down to size/magnification, then scales the
/// crop back up to fill the output canvas - standard "digital zoom":
/// magnification 2 keeps the center half of each dimension, 3 keeps a
/// third, etc. The center point never moves.
///
/// The crop rectangle is always fit to the output's aspect ratio before
/// magnification is applied, so a non-16:9 source (e.g. a 4:3 webcam) gets
/// its excess height/width cropped away instead of stretched to fill the
/// fixed-size output canvas.
/// </summary>
internal static class CenterCropScaler
{
    private const int MinCropSizePixels = 2;

    public static Mat CropAndScale(Mat source, double magnification, int outputWidth, int outputHeight)
    {
        var (baseWidth, baseHeight) = FitToAspectRatio(source.Width, source.Height, outputWidth, outputHeight);

        var cropWidth = ClampCropSize(baseWidth, magnification, source.Width);
        var cropHeight = ClampCropSize(baseHeight, magnification, source.Height);
        var x = (source.Width - cropWidth) / 2;
        var y = (source.Height - cropHeight) / 2;

        using var cropped = new Mat(source, new Rect(x, y, cropWidth, cropHeight));
        var result = new Mat();
        Cv2.Resize(cropped, result, new OpenCvSharp.Size(outputWidth, outputHeight), interpolation: InterpolationFlags.Linear);
        return result;
    }

    // The largest region, centered in the source, whose aspect ratio matches
    // the target's - i.e. what magnification 1.0 should crop to.
    private static (int Width, int Height) FitToAspectRatio(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var targetAspect = (double)targetWidth / targetHeight;
        var sourceAspect = (double)sourceWidth / sourceHeight;

        return sourceAspect > targetAspect
            ? ((int)Math.Round(sourceHeight * targetAspect), sourceHeight)
            : (sourceWidth, (int)Math.Round(sourceWidth / targetAspect));
    }

    private static int ClampCropSize(int baseLength, double magnification, int sourceLength)
    {
        var cropped = (int)Math.Round(baseLength / magnification);
        return Math.Clamp(cropped, Math.Min(MinCropSizePixels, sourceLength), sourceLength);
    }
}
