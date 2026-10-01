using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SecondCursor.Core.Util
{
    /// <summary>
    /// Phase Q4 (CH8): writes small files from one background thread, so the game's main thread never waits on the disk (or on an antivirus
    /// scan that holds a file for a moment). Each write is atomic as before (a temp file, then a replace that keeps the previous file as
    /// <c>.bak</c>). Writes to the same path made before the thread gets to them are coalesced into the latest one, so a tug win that saves
    /// three times writes the file once, and writes to one path always land in the order they were asked for. <see cref="Flush"/> waits for
    /// everything asked so far (quit, pause, a test reading the file). Engine-free: problems are returned through <see cref="TakeMessages"/>,
    /// to be logged on the main thread; this class never logs.
    /// </summary>
    public sealed class BackgroundFileWriter
    {
        readonly object _lock = new object();
        readonly Dictionary<string, string> _pending = new Dictionary<string, string>();
        readonly List<string> _order = new List<string>();
        readonly List<string> _messages = new List<string>();
        Thread _thread;
        bool _writing;
        /// <summary>Test hook: how many files were really written (coalesced writes count once).</summary>
        public int WritesDone { get; private set; }

        /// <summary>Asks for <paramref name="text"/> to be the content of <paramref name="path"/>. Returns at once.</summary>
        public void Enqueue(string path, string text)
        {
            lock (_lock)
            {
                if (!_pending.ContainsKey(path)) _order.Add(path);
                _pending[path] = text;
                if (_thread == null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "SECOND CURSOR file writer" };
                    _thread.Start();
                }
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>True when nothing is waiting or being written.</summary>
        public bool IsIdle
        {
            get
            {
                lock (_lock) return _pending.Count == 0 && !_writing;
            }
        }

        /// <summary>Waits until everything asked so far is on disk (or <paramref name="timeoutMilliseconds"/> passes). True if it is.</summary>
        public bool Flush(int timeoutMilliseconds = 3000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            lock (_lock)
            {
                while (_pending.Count > 0 || _writing)
                {
                    var left = end - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return false;
                    Monitor.Wait(_lock, left);
                }
                return true;
            }
        }

        /// <summary>The problems met since the last call (a write that failed), for the main thread to log.</summary>
        public List<string> TakeMessages()
        {
            lock (_lock)
            {
                if (_messages.Count == 0) return null;
                var copy = new List<string>(_messages);
                _messages.Clear();
                return copy;
            }
        }

        void Run()
        {
            while (true)
            {
                string path, text;
                lock (_lock)
                {
                    while (_order.Count == 0) Monitor.Wait(_lock);
                    path = _order[0];
                    _order.RemoveAt(0);
                    text = _pending[path];
                    _pending.Remove(path);
                    _writing = true;
                }
                string problem = WriteAtomic(path, text);
                lock (_lock)
                {
                    _writing = false;
                    WritesDone++;
                    if (problem != null) _messages.Add(problem);
                    Monitor.PulseAll(_lock);
                }
            }
        }

        /// <summary>The temp-file-then-replace write the save system always used; returns a message on failure, else null.</summary>
        public static string WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, text);
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                return null;
            }
            catch (Exception e)
            {
                return "Could not write " + Path.GetFileName(path) + ": " + e.Message;
            }
        }
    }
}
