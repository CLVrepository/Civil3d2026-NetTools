using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using DrawingImage = System.Drawing.Image;
using DrawingFont = System.Drawing.Font;
using System.Net;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Survey
{
    public static class SurveyPhotoReviewCommands
    {
        private static PaletteSet? _paletteSet;
        private static SurveyPhotoReviewControl? _control;

        [CommandMethod("SURVEY-PHOTO-REVIEW", CommandFlags.Modal)]
        [CommandMethod("SURVEYPHOTOREVIEW", CommandFlags.Modal)]
        [CommandMethod("CLV-PHOTO-REVIEW", CommandFlags.Modal)]
        [CommandMethod("VIEWPHOTOS", CommandFlags.Modal)]
        [CommandMethod("VIEW-PHOTOS", CommandFlags.Modal)]
        public static void OpenSurveyPhotoReview()
        {
            EnsurePalette();
            if (_paletteSet != null)
            {
                PalettePositionHelper.ShowNearAutoCadWindow(
                    _paletteSet,
                    new Size(1080, 760),
                    new Size(760, 520),
                    offsetX: 120,
                    offsetY: 170);
            }

            _control?.BeginInteractiveLoad();
        }

        internal static void EnsurePalette()
        {
            if (_paletteSet != null)
                return;

            _control = new SurveyPhotoReviewControl();
            _paletteSet = new PaletteSet("SURVEY - PHOTO REVIEW")
            {
                DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
            };


            _paletteSet.Add("PHOTO REVIEW", _control);
            PalettePositionHelper.ConfigureSize(
                _paletteSet,
                new Size(1080, 760),
                new Size(760, 520));

        }
    }

    internal sealed class SurveyPhotoReviewControl : UserControl
    {
        private readonly Button _loadButton;
                private readonly Button _openImageButton;
        private readonly Button _openMapButton;
        private readonly Button _zoomCadButton;
        private readonly Label _statusLabel;
        private readonly PictureBox _pictureBox;
        private readonly SplitContainer _mainSplit;
        private readonly PictureBox _mapPreviewBox;
        private readonly TextBox _detailsTextBox;

        private ObjectId _currentEntityId = ObjectId.Null;
        private Point3d? _currentMarkerPoint;
        private string? _currentImagePath;
        private PhotoGeoInfo? _currentGeoInfo;
        private byte[]? _currentImageBytes;

        public SurveyPhotoReviewControl()
        {
            Dock = DockStyle.Fill;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var header = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 8)
            };

            _loadButton = CreateHeaderButton("SELECT IMAGE");
            _openImageButton = CreateHeaderButton("OPEN IMAGE");
            _openMapButton = CreateHeaderButton("OPEN MAP");
            _zoomCadButton = CreateHeaderButton("ZOOM CAD");
            _statusLabel = new Label
            {
                AutoSize = true,
                Padding = new Padding(6, 8, 0, 0),
                Text = "Select a geotagged photo marker to begin."
            };

            _loadButton.Click += (_, _) => BeginInteractiveLoad();
            _openImageButton.Click += (_, _) => OpenCurrentImage();
            _openMapButton.Click += (_, _) => OpenCurrentMap();
            _zoomCadButton.Click += (_, _) => ZoomToCurrentMarker();

            header.Controls.Add(_loadButton);
            header.Controls.Add(_openImageButton);
            header.Controls.Add(_openMapButton);
            header.Controls.Add(_zoomCadButton);
            header.Controls.Add(_statusLabel);

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            var imagePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0)
            };
            imagePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            imagePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            imagePanel.Controls.Add(CreateSectionLabel("IMAGE"), 0, 0);

            var imageHolder = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true,
                BackColor = SystemColors.ControlDark
            };

            _pictureBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            imageHolder.Controls.Add(_pictureBox);
            imagePanel.Controls.Add(imageHolder, 0, 1);

            var rightPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0)
            };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

            rightPanel.Controls.Add(CreateSectionLabel("MAP"), 0, 0);
            var mapHolder = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true,
                BackColor = SystemColors.ControlLightLight
            };
            _mapPreviewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = SystemColors.ControlLightLight,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            mapHolder.Controls.Add(_mapPreviewBox);
            rightPanel.Controls.Add(mapHolder, 0, 1);

            rightPanel.Controls.Add(CreateSectionLabel("DETAILS"), 0, 2);
            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9f)
            };
            rightPanel.Controls.Add(_detailsTextBox, 0, 3);

            _mainSplit.Panel1.Controls.Add(imagePanel);
            _mainSplit.Panel2.Controls.Add(rightPanel);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_mainSplit, 0, 1);
            Controls.Add(root);

            Load += (_, _) => InitializeSplitLayout();
            SizeChanged += (_, _) => InitializeSplitLayout();

            RefreshButtonState();
            ShowPlaceholder();
        }


        private void InitializeSplitLayout()
        {
            if (_mainSplit.IsDisposed)
                return;

            int availableWidth = _mainSplit.ClientSize.Width;
            if (availableWidth <= 0)
                return;

            const int minPanelWidth = 300;
            int splitterWidth = Math.Max(4, _mainSplit.SplitterWidth);
            int requiredWidth = (minPanelWidth * 2) + splitterWidth;

            if (availableWidth < requiredWidth)
            {
                // The palette is still too small during startup/layout negotiation.
                // Do not apply min sizes yet, because WinForms will throw if the
                // current splitter distance cannot satisfy both panel minimums.
                return;
            }

            _mainSplit.Panel1MinSize = minPanelWidth;
            _mainSplit.Panel2MinSize = minPanelWidth;

            int target = Math.Max(minPanelWidth, Math.Min(availableWidth - minPanelWidth - splitterWidth, availableWidth / 2));
            if (_mainSplit.SplitterDistance != target)
                _mainSplit.SplitterDistance = target;

        }

        public void BeginInteractiveLoad()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;
            if (doc == null || ed == null)
                return;

            try
            {
                var peo = new PromptEntityOptions("\nSelect photo image marker: ");
                peo.SetRejectMessage("\nSelect a photo marker or block with a photo hyperlink.");
                peo.AddAllowedClass(typeof(Entity), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                LoadFromEntityId(per.ObjectId);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-PHOTO-REVIEW failed: {ex.Message}");
                SetStatus($"Load failed: {ex.Message}");
            }
        }

        private void LoadFromEntityId(ObjectId entityId)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            Database? db = doc?.Database;
            Editor? ed = doc?.Editor;
            if (doc == null || db == null || ed == null)
                return;

            string? imagePath = null;
            string entityType = string.Empty;
            Point3d markerPoint = Point3d.Origin;
            bool markerPointFound = false;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity? entity = tr.GetObject(entityId, OpenMode.ForRead, false) as Entity;
                if (entity == null)
                {
                    ed.WriteMessage("\nSURVEY-PHOTO-REVIEW: Selected object is not a valid entity.");
                    SetStatus("Selected object is not a valid entity.");
                    return;
                }

                entityType = entity.GetType().Name;
                imagePath = TryGetHyperlinkPath(entity);
                markerPointFound = TryGetMarkerPoint(entity, out markerPoint);
                tr.Commit();
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                ed.WriteMessage("\nSURVEY-PHOTO-REVIEW: No photo hyperlink was found on the selected object.");
                SetStatus("No photo hyperlink found on the selected object.");
                return;
            }

            if (!File.Exists(imagePath))
            {
                ed.WriteMessage($"\nSURVEY-PHOTO-REVIEW: Image file was not found: {imagePath}");
                SetStatus("Image file was not found.");
                _currentEntityId = entityId;
                _currentImagePath = imagePath;
                _currentGeoInfo = null;
                _currentMarkerPoint = markerPointFound ? markerPoint : (Point3d?)null;
                _currentImageBytes = null;
                ShowMissingFileDetails(entityType, imagePath, markerPointFound ? markerPoint : (Point3d?)null);
                RefreshButtonState();
                return;
            }

            PhotoGeoInfo? geoInfo = PhotoExifReader.TryReadGeoInfo(imagePath);
            _currentImageBytes = File.ReadAllBytes(imagePath);
            using (var ms = new MemoryStream(_currentImageBytes))
            using (var image = DrawingImage.FromStream(ms))
            {
                _pictureBox.Image?.Dispose();
                _pictureBox.Image = new Bitmap(image);
            }

            _currentEntityId = entityId;
            _currentImagePath = imagePath;
            _currentGeoInfo = geoInfo;
            _currentMarkerPoint = markerPointFound ? markerPoint : (Point3d?)null;

            UpdateMapBrowser();
            UpdateDetails(entityType);
            SetStatus($"Loaded: {Path.GetFileName(imagePath)}");
            RefreshButtonState();
        }

        private static string? TryGetHyperlinkPath(Entity entity)
        {
            var hyperlinks = entity.Hyperlinks;
            if (hyperlinks == null || hyperlinks.Count == 0)
                return null;

            foreach (HyperLink hyperlink in hyperlinks)
            {
                string url = hyperlink.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }

            return null;
        }

        private static bool TryGetMarkerPoint(Entity entity, out Point3d point)
        {
            switch (entity)
            {
                case BlockReference br:
                    point = br.Position;
                    return true;
                case DBPoint dbPoint:
                    point = dbPoint.Position;
                    return true;
                default:
                    try
                    {
                        Extents3d extents = entity.GeometricExtents;
                        point = new Point3d(
                            (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                            (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                            (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
                        return true;
                    }
                    catch
                    {
                        point = Point3d.Origin;
                        return false;
                    }
            }
        }

        private void UpdateMapBrowser()
        {
            _mapPreviewBox.Image?.Dispose();
            _mapPreviewBox.Image = null;

            if (_currentGeoInfo == null)
            {
                _mapPreviewBox.Image = BuildMessageBitmap(
                    "Map preview unavailable",
                    "No GPS EXIF data was found in the image.");
                return;
            }

            try
            {
                _mapPreviewBox.Image = BuildStaticTilePreview(_currentGeoInfo, 640, 420);
            }
            catch (System.Exception ex)
            {
                _mapPreviewBox.Image = BuildMessageBitmap(
                    "Map preview unavailable",
                    "Tile preview failed. Use OPEN MAP for the browser view.\n" + ex.Message);
            }
        }

        private void UpdateDetails(string entityType)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SURVEY PHOTO REVIEW");
            sb.AppendLine(new string('-', 72));
            sb.AppendLine($"Entity Type : {entityType}");
            sb.AppendLine($"Image File  : {_currentImagePath ?? string.Empty}");

            if (!string.IsNullOrWhiteSpace(_currentImagePath) && File.Exists(_currentImagePath))
            {
                var fi = new FileInfo(_currentImagePath);
                sb.AppendLine($"File Size   : {fi.Length:N0} bytes");
                sb.AppendLine($"Modified    : {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }

            if (_currentMarkerPoint.HasValue)
            {
                Point3d pt = _currentMarkerPoint.Value;
                sb.AppendLine($"CAD Marker  : X={pt.X:0.###}, Y={pt.Y:0.###}, Z={pt.Z:0.###}");
            }
            else
            {
                sb.AppendLine("CAD Marker  : <not available>");
            }

            if (_currentGeoInfo != null)
            {
                sb.AppendLine($"Latitude    : {_currentGeoInfo.Latitude.ToString("0.000000", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"Longitude   : {_currentGeoInfo.Longitude.ToString("0.000000", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"Map Link    : {BuildGoogleMapsPinnedUrl(_currentGeoInfo.Latitude, _currentGeoInfo.Longitude)}");

                if (_currentGeoInfo.HeadingDegrees.HasValue)
                    sb.AppendLine($"Heading     : {_currentGeoInfo.HeadingDegrees.Value.ToString("0.##", CultureInfo.InvariantCulture)}°");
                else
                    sb.AppendLine("Heading     : <not available>");
            }
            else
            {
                sb.AppendLine("Latitude    : <not available>");
                sb.AppendLine("Longitude   : <not available>");
                sb.AppendLine("Heading     : <not available>");
            }

            sb.AppendLine();
            sb.AppendLine("Notes:");
            sb.AppendLine("- This prototype reads the photo hyperlink from the selected CAD entity.");
            sb.AppendLine("- GPS / heading come from the image EXIF when available.");
            sb.AppendLine("- The embedded map uses online tile previews so the operator can keep image + map together in one palette.");
            sb.AppendLine("- OPEN MAP launches Google Maps in the browser with a pinned marker for the same GPS point.");
            sb.AppendLine("- The image remains the primary review surface for sewer / underground field verification.");

            _detailsTextBox.Text = sb.ToString();
        }

        private void ShowPlaceholder()
        {
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;
            _mapPreviewBox.Image?.Dispose();
            _mapPreviewBox.Image = BuildMessageBitmap(
                "SURVEY PHOTO REVIEW",
                "Click SELECT IMAGE, then select one of the imported field-photo markers that already contains a photo hyperlink.");
            _detailsTextBox.Text =
                "SURVEY PHOTO REVIEW\r\n" +
                "------------------------------------------------------------------------\r\n" +
                "Nothing loaded yet.\r\n\r\n" +
                "Expected workflow:\r\n" +
                "1. Run VIEW PHOTOS from the CLV menu, or run the CLV-PHOTO-REVIEW command.\r\n" +
                "2. Select a field-photo marker created by the existing CLV FIELD PHOTOS workflow.\r\n" +
                "3. Review the image on the left and the map context on the right.\r\n" +
                "4. Use OPEN MAP to launch Google Maps with a pinned marker for the same photo location.\r\n";
        }

        private void ShowMissingFileDetails(string entityType, string imagePath, Point3d? markerPoint)
        {
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;
            _mapPreviewBox.Image?.Dispose();
            _mapPreviewBox.Image = BuildMessageBitmap(
                "Image file not found",
                "The selected CAD marker contains a photo hyperlink, but the photo file was not found on disk. Check whether the project folder moved or the network path changed.");

            var sb = new StringBuilder();
            sb.AppendLine("SURVEY PHOTO REVIEW");
            sb.AppendLine(new string('-', 72));
            sb.AppendLine($"Entity Type : {entityType}");
            sb.AppendLine($"Image File  : {imagePath}");
            if (markerPoint.HasValue)
            {
                Point3d pt = markerPoint.Value;
                sb.AppendLine($"CAD Marker  : X={pt.X:0.###}, Y={pt.Y:0.###}, Z={pt.Z:0.###}");
            }
            sb.AppendLine();
            sb.AppendLine("The linked file could not be found. The CAD marker is still available for local review.");
            _detailsTextBox.Text = sb.ToString();
        }

        private void OpenCurrentImage()
        {
            if (string.IsNullOrWhiteSpace(_currentImagePath) || !File.Exists(_currentImagePath))
            {
                SetStatus("No image file is available to open.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _currentImagePath,
                UseShellExecute = true
            });
        }

        private void OpenCurrentMap()
        {
            if (_currentGeoInfo == null)
            {
                SetStatus("No GPS EXIF data is available for external map launch.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = BuildGoogleMapsPinnedUrl(_currentGeoInfo.Latitude, _currentGeoInfo.Longitude),
                UseShellExecute = true
            });
        }

        private void ZoomToCurrentMarker()
        {
            if (!_currentMarkerPoint.HasValue)
            {
                SetStatus("No CAD marker point is available to zoom to.");
                return;
            }

            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Point3d pt = _currentMarkerPoint.Value;
            string x = pt.X.ToString("0.###", CultureInfo.InvariantCulture);
            string y = pt.Y.ToString("0.###", CultureInfo.InvariantCulture);
            string z = pt.Z.ToString("0.###", CultureInfo.InvariantCulture);

            doc.SendStringToExecute($"_.ZOOM _C {x},{y},{z} 60 ", true, false, false);
            SetStatus("Zoomed CAD to the current photo marker.");
        }

        private void RefreshButtonState()
        {
            bool hasImagePath = !string.IsNullOrWhiteSpace(_currentImagePath) && File.Exists(_currentImagePath);
            bool hasGeo = _currentGeoInfo != null;
            bool hasMarker = _currentMarkerPoint.HasValue;
            bool hasCurrent = !_currentEntityId.IsNull;

            _openImageButton.Enabled = hasImagePath;
            _openMapButton.Enabled = hasGeo;
            _zoomCadButton.Enabled = hasMarker;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private static Button CreateHeaderButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 170,
                Height = 28,
                Margin = new Padding(0, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 4),
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        private static string BuildOpenStreetMapUrl(double latitude, double longitude)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "https://www.openstreetmap.org/?mlat={0}&mlon={1}#map=19/{0}/{1}",
                latitude,
                longitude);
        }

        private static string BuildGoogleMapsPinnedUrl(double latitude, double longitude)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "https://maps.google.com/maps?q=loc:{0},{1}",
                latitude.ToString("0.000000", CultureInfo.InvariantCulture),
                longitude.ToString("0.000000", CultureInfo.InvariantCulture));
        }

        private static Bitmap BuildStaticTilePreview(PhotoGeoInfo geoInfo, int width, int height)
        {
            const int zoom = 19;
            const int tileSize = 256;

            double centerTileX = LonToTileX(geoInfo.Longitude, zoom);
            double centerTileY = LatToTileY(geoInfo.Latitude, zoom);

            int baseTileX = (int)Math.Floor(centerTileX) - 1;
            int baseTileY = (int)Math.Floor(centerTileY) - 1;

            using var stitched = new Bitmap(tileSize * 3, tileSize * 3);
            using (Graphics g = Graphics.FromImage(stitched))
            {
                g.Clear(Color.WhiteSmoke);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                for (int dx = 0; dx < 3; dx++)
                {
                    for (int dy = 0; dy < 3; dy++)
                    {
                        int tx = baseTileX + dx;
                        int ty = baseTileY + dy;
                        using Bitmap tile = DownloadTileBitmap(zoom, tx, ty, tileSize);
                        g.DrawImage(tile, dx * tileSize, dy * tileSize, tileSize, tileSize);
                    }
                }
            }

            double px = (centerTileX - baseTileX) * tileSize;
            double py = (centerTileY - baseTileY) * tileSize;
            int cropX = (int)Math.Round(px - (width / 2.0));
            int cropY = (int)Math.Round(py - (height / 2.0));
            cropX = Math.Max(0, Math.Min(stitched.Width - width, cropX));
            cropY = Math.Max(0, Math.Min(stitched.Height - height, cropY));

            Rectangle srcRect = new Rectangle(cropX, cropY, Math.Min(width, stitched.Width), Math.Min(height, stitched.Height));
            Bitmap result = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.WhiteSmoke);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(stitched, new Rectangle(0, 0, width, height), srcRect, GraphicsUnit.Pixel);

                float markerX = (float)(px - cropX);
                float markerY = (float)(py - cropY);
                DrawMarker(g, markerX, markerY, geoInfo.HeadingDegrees);
                DrawMapHeader(g, geoInfo, width);
            }

            return result;
        }

        private static void DrawMapHeader(Graphics g, PhotoGeoInfo geoInfo, int width)
        {
            using var bg = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            using var border = new Pen(Color.LightGray, 1f);
            using var font = new DrawingFont(SystemFonts.DefaultFont.FontFamily, 9f, System.Drawing.FontStyle.Regular);
            string headingText = geoInfo.HeadingDegrees.HasValue
                ? geoInfo.HeadingDegrees.Value.ToString("0.##", CultureInfo.InvariantCulture) + "°"
                : "n/a";
            string text = string.Format(CultureInfo.InvariantCulture,
                "Lat {0}, Lon {1}, Heading {2}",
                geoInfo.Latitude.ToString("0.000000", CultureInfo.InvariantCulture),
                geoInfo.Longitude.ToString("0.000000", CultureInfo.InvariantCulture),
                headingText);
            Rectangle rect = new Rectangle(8, 8, width - 16, 24);
            g.FillRectangle(bg, rect);
            g.DrawRectangle(border, rect);
            g.DrawString(text, font, Brushes.Black, new PointF(14, 13));
        }

        private static void DrawMarker(Graphics g, float x, float y, double? headingDegrees)
        {
            using var redBrush = new SolidBrush(Color.Red);
            using var whitePen = new Pen(Color.White, 3f);
            using var redPen = new Pen(Color.Red, 2f);
            g.FillEllipse(redBrush, x - 6, y - 6, 12, 12);
            g.DrawEllipse(Pens.White, x - 6, y - 6, 12, 12);

            if (headingDegrees.HasValue)
            {
                double radians = (headingDegrees.Value - 90.0) * Math.PI / 180.0;
                float len = 30f;
                float endX = x + (float)(Math.Cos(radians) * len);
                float endY = y + (float)(Math.Sin(radians) * len);
                g.DrawLine(whitePen, x, y, endX, endY);
                g.DrawLine(redPen, x, y, endX, endY);
                g.FillEllipse(redBrush, endX - 3, endY - 3, 6, 6);
            }
        }

        private static Bitmap DownloadTileBitmap(int zoom, int tileX, int tileY, int tileSize)
        {
            int max = 1 << zoom;
            tileX = ((tileX % max) + max) % max;
            tileY = Math.Max(0, Math.Min(max - 1, tileY));

            string[] urls =
            {
                string.Format(CultureInfo.InvariantCulture, "https://tile.openstreetmap.org/{0}/{1}/{2}.png", zoom, tileX, tileY),
                string.Format(CultureInfo.InvariantCulture, "https://a.tile.openstreetmap.org/{0}/{1}/{2}.png", zoom, tileX, tileY),
                string.Format(CultureInfo.InvariantCulture, "https://b.tile.openstreetmap.org/{0}/{1}/{2}.png", zoom, tileX, tileY),
                string.Format(CultureInfo.InvariantCulture, "https://c.tile.openstreetmap.org/{0}/{1}/{2}.png", zoom, tileX, tileY),
                string.Format(CultureInfo.InvariantCulture, "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{0}/{2}/{1}", zoom, tileX, tileY)
            };

            List<string> failures = new();
            foreach (string url in urls)
            {
                try
                {
                    byte[] bytes = DownloadMapBytes(url);
                    using var ms = new MemoryStream(bytes);
                    using var img = DrawingImage.FromStream(ms);
                    return new Bitmap(img, new Size(tileSize, tileSize));
                }
                catch (System.Exception ex)
                {
                    failures.Add(ex.Message);
                }
            }

            throw new InvalidOperationException("Unable to download map tile. " + string.Join(" | ", failures));
        }

        private static byte[] DownloadMapBytes(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = WebRequestMethods.Http.Get;
            request.Accept = "image/png,image/*;q=0.8,*/*;q=0.5";
            request.UserAgent = "CLV_CivilTools/2026 Survey Photo Review map preview";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.KeepAlive = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            IWebProxy? proxy = WebRequest.DefaultWebProxy;
            if (proxy != null)
            {
                proxy.Credentials = CredentialCache.DefaultCredentials;
                request.Proxy = proxy;
            }

            using WebResponse response = request.GetResponse();
            using Stream? stream = response.GetResponseStream();
            if (stream == null)
                throw new IOException("Map tile response did not contain a data stream.");

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static double LonToTileX(double lon, int zoom)
        {
            double n = Math.Pow(2.0, zoom);
            return ((lon + 180.0) / 360.0) * n;
        }

        private static double LatToTileY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            double n = Math.Pow(2.0, zoom);
            return (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
        }

        private static Bitmap BuildMessageBitmap(string title, string body)
        {
            Bitmap bmp = new Bitmap(640, 420);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(244, 246, 248));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var titleFont = new DrawingFont(SystemFonts.DefaultFont.FontFamily, 14f, System.Drawing.FontStyle.Bold);
            using var bodyFont = new DrawingFont(SystemFonts.DefaultFont.FontFamily, 10f, System.Drawing.FontStyle.Regular);
            using var titleBrush = new SolidBrush(Color.FromArgb(31, 41, 55));
            using var bodyBrush = new SolidBrush(Color.FromArgb(55, 65, 81));
            g.DrawString(title, titleFont, titleBrush, new RectangleF(18, 18, bmp.Width - 36, 36));
            g.DrawString(body, bodyFont, bodyBrush, new RectangleF(18, 64, bmp.Width - 36, bmp.Height - 82));
            using var border = new Pen(Color.FromArgb(203, 213, 225), 1f);
            g.DrawRectangle(border, 0, 0, bmp.Width - 1, bmp.Height - 1);
            return bmp;
        }
    }

    internal sealed class PhotoGeoInfo
    {
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double? HeadingDegrees { get; init; }
    }

    internal static class PhotoExifReader
    {
        private const int PropertyIdGpsLatitudeRef = 0x0001;
        private const int PropertyIdGpsLatitude = 0x0002;
        private const int PropertyIdGpsLongitudeRef = 0x0003;
        private const int PropertyIdGpsLongitude = 0x0004;
        private const int PropertyIdGpsImgDirection = 0x0011;

        public static PhotoGeoInfo? TryReadGeoInfo(string imagePath)
        {
            try
            {
                using var image = DrawingImage.FromFile(imagePath);
                if (!TryReadCoordinate(image, PropertyIdGpsLatitude, PropertyIdGpsLatitudeRef, out double latitude) ||
                    !TryReadCoordinate(image, PropertyIdGpsLongitude, PropertyIdGpsLongitudeRef, out double longitude))
                {
                    return null;
                }

                double? heading = TryReadRational(image, PropertyIdGpsImgDirection);
                return new PhotoGeoInfo
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    HeadingDegrees = heading
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadCoordinate(DrawingImage image, int coordinateId, int referenceId, out double value)
        {
            value = 0.0;
            PropertyItem? coordinateItem = TryGetProperty(image, coordinateId);
            PropertyItem? referenceItem = TryGetProperty(image, referenceId);
            if (coordinateItem == null || referenceItem == null)
                return false;

            string direction = ReadAscii(referenceItem).Trim().ToUpperInvariant();
            byte[] bytes = coordinateItem.Value ?? Array.Empty<byte>();
            if (bytes.Length < 24)
                return false;

            double degrees = ReadUnsignedRational(bytes, 0);
            double minutes = ReadUnsignedRational(bytes, 8);
            double seconds = ReadUnsignedRational(bytes, 16);
            value = degrees + (minutes / 60.0) + (seconds / 3600.0);

            if (direction == "S" || direction == "W")
                value = -value;

            return true;
        }

        private static double? TryReadRational(DrawingImage image, int propertyId)
        {
            PropertyItem? item = TryGetProperty(image, propertyId);
            if (item == null || item.Value == null || item.Value.Length < 8)
                return null;

            return ReadUnsignedRational(item.Value!, 0);
        }

        private static PropertyItem? TryGetProperty(DrawingImage image, int propertyId)
        {
            try
            {
                return image.GetPropertyItem(propertyId);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadAscii(PropertyItem item)
        {
            return Encoding.ASCII.GetString(item.Value ?? Array.Empty<byte>()).TrimEnd('\0', ' ');
        }

        private static double ReadUnsignedRational(IReadOnlyList<byte> buffer, int startIndex)
        {
            uint numerator = BitConverter.ToUInt32(new[]
            {
                buffer[startIndex + 0],
                buffer[startIndex + 1],
                buffer[startIndex + 2],
                buffer[startIndex + 3]
            }, 0);

            uint denominator = BitConverter.ToUInt32(new[]
            {
                buffer[startIndex + 4],
                buffer[startIndex + 5],
                buffer[startIndex + 6],
                buffer[startIndex + 7]
            }, 0);

            if (denominator == 0)
                return 0.0;

            return numerator / (double)denominator;
        }
    }
}
