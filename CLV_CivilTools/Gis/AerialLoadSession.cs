using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Gis.Map.Platform;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Timer = System.Windows.Forms.Timer;

namespace CLV_CivilTools.Gis
{
    // Owns only one import's completion work. Never keeps restoring after user input.
    internal sealed class AerialLoadSession : IDisposable
    {
        private readonly Document _doc;
        private readonly AcMapMap _map;
        private readonly ViewTableRecord _view;
        private readonly Timer _timer = new() { Interval = 150 };
        private readonly DateTime _started = DateTime.UtcNow;
        private DateTime _changed = DateTime.UtcNow;
        private Action? _finish;
        private Func<bool>? _layersReady;
        private readonly TaskCompletionSource<bool> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Completion => _finished.Task;
        private bool _complete, _pending = true, _returned, _disposed, _applying, _viewReleased;
        private readonly AllLayerLoadingFinishedHandler _onComplete;
        private readonly LayerAddedHandler _onAdded;
        private readonly LayerInitializedHandler _onInitialized;

        internal AerialLoadSession(Document doc, AcMapMap map, ViewTableRecord? requestedView = null)
        {
            _doc = doc; _map = map;
            _view = requestedView == null ? doc.Editor.GetCurrentView() : (ViewTableRecord)requestedView.Clone();
            _onComplete = (_, _) => { _complete = true; Changed(); };
            _onAdded = (_, _) => Changed();
            _onInitialized = (_, _) => Changed();
            map.AllLayerLoadingFinished += _onComplete;
            map.LayerAdded += _onAdded;
            map.LayerInitialized += _onInitialized;
            doc.CommandWillStart += OnCommandStart;
            doc.CommandEnded += OnCommandEnd;
            doc.CommandCancelled += OnCommandEnd;
            doc.CommandFailed += OnCommandEnd;
            AcadApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
            AcadApp.PreTranslateMessage += OnInput;
            _timer.Tick += OnTick;
            _timer.Start();
            Log("begin; " + ViewText(_view) + "; DLL " + typeof(Aerials).Assembly.Location);
        }

        internal void Returned(Action finish, Func<bool> layersReady) { _finish = finish; _layersReady = layersReady; _returned = true; Changed(); }
        private void Changed() { if (!_applying && !_disposed) { _pending = true; _changed = DateTime.UtcNow; } }

        // Native Map may issue its own ZOOM after LoadLayer returns. Physical input is
        // filtered separately, so a user's zoom/pan cancels instead of being undone.
        private void OnCommandStart(object sender, CommandEventArgs e)
        {
            if (!_returned) return;
            string name = e.GlobalCommandName.ToUpperInvariant();
            if (name is "ZOOM" or "PAN" or "3DORBIT" or "REGEN" or "REGENALL" or "NM_AERIAL_STATE") Changed();
            else { Log("completion canceled by command " + name); Dispose(); }
        }
        private void OnCommandEnd(object sender, CommandEventArgs e) { Changed(); }
        private void OnDocumentClosing(object sender, DocumentCollectionEventArgs e) { if (ReferenceEquals(e.Document, _doc)) Dispose(); }
        private void OnInput(object sender, PreTranslateMessageEventArgs e)
        {
            if (!_returned || _disposed) return;
            try
            {
                // The host exposes WPF's MSG struct. Read only its message number so
                // this WinForms project does not acquire a WPF build dependency.
                object message = e.GetType().GetProperty("Message")?.GetValue(e)
                    ?? throw new InvalidOperationException("Host message is unavailable.");
                object number = message.GetType().GetProperty("message")?.GetValue(message)
                    ?? throw new InvalidOperationException("Host MSG.message property is unavailable.");
                int code = Convert.ToInt32(number);
                if (code is 0x0100 or 0x0104 or 0x0201 or 0x0204 or 0x0207 or 0x020A or 0x020E) _viewReleased = true;
            }
            catch (System.Exception ex)
            {
                // Input observation is optional. Never cancel grouping/order completion
                // because its message adapter failed. Release view ownership to the user.
                _viewReleased = true;
                AcadApp.PreTranslateMessage -= OnInput;
                Log("input guard disabled; group/order completion continues; saved-view ownership released: " + ex.Message);
            }
        }

