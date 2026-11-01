using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace PositioningLaser {
    sealed class FakeWire : IWire {
        public readonly List<string> Writes = new List<string>();
        public string Settings = "$30=1000\n$31=0\n$32=1\n$51=DO_NOT_LOG_THIS\nok\n";
        public string State = "Idle";
        public string Override = "100";
        public string Power = "950"; // Deliberately retain an old engraving power while M5 is active.
        public string Mode = "M5", Motion = "G0", Feed = "0";
        public bool RefuseLowPower, DropAfterPulse, Closed;
        public Action Pulse;
        bool dropped;
        string replies = "";
        public void Open() { }
        public void Dispose() { Closed = true; }
        public void Write(byte[] bytes) {
            string text = bytes.Length == 1 && bytes[0] > 127 ? "RT:" + bytes[0] : Encoding.ASCII.GetString(bytes);
            Writes.Add(text);
            if (dropped) throw new InvalidOperationException("Simulated USB loss");
            if (bytes.Length == 1 && bytes[0] == 0x99) { Override = "100"; return; }
            if (text == "?") { replies += "<" + State + "|MPos:0,0,0|FS:0,0|Ov:100,100," + Override + ">\n"; return; }
            foreach (string line in text.Split('\n')) {
                if (line.Length == 0) continue;
                if (line == "$$") { replies += Settings; continue; }
                if (line == "$G") {
                    replies += "[GC:" + Motion + " G54 G17 G21 G90 G94 " + Mode + " M9 T0 F" + Feed + " S" + Power + "]\nok\n";
                    continue;
                }
                if (line.Contains("M5")) Mode = "M5";
                if (line.Contains("M3")) Mode = "M3";
                var powerWord = System.Text.RegularExpressions.Regex.Match(line, @"(?:^| )S(\d+)(?: |$)");
                if (powerWord.Success) {
                    Power = powerWord.Groups[1].Value;
                    if (RefuseLowPower && Power != "0") Power = "1000";
                }
                if (line.StartsWith("G1")) { Motion = "G1"; Feed = "3000"; }
                if (line.StartsWith("G0")) { Motion = "G0"; Feed = "0"; }
                replies += "ok\n";
            }
            if (text.Contains("M3 S10\n")) {
                if (Pulse != null) Pulse();
                if (DropAfterPulse) dropped = true;
            }
        }
        public string Read() {
            if (dropped) throw new InvalidOperationException("Simulated USB loss");
            string result = replies; replies = ""; return result;
        }
    }
    public static class Tests {
        static int count;
        static void Check(bool pass, string description) {
            if (!pass) throw new Exception("FAIL: " + description);
            ++count; Console.WriteLine("PASS: " + description);
        }
        static SwitchController Run(FakeWire wire, bool stopAfterPulse) {
            var controller = new SwitchController(() => wire);
            if (stopAfterPulse) wire.Pulse = () => controller.Stop();
            controller.Start();
            for (int i = 0; i < 1200 && controller.Busy; i++) Thread.Sleep(5);
            Check(!controller.Busy, "worker completes without hanging");
            Check(wire.Closed, "serial ownership released");
            return controller;
        }
        static bool Fired(FakeWire wire) { return wire.Writes.Any(s => s.Contains("M3")); }
        public static int Main() {
            try {
                var normal = new FakeWire(); var controller = Run(normal, true);
                Check(controller.Phase == "off", "OFF confirmed after toggle");
                Check(normal.Writes.Count(s => s.Contains("M3")) == 1, "OFF stops renewal after one pulse");
                int prime = normal.Writes.FindIndex(s => s == "G1 F3000 M5 S10\n");
                int fire = normal.Writes.FindIndex(s => s.Contains("M3"));
                Check(prime > 0 && normal.Writes[prime + 1] == "$G\n" && fire > prime + 1,
                    "old 95% power overwritten and S10 verified under M5 before enabling");
                Check(normal.Writes[fire] == "M3 S10\nG4 P0.25\nM5 S0\n", "1% pulse and bounded dwell/OFF queued in one write");
                Check(!normal.Writes.Any(s => System.Text.RegularExpressions.Regex.Matches(s, @"(?:^|[ \n])S(\d+)")
                    .Cast<System.Text.RegularExpressions.Match>().Any(m => int.Parse(m.Groups[1].Value) > 10)),
                    "no programmed power exceeds 1% (S10)");
                foreach (string wrong in new[] { "1", "100", "1000" }) {
                    bool refused = false;
                    try { Protocol.RequireMode("[GC:G1 M5 S" + wrong + "]", "10"); } catch (InvalidOperationException) { refused = true; }
                    Check(refused, "S10 check rejects different power S" + wrong);
                }
                Check(normal.Writes.Contains("RT:153"), "spindle override reset to 100%");
                Check(normal.Power == "0" && normal.Mode == "M5" && normal.Motion == "G0", "zero power and original motion mode restored");
                Check(!normal.Writes.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"(?:^|[ \n])(?:[XYZ]|G10|G28|G92|\$H|\$X|\$\d+=)")),
                    "no coordinate, movement, unlock or EEPROM-write commands");
                foreach (string settings in new[] {
                    "$30=255\n$31=0\n$32=1\nok\n", "$30=1000\n$31=5\n$32=1\nok\n",
                    "$30=1000\n$31=0\n$32=0\nok\n", "$31=0\n$32=1\nok\n" }) {
                    var bad = new FakeWire { Settings = settings }; Run(bad, true);
                    Check(!Fired(bad), "unexpected or missing scale/minimum/laser-mode setting blocks ON");
                }
                var corrupt = new FakeWire { RefuseLowPower = true }; Run(corrupt, true);
                Check(!Fired(corrupt), "mismatched S readback blocks ON");
                foreach (string state in new[] { "Run", "Hold:0", "Alarm", "Door:0" }) {
                    var occupied = new FakeWire { State = state }; Run(occupied, true);
                    Check(!Fired(occupied) && occupied.Writes.SequenceEqual(new[] { "?" }), "non-idle machine untouched: " + state);
                }
                var dropout = new FakeWire { DropAfterPulse = true }; var lost = Run(dropout, false);
                Check(lost.Phase == "error" && lost.Message.Contains("OFF NOT CONFIRMED"), "USB failure never falsely reports OFF");
                Check(dropout.Writes.Count(s => s.Contains("M3")) == 1, "USB failure cannot restart beam");
                Check(dropout.Writes.Any(s => s.Length == 1 && s[0] == 0x18), "last-resort reset attempted when OFF cannot be confirmed");
                Console.WriteLine("All " + count + " checks passed. Simulated serial only; no hardware opened.");
                return 0;
            } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
