using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PegSnap.Core.Ocr;

public static class OcrService
{
    /// <summary>
    /// Extracts text directly from a saved image file using native Windows 10/11 OCR engine.
    /// Zero WPF BitmapSource allocations!
    /// </summary>
    public static async Task<string> ExtractTextFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return "[Clothey OCR] File not found.";

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null)
            {
                var lang = new Windows.Globalization.Language("en-US");
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }

            if (ocrEngine == null)
            {
                return "[Clothey OCR] Windows OCR language pack is not available on this system.";
            }

            var result = await ocrEngine.RecognizeAsync(softwareBitmap);
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                return "[Clothey OCR] No text recognized in selected area.";
            }

            var sb = new StringBuilder();
            foreach (var line in result.Lines)
            {
                sb.AppendLine(line.Text);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"[Clothey OCR Error] {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts text from a BitmapSource using native Windows 10/11 OCR engine.
    /// </summary>
    public static async Task<string> ExtractTextAsync(BitmapSource bitmapSource)
    {
        try
        {
            // Convert BitmapSource to byte array PNG stream
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
            encoder.Save(ms);
            ms.Position = 0;

            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var outputStream = randomAccessStream.GetOutputStreamAt(0))
            {
                using var dw = new DataWriter(outputStream);
                dw.WriteBytes(ms.ToArray());
                await dw.StoreAsync();
                await outputStream.FlushAsync();
            }

            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null)
            {
                var lang = new Windows.Globalization.Language("en-US");
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }

            if (ocrEngine == null)
            {
                return "[Clothey OCR] Windows OCR language pack is not available on this system.";
            }

            var result = await ocrEngine.RecognizeAsync(softwareBitmap);
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                return "[Clothey OCR] No text recognized in selected area.";
            }

            var sb = new StringBuilder();
            foreach (var line in result.Lines)
            {
                sb.AppendLine(line.Text);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"[Clothey OCR Error] {ex.Message}";
        }
    }
}
