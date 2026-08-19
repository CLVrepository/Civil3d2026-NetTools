using System.Drawing;
using System.Text.Json;
using PDFtoImage;
using SkiaSharp;

internal static class Program
{
    private static int Main(string[] args)
    {
        PdfRenderRequest? request = null;
        PdfRenderResponse response = new();
        try
        {
            if (args.Length != 1 || !File.Exists(args[0]))
                throw new ArgumentException("A valid renderer request file is required.");

            request = JsonSerializer.Deserialize<PdfRenderRequest>(File.ReadAllText(args[0]))
                ?? throw new InvalidOperationException("The renderer request is invalid.");
            if (string.IsNullOrWhiteSpace(request.PdfPath) || !File.Exists(request.PdfPath))
                throw new FileNotFoundException("The selected PDF file was not found.", request.PdfPath);

            if (!string.Equals(request.Operation, "metadata", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Operation, "render", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported renderer operation: {request.Operation}");
            }

            // PDFtoImage may take ownership of and dispose the supplied stream.
            // Use a fresh stream for each independent PDF operation rather than
            // attempting to rewind and reuse a stream that may already be closed.
            using (FileStream pageCountStream = OpenPdf(request.PdfPath))
            {
                response.PageCount = Conversion.GetPageCount(pageCountStream);
            }

            if (response.PageCount <= 0)
            {
                throw new InvalidDataException(
                    "PDFium opened the document but reported zero pages. " +
                    "The file may be damaged, password-protected, or use an unsupported PDF structure.");
            }

            if (request.PageIndex < 0 || request.PageIndex >= response.PageCount)
                request.PageIndex = 0;

            using (FileStream pageSizeStream = OpenPdf(request.PdfPath))
            {
                SizeF pageSize = Conversion.GetPageSize(pageSizeStream, request.PageIndex);
                response.PageWidth = pageSize.Width;
                response.PageHeight = pageSize.Height;
            }

            if (string.Equals(request.Operation, "render", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.OutputPath))
                    throw new ArgumentException("An output image path is required.");

                RectangleF? bounds = request.HasBounds
                    ? new RectangleF(request.BoundsLeft, request.BoundsTop, request.BoundsWidth, request.BoundsHeight)
                    : null;

                RenderOptions options = new(
                    Dpi: 144,
                    Width: Math.Max(1, request.Width),
                    Height: Math.Max(1, request.Height),
                    WithAnnotations: true,
                    WithAspectRatio: true,
                    Bounds: bounds,
                    DpiRelativeToBounds: bounds.HasValue,
                    UseTiling: true);

                using FileStream renderStream = OpenPdf(request.PdfPath);
                using SKBitmap bitmap = Conversion.ToImage(renderStream, request.PageIndex, options: options);
                using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(request.OutputPath, encoded.ToArray());
            }

            response.Success = true;
            WriteResponse(request.ResponsePath, response);
            return 0;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                response.Error += $" | {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            if (request != null)
                WriteResponse(request.ResponsePath, response);
            else
                Console.Error.WriteLine(ex);
            return 1;
        }
    }


    private static FileStream OpenPdf(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
    }

    private static void WriteResponse(string path, PdfRenderResponse response)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        File.WriteAllText(path, JsonSerializer.Serialize(response));
    }

    private sealed class PdfRenderRequest
    {
        public string Operation { get; set; } = string.Empty;
        public string PdfPath { get; set; } = string.Empty;
        public int PageIndex { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ResponsePath { get; set; } = string.Empty;
        public bool HasBounds { get; set; }
        public float BoundsLeft { get; set; }
        public float BoundsTop { get; set; }
        public float BoundsWidth { get; set; }
        public float BoundsHeight { get; set; }
    }

    private sealed class PdfRenderResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int PageCount { get; set; }
        public float PageWidth { get; set; }
        public float PageHeight { get; set; }
    }
}
