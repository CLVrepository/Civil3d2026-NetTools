using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisLocateAddressCommands
    {
        [CommandMethod("CLV-LOCATE", CommandFlags.Modal)]
        [CommandMethod("CLVLOCATE", CommandFlags.Modal)]
        public static void ShowLocateDialogCommand()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                EnsureHybridMap(doc);
                using var form = new GisLocateAddressForm(doc);
                AcadApp.ShowModalDialog(form);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nCLV-LOCATE failed: {ex.Message}");
            }
        }

        public static void ShowLocateDialogFromPalette()
        {
            ShowLocateDialogCommand();
        }

        private static void EnsureHybridMap(Document doc)
        {
            try
            {
                doc.SendStringToExecute("._GEOMAP _Hybrid ", true, false, false);
            }
            catch
            {
            }
        }

    }

    internal sealed class GisLocateSettings
    {
        public const string SettingsFileName = "CLV-LOCATE-SETTINGS.md";
        public const string ServerSettingsFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\AccessControl";
        public const string HelperLispPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_LOCATE_HELPERS.lsp";

        public int ResultLimit { get; init; } = 5;
        public double ZoomHalfWindow { get; init; } = 250.0;
        public double MarkerRadius { get; init; } = 20.0;
        public string BiasCoordinates { get; init; } = string.Empty;
        public string CountryCodes { get; init; } = "us";
        public string SearchSuffix { get; init; } = "Las Vegas, Nevada";
        public string ContactEmail { get; init; } = string.Empty;

        public string SettingsPath => Path.Combine(ServerSettingsFolder, SettingsFileName);

        public static GisLocateSettings Load(Editor? ed)
        {
            string path = Path.Combine(ServerSettingsFolder, SettingsFileName);
            if (!File.Exists(path))
            {
                ed?.WriteMessage($"\nCLV-LOCATE settings file not found: {path}");
                return new GisLocateSettings();
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)
                    || line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith(";", StringComparison.Ordinal)
                    || line.StartsWith(">", StringComparison.Ordinal))
                {
                    continue;
                }

                int colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                string key = line[..colonIndex].Trim();
                string value = line[(colonIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    values[key] = value;
            }

            return new GisLocateSettings
            {
                ResultLimit = ClampInt(GetTrimmed(values, "TOP", "5"), 1, 10, 5),
                ZoomHalfWindow = ClampDouble(GetTrimmed(values, "ZOOM_HALF_WINDOW", "250"), 25.0, 5000.0, 250.0),
                MarkerRadius = ClampDouble(GetTrimmed(values, "MARKER_RADIUS", "20"), 1.0, 1000.0, 20.0),
                BiasCoordinates = GetTrimmed(values, "BIAS_COORDINATES"),
                CountryCodes = GetTrimmed(values, "COUNTRYCODES", "us"),
                SearchSuffix = GetTrimmed(values, "SEARCH_SUFFIX", "Las Vegas, Nevada"),
                ContactEmail = GetTrimmed(values, "CONTACT_EMAIL")
            };
        }

        private static string GetTrimmed(Dictionary<string, string> values, string key, string fallback = "")
        {
            return values.TryGetValue(key, out string? value) ? value.Trim() : fallback;
        }

        private static int ClampInt(string raw, int min, int max, int fallback)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? Math.Max(min, Math.Min(max, value))
                : fallback;
        }

        private static double ClampDouble(string raw, double min, double max, double fallback)
        {
            return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
                ? Math.Max(min, Math.Min(max, value))
                : fallback;
        }
    }

    internal sealed class GisLocateResult
    {
        public string DisplayText { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
        public string MatchSummary { get; init; } = string.Empty;
        public string ResultType { get; init; } = string.Empty;
        public string FormattedAddress { get; init; } = string.Empty;
        public double Longitude { get; init; }
        public double Latitude { get; init; }

        public override string ToString() => DisplayText;
    }

    internal static class NominatimGeocoder
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly object Gate = new();
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        public static async Task<List<GisLocateResult>> SearchAsync(GisLocateSettings settings, string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException("Enter an address or intersection to search.");

            await RespectThrottleAsync(cancellationToken).ConfigureAwait(false);

            string url = BuildRequestUrl(settings, query.Trim());
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(settings.ContactEmail))
            {
                request.Headers.Referrer = new Uri("mailto:" + settings.ContactEmail.Trim());
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Nominatim request failed ({(int)response.StatusCode}): {json}");

            return ParseResults(json);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CLV_CivilTools", "1.0"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Locate", "V1"));
            return client;
        }

        private static async Task RespectThrottleAsync(CancellationToken cancellationToken)
        {
            TimeSpan delay = TimeSpan.Zero;
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                DateTime earliest = _lastRequestUtc.AddSeconds(1);
                if (earliest > now)
                    delay = earliest - now;
                _lastRequestUtc = now + delay;
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        private static string BuildRequestUrl(GisLocateSettings settings, string query)
        {
            string finalQuery = query;
            if (!string.IsNullOrWhiteSpace(settings.SearchSuffix)
                && finalQuery.IndexOf(settings.SearchSuffix, StringComparison.OrdinalIgnoreCase) < 0)
            {
                finalQuery += ", " + settings.SearchSuffix.Trim();
            }

            var parts = new List<string>
            {
                "https://nominatim.openstreetmap.org/search?format=jsonv2",
                "addressdetails=1",
                "limit=" + settings.ResultLimit.ToString(CultureInfo.InvariantCulture),
                "q=" + Uri.EscapeDataString(finalQuery)
            };

            if (!string.IsNullOrWhiteSpace(settings.CountryCodes))
                parts.Add("countrycodes=" + Uri.EscapeDataString(settings.CountryCodes));

            if (!string.IsNullOrWhiteSpace(settings.ContactEmail))
                parts.Add("email=" + Uri.EscapeDataString(settings.ContactEmail));

            if (TryBuildBiasViewbox(settings.BiasCoordinates, out string viewbox))
            {
                parts.Add("viewbox=" + Uri.EscapeDataString(viewbox));
                parts.Add("bounded=0");
            }

            return string.Join("&", parts);
        }

        private static bool TryBuildBiasViewbox(string biasCoordinates, out string viewbox)
        {
            viewbox = string.Empty;
            if (string.IsNullOrWhiteSpace(biasCoordinates))
                return false;

            string[] parts = biasCoordinates.Split(',');
            if (parts.Length != 2)
                return false;

            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double lon))
                return false;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double lat))
                return false;

            double west = lon - 0.35;
            double east = lon + 0.35;
            double north = lat + 0.25;
            double south = lat - 0.25;
            viewbox = $"{west.ToString(CultureInfo.InvariantCulture)},{north.ToString(CultureInfo.InvariantCulture)},{east.ToString(CultureInfo.InvariantCulture)},{south.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        private static List<GisLocateResult> ParseResults(string json)
        {
            var results = new List<GisLocateResult>();

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return results;

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (!TryReadDouble(item, "lon", out double lon) || !TryReadDouble(item, "lat", out double lat))
                    continue;

                string address = GetString(item, "display_name");
                if (string.IsNullOrWhiteSpace(address))
                    address = $"{lat.ToString("0.000000", CultureInfo.InvariantCulture)}, {lon.ToString("0.000000", CultureInfo.InvariantCulture)}";

                string className = GetString(item, "class");
                string resultType = GetString(item, "type");
                string matchSummary = GetString(item, "addresstype");
                string confidence = TryReadDouble(item, "importance", out double importance)
                    ? importance.ToString("0.000", CultureInfo.InvariantCulture)
                    : string.Empty;

                results.Add(new GisLocateResult
                {
                    DisplayText = BuildDisplayText(address, confidence, resultType, className, matchSummary),
                    Confidence = confidence,
                    MatchSummary = matchSummary,
                    ResultType = resultType,
                    FormattedAddress = address,
                    Longitude = lon,
                    Latitude = lat
                });
            }

            return results;
        }

        private static string BuildDisplayText(string address, string confidence, string resultType, string className, string matchSummary)
        {
            var suffixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(confidence))
                suffixParts.Add("importance=" + confidence.Trim());
            if (!string.IsNullOrWhiteSpace(className))
                suffixParts.Add(className.Trim());
            if (!string.IsNullOrWhiteSpace(resultType))
                suffixParts.Add(resultType.Trim());
            if (!string.IsNullOrWhiteSpace(matchSummary))
                suffixParts.Add(matchSummary.Trim());

            return suffixParts.Count == 0 ? address : address + " [" + string.Join(" | ", suffixParts) + "]";
        }

        private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0.0;
            if (!element.TryGetProperty(propertyName, out JsonElement property))
                return false;

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetDouble(out value);

            if (property.ValueKind == JsonValueKind.String)
                return double.TryParse(property.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

            return false;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
    }

    internal sealed class GisLocateAddressForm : Form
    {
        private readonly Document _document;
        private readonly Editor _editor;
        private readonly GisLocateSettings _settings;
        private readonly TextBox _txtQuery;
        private readonly ListBox _lstResults;
        private readonly Label _lblStatus;
        private readonly Button _btnFind;
        private readonly Button _btnZoom;
        private readonly Button _btnZoomMarker;
        private readonly Button _btnGoogle;
        private readonly CheckBox _chkMarker;

        private CancellationTokenSource? _searchCts;

        public GisLocateAddressForm(Document document)
        {
            _document = document;
            _editor = document.Editor;
            _settings = GisLocateSettings.Load(_editor);

            Text = "CLV LOCATE";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(660, 430);

            var lblQuery = new Label
            {
                Left = 12,
                Top = 14,
                Width = 620,
                Height = 18,
                Text = "Address or intersection"
            };

            _txtQuery = new TextBox
            {
                Left = 12,
                Top = 36,
                Width = 520,
                Height = 26
            };
            _txtQuery.KeyDown += TxtQuery_KeyDown;

            _btnFind = new Button
            {
                Left = 544,
                Top = 34,
                Width = 92,
                Height = 28,
                Text = "Find"
            };
            _btnFind.Click += async (s, e) => await FindAsync().ConfigureAwait(true);

            var lblResults = new Label
            {
                Left = 12,
                Top = 74,
                Width = 620,
                Height = 18,
                Text = "Results"
            };

            _lstResults = new ListBox
            {
                Left = 12,
                Top = 96,
                Width = 624,
                Height = 230
            };
            _lstResults.DoubleClick += (s, e) => ExecuteZoom(insertMarker: _chkMarker?.Checked ?? false);

            _chkMarker = new CheckBox
            {
                Left = 12,
                Top = 338,
                Width = 180,
                Height = 24,
                Text = "Insert temp marker",
                Checked = true
            };

            _btnZoom = new Button
            {
                Left = 12,
                Top = 372,
                Width = 110,
                Height = 30,
                Text = "Zoom"
            };
            _btnZoom.Click += (s, e) => ExecuteZoom(insertMarker: false);

            _btnZoomMarker = new Button
            {
                Left = 132,
                Top = 372,
                Width = 138,
                Height = 30,
                Text = "Zoom + Marker"
            };
            _btnZoomMarker.Click += (s, e) => ExecuteZoom(insertMarker: true);

            _btnGoogle = new Button
            {
                Left = 280,
                Top = 372,
                Width = 146,
                Height = 30,
                Text = "Open Google Maps"
            };
            _btnGoogle.Click += (s, e) => OpenGoogleMaps();

            var btnClose = new Button
            {
                Left = 546,
                Top = 372,
                Width = 90,
                Height = 30,
                Text = "Close",
                DialogResult = DialogResult.Cancel
            };

            _lblStatus = new Label
            {
                Left = 206,
                Top = 339,
                Width = 430,
                Height = 24,
                Text = BuildStatusText()
            };

            Controls.Add(lblQuery);
            Controls.Add(_txtQuery);
            Controls.Add(_btnFind);
            Controls.Add(lblResults);
            Controls.Add(_lstResults);
            Controls.Add(_chkMarker);
            Controls.Add(_lblStatus);
            Controls.Add(_btnZoom);
            Controls.Add(_btnZoomMarker);
            Controls.Add(_btnGoogle);
            Controls.Add(btnClose);

            AcceptButton = _btnFind;
            CancelButton = btnClose;
        }

        private async Task FindAsync()
        {
            string query = _txtQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show(this, "Enter an address or intersection.", "CLV LOCATE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                SetBusyState(true, "Searching Nominatim...");
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = new CancellationTokenSource();

                List<GisLocateResult> results = await NominatimGeocoder.SearchAsync(_settings, query, _searchCts.Token).ConfigureAwait(true);
                _lstResults.BeginUpdate();
                _lstResults.Items.Clear();
                foreach (GisLocateResult result in results)
                    _lstResults.Items.Add(result);
                _lstResults.EndUpdate();

                if (_lstResults.Items.Count > 0)
                {
                    _lstResults.SelectedIndex = 0;
                    _lblStatus.Text = $"Found {_lstResults.Items.Count} result(s).";
                }
                else
                {
                    _lblStatus.Text = "No results returned.";
                }
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "Search canceled.";
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = "Search failed.";
                MessageBox.Show(this, ex.Message, "CLV LOCATE", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusyState(false, _lblStatus.Text);
            }
        }

        private void ExecuteZoom(bool insertMarker)
        {
            if (_lstResults.SelectedItem is not GisLocateResult result)
            {
                MessageBox.Show(this, "Select a result first.", "CLV LOCATE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryGetDrawingCoordinateSystem(out string drawingCs))
            {
                MessageBox.Show(this,
                    "No drawing coordinate system was detected. Assign coordinates first, then try again.",
                    "CLV LOCATE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _editor.WriteMessage("\nCLV-LOCATE: no drawing coordinate system detected.");
                return;
            }

            if (!File.Exists(GisLocateSettings.HelperLispPath))
            {
                MessageBox.Show(this,
                    "Locate helper LISP was not found on the server path. Copy CLV_LOCATE_HELPERS.lsp to the shared LISP folder first.",
                    "CLV LOCATE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _editor.WriteMessage($"\nCLV-LOCATE: helper not found: {GisLocateSettings.HelperLispPath}");
                return;
            }

            string label = SanitizeForLispString(result.FormattedAddress);
            string escapedPath = GisLocateSettings.HelperLispPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string lonText = result.Longitude.ToString("0.########", CultureInfo.InvariantCulture);
            string latText = result.Latitude.ToString("0.########", CultureInfo.InvariantCulture);
            string zoomText = _settings.ZoomHalfWindow.ToString("0.########", CultureInfo.InvariantCulture);
            string radiusText = _settings.MarkerRadius.ToString("0.########", CultureInfo.InvariantCulture);
            string addMarkerText = insertMarker ? "T" : "nil";

            string lisp = $"(progn (vl-load-com) (load \"{escapedPath}\") (clv-locate-zoom-run {lonText} {latText} \"{label}\" {addMarkerText} {zoomText} {radiusText}) (princ)) ";
            _document.SendStringToExecute(lisp, true, false, false);
            _editor.WriteMessage($"\nCLV-LOCATE: queued locate for {result.FormattedAddress} ({latText}, {lonText}) into {drawingCs}.");
            Close();
        }

        private void OpenGoogleMaps()
        {
            if (_lstResults.SelectedItem is not GisLocateResult result)
            {
                MessageBox.Show(this, "Select a result first.", "CLV LOCATE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string url = string.Format(CultureInfo.InvariantCulture,
                "https://maps.google.com/maps?q=loc:{0},{1}",
                result.Latitude,
                result.Longitude);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "CLV LOCATE", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryGetDrawingCoordinateSystem(out string coordinateSystem)
        {
            coordinateSystem = string.Empty;

            try
            {
                using Transaction tr = _document.Database.TransactionManager.StartTransaction();
                ObjectId geoDataId = _document.Database.GeoDataObject;
                if (!geoDataId.IsNull && !geoDataId.IsErased)
                {
                    DBObject? geoDataObject = tr.GetObject(geoDataId, OpenMode.ForRead, false) as DBObject;
                    if (geoDataObject != null)
                    {
                        var csProperty = geoDataObject.GetType().GetProperty("CoordinateSystem");
                        if (csProperty?.GetValue(geoDataObject) is string cs && !string.IsNullOrWhiteSpace(cs))
                        {
                            coordinateSystem = cs.Trim();
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                var method = typeof(GisImportCommands).GetMethod("InferDrawingCoordinateSystem", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (method?.Invoke(null, null) is string detected && !string.IsNullOrWhiteSpace(detected))
                {
                    coordinateSystem = detected.Trim();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void SetBusyState(bool busy, string statusText)
        {
            UseWaitCursor = busy;
            _btnFind.Enabled = !busy;
            _btnZoom.Enabled = !busy;
            _btnZoomMarker.Enabled = !busy;
            _btnGoogle.Enabled = !busy;
            _lstResults.Enabled = !busy;
            _txtQuery.Enabled = !busy;
            _chkMarker.Enabled = !busy;
            _lblStatus.Text = statusText;
        }

        private string BuildStatusText()
        {
            string suffixStatus = string.IsNullOrWhiteSpace(_settings.SearchSuffix) ? "No search suffix" : "Suffix loaded";
            return $"Nominatim public geocoder | {suffixStatus} | Settings: {_settings.SettingsPath}";
        }

        private static string SanitizeForLispString(string value)
        {
            string safe = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            safe = safe.Replace("\\", "/").Replace("\"", "'");
            return safe;
        }

        private async void TxtQuery_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await FindAsync().ConfigureAwait(true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