        internal void RestoreNow()
        {
            if (_disposed || _viewReleased || !ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) return;
            _doc.Editor.SetCurrentView(_view);
            using ViewTableRecord actual = _doc.Editor.GetCurrentView();
            double size = Math.Max(1, Math.Max(_view.Width, _view.Height));
            bool near(double a, double b) => double.IsFinite(a) && Math.Abs(a - b) <= size * 1e-8;
            if (!near(actual.CenterPoint.X, _view.CenterPoint.X) || !near(actual.CenterPoint.Y, _view.CenterPoint.Y)
                || !near(actual.Width, _view.Width) || !near(actual.Height, _view.Height)
                || !near(actual.Target.X, _view.Target.X) || !near(actual.Target.Y, _view.Target.Y) || !near(actual.Target.Z, _view.Target.Z)
                || Math.Abs(Math.IEEERemainder(actual.ViewTwist - _view.ViewTwist, 2 * Math.PI)) > 1e-8
                || actual.PerspectiveEnabled != _view.PerspectiveEnabled
                || actual.ViewDirection.GetNormal().DotProduct(_view.ViewDirection.GetNormal()) < 1 - 1e-10)
                throw new InvalidOperationException("Viewport restore mismatch; saved " + ViewText(_view) + "; actual " + ViewText(actual));
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_disposed || !_returned) return;
            if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) { Dispose(); return; }
            if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(60))
            { Log("completion wait expired; native-finished=" + _complete + ". Run NM_AERIAL_STATE if the view/order is wrong."); Dispose(); return; }
            if (!string.IsNullOrEmpty(_doc.CommandInProgress) || !_doc.Editor.IsQuiescent || DateTime.UtcNow - _changed < TimeSpan.FromMilliseconds(500)) return;
            bool layersReady;
            try { using (_doc.LockDocument()) layersReady = _layersReady?.Invoke() == true; }
            catch (System.Exception ex) { Log("readiness check failed: " + ex.Message); Dispose(); return; }
            if (!_pending && !(_complete || layersReady)) return;
            if (!_complete && !layersReady)
            {
                if (_pending)
                {
                    try { using (_doc.LockDocument()) RestoreNow(); }
                    catch (System.Exception ex) { Log("interim view restore failed: " + ex.Message); }
                    _pending = false;
                }
                return;
            }
            if (!_complete && layersReady && DateTime.UtcNow - _started < TimeSpan.FromSeconds(2)) return;
            _pending = false; _applying = true;
            try
            {
                using (_doc.LockDocument())
                {
                    // An ordering failure must never skip restoration of the captured view.
                    try { _finish?.Invoke(); }
                    finally { RestoreNow(); }
                }
                Log("settled; native-finished=" + _complete + "; layers-loaded=" + layersReady + "; order checked; saved-view-restored=" + !_viewReleased + "; " + ViewText(_view));
                if (_complete || layersReady) Dispose();
            }
            catch (System.Exception ex) { Log("finalization failed: " + ex.Message + ". Run NM_AERIAL_STATE and send the output."); Dispose(); }
            finally { _applying = false; }
        }

        internal string Status => "returned=" + _returned + "; native-finished=" + _complete + "; pending=" + _pending + "; disposed=" + _disposed + "; view-released=" + _viewReleased;
        private void Log(string text) => _doc.Editor.WriteMessage("\n[AERIAL-R4] " + text);
        internal static string ViewText(ViewTableRecord v) => string.Format(CultureInfo.InvariantCulture, "center=({0:0.###},{1:0.###}); height={2:0.######}; width={3:0.######}", v.CenterPoint.X, v.CenterPoint.Y, v.Height, v.Width);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _timer.Stop(); _timer.Dispose();
            AcadApp.PreTranslateMessage -= OnInput;
            _map.AllLayerLoadingFinished -= _onComplete; _map.LayerAdded -= _onAdded; _map.LayerInitialized -= _onInitialized;
            _doc.CommandWillStart -= OnCommandStart; _doc.CommandEnded -= OnCommandEnd;
            _doc.CommandCancelled -= OnCommandEnd; _doc.CommandFailed -= OnCommandEnd;
            AcadApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
            _view.Dispose(); _finish = null; _layersReady = null; _finished.TrySetResult(true);
        }
    }
}
