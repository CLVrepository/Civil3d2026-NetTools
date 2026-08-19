using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CLV_CivilTools.PdfViewer
{
    internal static class PdfRenderClient
    {
        private const int ProcessTimeoutMilliseconds = 60000;

        public static int GetPageCount(string pdfPath)
        {
            PdfRenderResponse response = Execute(new PdfRenderRequest
            {
                Operation = "metadata",
                PdfPath = pdfPath
            });
            if (response.PageCount <= 0)
            {
                throw new InvalidOperationException(
                    "The PDF renderer opened the file but reported zero pages. " +
                    "The PDF may be damaged, password-protected, or use an unsupported structure.");
            }

            return response.PageCount;
        }

        public static SizeF GetPageSize(string pdfPath, int pageIndex)
        {
            PdfRenderResponse response = Execute(new PdfRenderRequest
            {
                Operation = "metadata",
                PdfPath = pdfPath,
                PageIndex = pageIndex
            });
            return new SizeF(response.PageWidth, response.PageHeight);
        }

        public static Bitmap Render(
            string pdfPath,
            int pageIndex,
            int width,
            int height,
            RectangleF? bounds)
        {
            string outputPath = Path.Combine(Path.GetTempPath(), $"CLV_PdfViewer_{Guid.NewGuid():N}.png");
            try
            {
                PdfRenderRequest request = new()
                {
                    Operation = "render",
                    PdfPath = pdfPath,
                    PageIndex = pageIndex,
                    Width = width,
                    Height = height,
                    OutputPath = outputPath,
                    HasBounds = bounds.HasValue
                };

                if (bounds.HasValue)
                {
                    request.BoundsLeft = bounds.Value.Left;
                    request.BoundsTop = bounds.Value.Top;
                    request.BoundsWidth = bounds.Value.Width;
                    request.BoundsHeight = bounds.Value.Height;
                }

                _ = Execute(request);
                if (!File.Exists(outputPath))
                    throw new InvalidOperationException("The PDF renderer did not create an output image.");

                using FileStream stream = new(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image loaded = Image.FromStream(stream);
                return new Bitmap(loaded);
            }
            finally
            {
                TryDelete(outputPath);
            }
        }

        private static PdfRenderResponse Execute(PdfRenderRequest request)
        {
            string hostPath = GetHostPath();
            string requestPath = Path.Combine(Path.GetTempPath(), $"CLV_PdfViewer_Request_{Guid.NewGuid():N}.json");
            string responsePath = Path.Combine(Path.GetTempPath(), $"CLV_PdfViewer_Response_{Guid.NewGuid():N}.json");
            request.ResponsePath = responsePath;

            try
            {
                File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

                ProcessStartInfo startInfo = new()
                {
                    FileName = hostPath,
                    Arguments = $"\"{requestPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
                };

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to start the isolated PDF renderer.");

                if (!process.WaitForExit(ProcessTimeoutMilliseconds))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    throw new TimeoutException("The isolated PDF renderer did not respond within 60 seconds.");
                }

                string stderr = process.StandardError.ReadToEnd();
                string stdout = process.StandardOutput.ReadToEnd();
                if (!File.Exists(responsePath))
                {
                    string detail = !string.IsNullOrWhiteSpace(stderr)
                        ? stderr.Trim()
                        : !string.IsNullOrWhiteSpace(stdout)
                            ? stdout.Trim()
                            : $"Renderer exited with code {process.ExitCode}.";
                    throw new InvalidOperationException(
                        $"The isolated PDF renderer did not return a response. {detail}");
                }

                PdfRenderResponse? response = JsonSerializer.Deserialize<PdfRenderResponse>(File.ReadAllText(responsePath));
                if (response == null)
                    throw new InvalidOperationException("The isolated PDF renderer returned an invalid response.");
                if (!response.Success)
                {
                    string detail = string.IsNullOrWhiteSpace(response.Error)
                        ? "PDF rendering failed without an error message."
                        : response.Error.Trim();
                    throw new InvalidOperationException(detail);
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"The isolated PDF renderer exited with code {process.ExitCode} after returning a response.");
                }

                return response;
            }
            finally
            {
                TryDelete(requestPath);
                TryDelete(responsePath);
            }
        }

        private static string GetHostPath()
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? AppContext.BaseDirectory;
            string hostPath = Path.Combine(assemblyFolder, "PdfRenderer", "CLV.PdfRenderHost.exe");
            if (!File.Exists(hostPath))
            {
                throw new FileNotFoundException(
                    "The isolated PDF renderer is missing. Copy the PdfRenderer folder beside the Civil Tools DLL.",
                    hostPath);
            }
            return hostPath;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temporary cleanup failure is non-fatal.
            }
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
}
