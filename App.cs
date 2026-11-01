using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PositioningLaser {
    public sealed class SwitchWindow : Form {
        readonly SwitchController controller = new SwitchController(() => new SerialWire());
        readonly Button toggle = new Button();
        readonly Label status = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool closing;
        public SwitchWindow() {
            Text = "Positioning laser"; ClientSize = new Size(440, 290);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(24, 29, 37);
            ForeColor = Color.White; FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            var title = new Label { Text = "Positioning laser", Font = new Font("Segoe UI", 18, FontStyle.Bold),
                Location = new Point(24, 20), AutoSize = true };
            var details = new Label { Text = "COM4  /  fixed 1%  /  no movement", AutoSize = true,
                ForeColor = Color.Silver, Location = new Point(26, 61) };
            toggle.Location = new Point(24, 94); toggle.Size = new Size(392, 67);
            toggle.Font = new Font("Segoe UI", 16, FontStyle.Bold); toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 0; toggle.Text = "TURN ON";
            toggle.BackColor = Color.FromArgb(46, 100, 124); toggle.ForeColor = Color.White;
            toggle.AccessibleName = "Toggle positioning laser";
            toggle.Click += delegate { if (controller.Busy) controller.Stop(); else controller.Start(); RefreshState(); };
            status.Location = new Point(26, 175); status.Size = new Size(390, 64);
            status.Text = controller.Message;
            var note = new Label { Text = "Auto-off after 60 seconds. Low power can still mark material.",
                Location = new Point(26, 251), AutoSize = true, Font = new Font("Segoe UI", 8), ForeColor = Color.Silver };
            Controls.AddRange(new Control[] { title, details, toggle, status, note });
            timer.Interval = 80; timer.Tick += delegate { RefreshState(); if (closing && !controller.Busy) Close(); };
            timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (controller.Busy) { e.Cancel = true; closing = true; controller.Stop(); toggle.Enabled = false; }
            };
            FormClosed += delegate { timer.Dispose(); };
        }
        void RefreshState() {
            status.Text = controller.Message;
            toggle.Text = controller.Busy ? "TURN OFF" : "TURN ON";
            toggle.BackColor = controller.Busy ? Color.FromArgb(158, 63, 53) : Color.FromArgb(46, 100, 124);
            status.ForeColor = controller.Phase == "error" ? Color.FromArgb(255, 174, 145) : Color.White;
        }
        [STAThread]
        public static int Main(string[] args) {
            bool first;
            using (var mutex = new Mutex(true, "Local\\HeemiPositioningLaserCOM4", out first)) {
                if (!first) { MessageBox.Show("Positioning laser is already open."); return 1; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new SwitchWindow()) {
                    if (args.Length == 2 && args[0] == "--preview") {
                        // Render UI only: never opens a serial port or activates the button.
                        form.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]);
                        }
                        form.Close();
                    } else Application.Run(form);
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }
    }
}
