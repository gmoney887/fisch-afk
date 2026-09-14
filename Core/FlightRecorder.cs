using System;
using System.IO;
using System.Text;
using OpenCvSharp;

namespace FischMacroCS.Core;

public class FlightRecorder : IDisposable
{
    private readonly string _baseDir;
    private string? _currentSessionDir;
    private VideoWriter? _videoWriter;
    private StreamWriter? _csvWriter;
    private bool _isRecording = false;
    private long _sessionStartTicks = 0;
    private int _frameCount = 0;
    private int _insideBarCount = 0;
    private int _outsideBarCount = 0;
    private int _deadCenterCount = 0;
    private int _activeReelingTicks = 0;
    private double _totalAbsError = 0;
    private int _frameWidth = 640;
    private int _frameHeight = 120;
    private int _maxRecordingsToKeep = 25;

    public bool IsRecording => _isRecording;
    public string RecordingsDirectory => _baseDir;
    public int MaxRecordingsToKeep
    {
        get => _maxRecordingsToKeep;
        set
        {
            _maxRecordingsToKeep = value;
            EnforceRetentionPolicy();
        }
    }

    public FlightRecorder(string? customDir = null, int maxRecordings = 25)
    {
        _maxRecordingsToKeep = maxRecordings;
        _baseDir = customDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordings");
        try
        {
            Directory.CreateDirectory(_baseDir);
            EnforceRetentionPolicy();
        }
        catch { }
    }

    public void EnforceRetentionPolicy()
    {
        try
        {
            if (_maxRecordingsToKeep <= 0 || !Directory.Exists(_baseDir)) return;

            var sessionDirs = Directory.GetDirectories(_baseDir, "session_*")
                .OrderBy(d => Directory.GetCreationTimeUtc(d))
                .ToList();

            int excessCount = sessionDirs.Count - _maxRecordingsToKeep;
            for (int i = 0; i < excessCount; i++)
            {
                try
                {
                    Directory.Delete(sessionDirs[i], true);
                }
                catch { }
            }
        }
        catch { }
    }

    public void StartSession(int frameWidth, int frameHeight)
    {
        StopSession();

        try
        {
            EnforceRetentionPolicy();

            string sessionName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
            _currentSessionDir = Path.Combine(_baseDir, sessionName);
            Directory.CreateDirectory(_currentSessionDir);

            // Ensure even dimensions for standard video codecs
            int w = (frameWidth % 2 != 0) ? frameWidth - 1 : frameWidth;
            int h = (frameHeight % 2 != 0) ? frameHeight - 1 : frameHeight;
            _frameWidth = Math.Max(2, w);
            _frameHeight = Math.Max(2, h);

            string videoPath = Path.Combine(_currentSessionDir, "gameplay.avi");
            _videoWriter = new VideoWriter(videoPath, FourCC.MJPG, 30, new Size(_frameWidth, _frameHeight));

            string csvPath = Path.Combine(_currentSessionDir, "telemetry.csv");
            _csvWriter = new StreamWriter(csvPath, false, Encoding.UTF8) { AutoFlush = true };
            _csvWriter.WriteLine("ElapsedMs,State,Action,MouseDown,BarFound,FishFound,BarLeft,BarRight,BarCenter,FishX,Error,BarVelocity,FishVelocity,LoopLatencyMs,VisionLatencyMs");

            _sessionStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _frameCount = 0;
            _insideBarCount = 0;
            _outsideBarCount = 0;
            _deadCenterCount = 0;
            _activeReelingTicks = 0;
            _totalAbsError = 0;
            _isRecording = true;
        }
        catch { }
    }

