using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PositioningLaser {
    public interface IWire : IDisposable {
        void Open();
        void Write(byte[] bytes);
        string Read();
    }

    public sealed class SerialWire : IWire {
        readonly SerialPort port = new SerialPort("COM4", 115200, Parity.None, 8, StopBits.One);
        public SerialWire() {
            port.DtrEnable = false; port.RtsEnable = false;
            port.Handshake = Handshake.None; port.WriteTimeout = 750;
        }
        public void Open() { port.Open(); Thread.Sleep(1000); port.DiscardInBuffer(); }
        public void Write(byte[] bytes) { port.Write(bytes, 0, bytes.Length); }
        public string Read() { return port.ReadExisting(); }
        public void Dispose() { port.Dispose(); }
    }

    // No axis words, offsets, homing, unlock, configuration writes or resume commands.
    public sealed class Protocol {
        readonly IWire wire;
        string pending = "";
        public Protocol(IWire wire) { this.wire = wire; }
        public void Realtime(byte value) { wire.Write(new byte[] { value }); }
        public List<string> Exchange(string text, int acknowledgements) {
            wire.Write(Encoding.ASCII.GetBytes(text));
            return Receive(acknowledgements, false);
        }
        public string Status() {
            Realtime((byte)'?');
            List<string> lines = Receive(0, true);
            return lines[lines.Count - 1];
        }
        List<string> Receive(int acknowledgements, bool status) {
            var result = new List<string>(); int count = 0;
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 2000) {
                pending += wire.Read();
                if (pending.Length > 65536) throw new InvalidOperationException("Unexpected controller response.");
                int end;
                while ((end = pending.IndexOf('\n')) >= 0) {
                    string line = pending.Substring(0, end).Trim(); pending = pending.Substring(end + 1);
                    if (line.Length == 0) continue;
                    if (line.StartsWith("error:") || line.StartsWith("ALARM:"))
                        throw new InvalidOperationException("Controller rejected the command: " + line);
                    if (line.StartsWith("Grbl ") || line.StartsWith("ESP-ROM:") || line.StartsWith("rst:"))
                        throw new InvalidOperationException("Controller reset. Toggle again after checking the machine.");
                    result.Add(line);
                    if (status && line.StartsWith("<") && line.EndsWith(">")) return result;
                    if (line == "ok" && ++count == acknowledgements && !status) return result;
                }
                Thread.Sleep(5);
            }
            throw new TimeoutException("Controller did not answer in time.");
        }
        static double Number(string text) { return double.Parse(text, CultureInfo.InvariantCulture); }
        public static void RequireIdleOff(string status) {
            if (!status.StartsWith("<Idle|")) throw new InvalidOperationException("Machine is not idle. No positioning beam started.");
            Match fs = Regex.Match(status, @"\|FS:([-\d.]+),([-\d.]+)(?:\||>)");
            if (!fs.Success || Number(fs.Groups[1].Value) != 0 || Number(fs.Groups[2].Value) != 0)
                throw new InvalidOperationException("Machine must report zero feed and zero laser output.");
            Match ov = Regex.Match(status, @"\|Ov:\d+,\d+,(\d+)(?:\||>)");
            if (ov.Success && ov.Groups[1].Value != "100")
                throw new InvalidOperationException("Laser override is not 100%.");
        }
        public static void RequireSettings(List<string> lines) {
            foreach (var expected in new string[] { "30=1000", "31=0", "32=1" }) {
                string[] pair = expected.Split('=');
                string pattern = @"^\$" + pair[0] + @"=([-\d.]+)$";
                bool found = false;
                foreach (string line in lines) {
                    Match match = Regex.Match(line, pattern);
                    if (match.Success) {
                        found = true;
                        if (Number(match.Groups[1].Value) != Number(pair[1]))
                            throw new InvalidOperationException("Unexpected $" + pair[0] + ". Fixed 1% output cannot be verified.");
                    }
                }
                if (!found) throw new InvalidOperationException("Missing $" + pair[0] + ". Fixed 1% output cannot be verified.");
            }
        }
        public static string Modal(List<string> lines) {
            foreach (string line in lines) if (line.StartsWith("[GC:") && line.EndsWith("]")) return line;
            throw new InvalidOperationException("Controller did not report its programmed power.");
        }
        public static void RequireMode(string modal, string power) {
            if (!Regex.IsMatch(modal, @"(?:[: ])M5(?: |\])") ||
                !Regex.IsMatch(modal, @"(?:[: ])S" + power + @"(?:\.0+)?(?: |\])"))
                throw new InvalidOperationException("Laser-off / programmed-power verification failed.");
        }
    }

    public sealed class SwitchController {
        readonly object gate = new object();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly Func<IWire> factory;
        volatile bool busy;
        string message = "OFF - not connected";
        string phase = "off";
        public bool Busy { get { return busy; } }
        public string Message { get { lock (gate) return message; } }
        public string Phase { get { lock (gate) return phase; } }
        public SwitchController(Func<IWire> factory) { this.factory = factory; }
        void Report(string state, string text) { lock (gate) { phase = state; message = text; } }
        public void Start() {
            if (busy) return;
            stop.Reset(); busy = true; Report("checking", "Checking COM4 and fixed low power...");
            var thread = new Thread(Run); thread.IsBackground = true; thread.Start();
        }
        public void Stop() { stop.Set(); Report("stopping", "Turning off and checking..."); }
        void Cancelled() { if (stop.WaitOne(0)) throw new OperationCanceledException(); }
        void Run() {
            IWire wire = null; Protocol protocol = null;
            bool owned = false, offConfirmed = false; string failure = null;
            string originalMotion = "G0", originalFeed = "0";
            try {
                wire = factory(); wire.Open(); protocol = new Protocol(wire);
                Cancelled();
                // Refuse a running/held/energized machine before taking ownership.
                Protocol.RequireIdleOff(protocol.Status());
                owned = true;
                protocol.Exchange("M5 S0\n", 1);
                string modal = Protocol.Modal(protocol.Exchange("$G\n", 1));
                Protocol.RequireMode(modal, "0");
                Match motion = Regex.Match(modal, @"(?:[: ])(G0|G1)(?: |\])");
                Match feed = Regex.Match(modal, @"(?:[: ])F(\d+(?:\.\d+)?)(?: |\])");
                if (!motion.Success || !feed.Success) throw new InvalidOperationException("Unsupported saved motion mode.");
                originalMotion = motion.Groups[1].Value; originalFeed = feed.Groups[1].Value;
                Protocol.RequireSettings(protocol.Exchange("$$\n", 1));
                protocol.Realtime(0x99); // Grbl 1.1: reset spindle override to 100%.
                Protocol.RequireIdleOff(protocol.Status());
                Cancelled();
                var duration = Stopwatch.StartNew();
                while (!stop.WaitOne(0) && duration.Elapsed.TotalSeconds < 60) {
                    protocol.Realtime(0x99);
                    Protocol.RequireIdleOff(protocol.Status());
                    // Verify S10 (1% of $30=1000) WHILE M5 is active before every burst.
                    protocol.Exchange("G1 F3000 M5 S10\n", 1);
                    Protocol.RequireMode(Protocol.Modal(protocol.Exchange("$G\n", 1)), "10");
                    Cancelled();
                    Report("on", "ON - 1% positioning pulses");
                    // One short write, well below Grbl's receive-buffer capacity.
                    // The controller gets its own OFF command with each 250 ms pulse.
                    protocol.Exchange("M3 S10\nG4 P0.25\nM5 S0\n", 3);
                    Protocol.RequireMode(Protocol.Modal(protocol.Exchange("$G\n", 1)), "0");
                    Protocol.RequireIdleOff(protocol.Status());
                }
            } catch (OperationCanceledException) {
                // The normal finally path verifies OFF before releasing the port.
            } catch (Exception error) { failure = error.Message; }
            finally {
                if (owned && protocol != null) {
                    try {
                        protocol.Exchange("M5 S0\n", 1);
                        Protocol.RequireMode(Protocol.Modal(protocol.Exchange("$G\n", 1)), "0");
                        Protocol.RequireIdleOff(protocol.Status());
                        offConfirmed = true;
                        // Restore feed/motion mode only. Never restore an old S value.
                        protocol.Exchange(originalMotion + " F" + originalFeed + " M5 S0\n", 1);
                    } catch (Exception error) {
                        offConfirmed = false;
                        failure = "OFF NOT CONFIRMED. Switch machine off. " + error.Message;
                        // Last resort: reset, never unlock or resume. Coordinates may be lost.
                        try { protocol.Realtime(0x18); } catch { }
                    }
                }
                if (wire != null) try { wire.Dispose(); } catch { }
                if (failure != null) Report("error", failure + (offConfirmed ? " Beam off confirmed." : ""));
                else Report("off", offConfirmed ? "OFF - controller confirmed" : "OFF - no beam enabled");
                busy = false;
            }
        }
    }
}
