using System;
using System.Globalization;
using System.Collections.Generic;
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
        private DateTime? _readyAt;
        private Action? _finish;
        private Func<bool>? _layersReady;
        private bool _complete, _pending = true, _returned, _disposed, _applying, _viewReleased, _finishQueued, _initialApplied, _finishApplied;
        private string _endReason = "active", _stage = "captured", _lastError = "";
        private readonly string _savedViewText;
        private readonly List<string> _trace = new();
        internal string RequestId { get; } = Guid.NewGuid().ToString("N");
        internal bool MatchesDocument(Document doc) => ReferenceEquals(doc, _doc);
        internal string Trace => string.Join(Environment.NewLine, _trace);
        private readonly AllLayerLoadingFinishedHandler _onComplete;
        private readonly LayerAddedHandler _onAdded;
        private readonly LayerInitializedHandler _onInitialized;

        internal AerialLoadSession(Document doc, AcMapMap map, ViewTableRecord? requestedView = null)
        {
            _doc = doc; _map = map;
            _view = requestedView == null ? doc.Editor.GetCurrentView() : (ViewTableRecord)requestedView.Clone();
            _savedViewText = ViewText(_view);
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
            if (!_returned || _applying) return;
            string name = e.GlobalCommandName.ToUpperInvariant();
            // Our own verification must not restart the quiet interval. Map's layer
            // import queues its DMUI registration, ZOOM and _MW/MAPWSPACE after
            // its loading events finish. R6's trace shows registration before ZOOM.
            if (name is "NM_AERIAL_FINISH" or "NM_AERIAL_STATE") return;
            if (name is "ZOOM" or "PAN" or "3DORBIT" or "REGEN" or "REGENALL" or "MW" or "_MW" or "MAPWSPACE" or "DMUI_MAPREGISTERCMDSCMD")
            { Log("deferred command started=" + name); Changed(); }
            else Stop("canceled by command " + name);
        }
        private void OnCommandEnd(object sender, CommandEventArgs e)
        {
            if (e.GlobalCommandName.ToUpperInvariant() is "NM_AERIAL_FINISH" or "NM_AERIAL_STATE") return;
            Changed();
        }
        private void OnDocumentClosing(object sender, DocumentCollectionEventArgs e) { if (ReferenceEquals(e.Document, _doc)) Stop("document closing"); }
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
                if (!_viewReleased && (code is 0x0100 or 0x0104 or 0x0201 or 0x0204 or 0x0207 or 0x020A or 0x020E))
                { _viewReleased = true; Log("saved-view ownership released by input=0x" + code.ToString("X4")); }
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
            if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) { Stop("document changed"); return; }
            if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(60))
            { Stop("completion deadline; command=" + _doc.CommandInProgress + "; native-finished=" + _complete); return; }
            // Do not dispose as soon as AllLayerLoadingFinished fires: the transcript
            // proves ZOOM Extents can still be queued AFTER our first finish command.
            bool verificationDue = _readyAt.HasValue && DateTime.UtcNow - _readyAt.Value >= TimeSpan.FromSeconds(5);
            if (!_finishQueued && (_pending || verificationDue) && string.IsNullOrEmpty(_doc.CommandInProgress) && DateTime.UtcNow - _changed >= TimeSpan.FromMilliseconds(750))
                QueueCompletion();
        }

        internal void MarkStage(string stage) { _stage = stage; Log("stage=" + stage); }

        internal void ApplyInitial() => Apply(false);

        internal void QueueCompletion()
        {
            if (_disposed || _finishQueued || !_returned) return;
            _finishQueued = true;
            Log("queued finish; request=" + RequestId);
            // Queue only: do not reactivate another drawing or hold a command awaiting us.
            _doc.SendStringToExecute("NM_AERIAL_FINISH " + RequestId + " ", false, false, false);
        }

        internal void FinishFromCommand()
        {
            _finishQueued = false;
            if (_disposed || !ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) return;
            bool layersReady = _complete;
            try { if (!layersReady) layersReady = _layersReady?.Invoke() == true; }
            catch (System.Exception ex) { _lastError = ex.Message; Stop("readiness failed"); return; }
            if (!_complete && (!layersReady || DateTime.UtcNow - _started < TimeSpan.FromSeconds(2)))
            {
                Log("finish waiting for native layers");
                return;
            }
            Apply(true);
        }

        private void Apply(bool final)
        {
            if (_disposed || _finish == null) return;
            _pending = false; _applying = true;
            try
            {
                using (_doc.LockDocument())
                {
                    // An ordering failure must never skip restoration of the captured view.
                    // Group/order once in the finish command. Later view checks must
                    // not refresh Map again and potentially enqueue another auto-zoom.
                    try { if (!final || !_finishApplied) _finish?.Invoke(); }
                    finally { RestoreNow(); }
                }
                if (!final) _initialApplied = true;
                _lastError = "";
                if (final)
                {
                    _finishApplied = true;
                    _readyAt ??= DateTime.UtcNow;
                    bool quiet = DateTime.UtcNow - _readyAt.Value >= TimeSpan.FromSeconds(5)
                        && DateTime.UtcNow - _changed >= TimeSpan.FromSeconds(2);
                    Log((quiet ? "settled" : "view guard active; waiting for deferred Map commands")
                        + "; native-finished=" + _complete + "; saved-view-restored=" + !_viewReleased + "; " + _savedViewText);
                    if (quiet || _viewReleased) Stop(_viewReleased ? "completed; view released to user" : "completed after deferred commands");
                    // No refresh/requeue loop: timer requests the final verification
                    // after five seconds, or sooner when a native command changes it.
                }
                else Log("initial pass; native-finished=" + _complete + "; order checked; saved-view-restored=" + !_viewReleased + "; " + _savedViewText);
                if (!final) _pending = true;
            }
            catch (System.Exception ex)
            {
                _lastError = ex.ToString();
                Log("apply failed at " + _stage + ": " + ex.Message);
                if (final) Stop("finalization failed");
                else _pending = true;
            }
            finally { _applying = false; }
        }

        internal string Status => "returned=" + _returned + "; native-finished=" + _complete + "; pending=" + _pending + "; disposed=" + _disposed + "; view-released=" + _viewReleased
            + "; initial-applied=" + _initialApplied + "; finish-applied=" + _finishApplied + "; stage=" + _stage + "; end=" + _endReason + "; saved " + _savedViewText + "; error=" + _lastError;
        private void Log(string text)
        {
            string line = "[AERIAL-R7] " + text;
            _trace.Add(line); _doc.Editor.WriteMessage("\n" + line);
        }
        internal static string ViewText(ViewTableRecord v) => string.Format(CultureInfo.InvariantCulture, "center=({0:0.###},{1:0.###}); height={2:0.######}; width={3:0.######}", v.CenterPoint.X, v.CenterPoint.Y, v.Height, v.Width);
        public void Dispose() => Stop("disposed by caller");
        private void Stop(string reason)
        {
            if (_disposed) return;
            _endReason = reason;
            Log("end=" + reason + "; stage=" + _stage);
            _disposed = true; _timer.Stop(); _timer.Dispose();
            AcadApp.PreTranslateMessage -= OnInput;
            _map.AllLayerLoadingFinished -= _onComplete; _map.LayerAdded -= _onAdded; _map.LayerInitialized -= _onInitialized;
            _doc.CommandWillStart -= OnCommandStart; _doc.CommandEnded -= OnCommandEnd;
            _doc.CommandCancelled -= OnCommandEnd; _doc.CommandFailed -= OnCommandEnd;
            AcadApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
            _view.Dispose(); _finish = null; _layersReady = null;
        }
    }
}