    public void RecordTick(TelemetryData t, bool isMouseDown, Mat? debugFrame)
    {
        if (!_isRecording || _csvWriter == null) return;

        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _sessionStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        if (t.BarWidth > 0 && t.FishX > 0)
        {
            _activeReelingTicks++;
            bool inside = (t.FishX >= t.BarLeft && t.FishX <= t.BarRight);
            if (inside) _insideBarCount++; else _outsideBarCount++;

            double absErr = Math.Abs(t.Error);
            _totalAbsError += absErr;
            if (absErr <= 20.0) _deadCenterCount++;
        }

        // Log telemetry row (safeguarded against transient file locking by OneDrive / antivirus)
        try
        {
            _csvWriter.WriteLine(
                $"{elapsedMs:F1},{t.State},{t.Action.Replace(',', ';')},{(isMouseDown ? 1 : 0)}," +
                $"{(t.BarWidth > 0 ? 1 : 0)},{(t.FishX > 0 ? 1 : 0)},{t.BarLeft},{t.BarRight},{t.BarCenter:F1}," +
                $"{t.FishX},{t.Error:F1},{t.BarVelocity:F1},{t.FishVelocity:F1},{t.LoopLatencyMs:F1},{t.VisionLatencyMs:F1}"
            );
        }
        catch { }

        // Record video frame if provided
        if (_videoWriter != null && _videoWriter.IsOpened() && debugFrame != null && !debugFrame.Empty())
        {
            try
            {
                using Mat frameToSave = new Mat();
                if (debugFrame.Width != _frameWidth || debugFrame.Height != _frameHeight)
                {
                    Cv2.Resize(debugFrame, frameToSave, new Size(_frameWidth, _frameHeight));
                }
                else
                {
                    debugFrame.CopyTo(frameToSave);
                }

                // Draw overlay flight telemetry HUD on the saved video
                string statusText = $"T={elapsedMs / 1000.0:F1}s | {(isMouseDown ? "[MOUSE DOWN]" : "[MOUSE UP]")} | Err: {t.Error:+0;-0;0}px | {t.Action}";
                Scalar hudColor = isMouseDown ? Scalar.FromRgb(255, 82, 82) : Scalar.FromRgb(0, 230, 118);
                Cv2.PutText(frameToSave, statusText, new Point(10, 20), HersheyFonts.HersheySimplex, 0.45, hudColor, 1);

                _videoWriter.Write(frameToSave);
                _frameCount++;
            }
            catch { }
        }
    }

    public void StopSession(string outcome = "Completed")
    {
        if (!_isRecording) return;
        _isRecording = false;

        try
        {
            _videoWriter?.Dispose();
            _videoWriter = null;

            if (_csvWriter != null)
            {
                _csvWriter.Flush();
                _csvWriter.Dispose();
                _csvWriter = null;
            }

            if (!string.IsNullOrEmpty(_currentSessionDir) && Directory.Exists(_currentSessionDir))
            {
                double totalSec = (System.Diagnostics.Stopwatch.GetTimestamp() - _sessionStartTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                double insidePct = _activeReelingTicks > 0 ? (_insideBarCount * 100.0 / _activeReelingTicks) : 0;
                double deadCenterPct = _activeReelingTicks > 0 ? (_deadCenterCount * 100.0 / _activeReelingTicks) : 0;
                double avgErr = _activeReelingTicks > 0 ? (_totalAbsError / _activeReelingTicks) : 0;

                string summaryPath = Path.Combine(_currentSessionDir, "summary.txt");
                File.WriteAllText(summaryPath, 
                    $"Session Outcome: {outcome}\n" +
                    $"Duration: {totalSec:F2} seconds\n" +
                    $"Total Recorded Frames: {_frameCount}\n" +
                    $"Active Reeling Ticks: {_activeReelingTicks}\n" +
                    $"Safe Zone Retention: {insidePct:F1}% ({_insideBarCount}/{_activeReelingTicks} ticks)\n" +
                    $"Dead-Center Retention (<=20px): {deadCenterPct:F1}% ({_deadCenterCount}/{_activeReelingTicks} ticks)\n" +
                    $"Average Tracking Error: {avgErr:F1} px\n"
                );
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopSession("Engine Disposed");
    }
}
