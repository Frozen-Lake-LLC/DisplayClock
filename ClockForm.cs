using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XP_DigitalClock
{
    /// <summary>
    /// Borderless, transparent, always-on-top digital clock window.
    ///
    /// Intended use:
    /// - Public-display / kiosk-style Windows systems
    /// - Windows XP Professional SP3 compatibility
    /// - .NET Framework 2.0-compatible WinForms code
    ///
    /// Main behaviors:
    /// - Displays the current time in a large digital format.
    /// - Uses a color-key transparent background so only the clock text is visible.
    /// - Stays above other applications by default.
    /// - Can be dragged around by holding the clock text.
    /// - Can be resized by dragging the lower/right edges or lower-right corner.
    /// - Can be locked to prevent accidental moving/resizing.
    /// - Double-click opens the system font picker.
    /// </summary>
    public class ClockForm : Form
    {
        /*
         * Primary UI controls.
         *
         * clockLabel:
         *   The visible digital clock text. The Form itself is transparent, so this
         *   Label is the main visible UI element.
         *
         * clockTimer:
         *   WinForms timer that updates the clock text once per second.
         *
         * menu:
         *   Right-click context menu for display options and exit.
         */
        private Label clockLabel;
        private Timer clockTimer;
        private ContextMenuStrip menu;

        /*
         * Runtime display options.
         *
         * showSeconds:
         *   false = show "8:26 PM"
         *   true  = show "8:26:04 PM"
         *
         * twentyFourHour:
         *   false = 12-hour time with AM/PM, unless AM/PM auto-hide is active
         *   true  = 24-hour time like "20:26"
         *
         * autoHideAmPmWhenLarge:
         *   true = hide AM/PM when the clock is large enough
         *
         * locked:
         *   true = prevents dragging/resizing from the overlay
         *   This helps prevent accidental movement on public-display screens.
         */
        private bool showSeconds = false;
        private bool twentyFourHour = false;
        private bool autoHideAmPmWhenLarge = true;
        private bool locked = false;

        /*
         * Resize and minimum-size settings.
         *
         * ResizeBorder:
         *   Invisible hit-test area, in pixels, used for resizing the borderless form.
         *   The user can hover near the right edge, bottom edge, or bottom-right corner.
         *
         * MinClockWidth / MinClockHeight:
         *   Prevents the window from being resized so small that it becomes unusable.
         */
        private const int ResizeBorder = 14;
        private const int MinClockWidth = 120;
        private const int MinClockHeight = 50;

        /*
         * AM/PM auto-hide thresholds.
         *
         * If enabled, AM/PM disappears when either threshold is met:
         * - The window client width is at least AmPmHideWidth.
         * - The current font size is at least AmPmHideFontSize.
         *
         * Example:
         *   Smaller clock: "8:26 PM"
         *   Larger clock:  "8:26"
         */
        private const int AmPmHideWidth = 420;
        private const float AmPmHideFontSize = 64.0f;

        /*
         * Font size limits used by the auto-fit and manual resize logic.
         *
         * MaxFontSize is intentionally large for public-display screens where the
         * clock may be stretched across a large monitor or TV.
         */
        private const float MinFontSize = 10.0f;
        private const float MaxFontSize = 260.0f;

        /*
         * Transparency color key.
         *
         * WinForms on Windows XP does not have modern per-pixel transparency in the
         * same way newer desktop UI stacks do. This uses TransparencyKey instead:
         *
         * - Every pixel that exactly matches TransparentBackColor becomes invisible.
         * - All other pixels remain visible.
         *
         * Black is used instead of magenta because anti-aliased white text can blend
         * with the background color. Magenta can cause a visible pink fringe around
         * white text. Black blending is usually less noticeable on public displays.
         */
        private static readonly Color TransparentBackColor = Color.Black;

        /*
         * Native Windows hit-test constants.
         *
         * These let a borderless Form behave like it has resize handles.
         *
         * WM_NCHITTEST:
         *   Windows message asking which part of the window the mouse is over.
         *
         * HTCLIENT:
         *   Normal client/content area.
         *
         * HTCAPTION:
         *   Title-bar area. Sending this allows dragging a borderless form.
         *
         * HTRIGHT / HTBOTTOM / HTBOTTOMRIGHT:
         *   Resize zones for the right edge, bottom edge, and lower-right corner.
         */
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTRIGHT = 11;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMRIGHT = 17;

        /// <summary>
        /// Creates and wires the clock window.
        ///
        /// Order matters:
        /// - The Label must exist before UpdateClock() tries to set its text.
        /// - The timer is started after the controls are initialized.
        /// </summary>
        public ClockForm()
        {
            InitializeWindow();
            InitializeClockLabel();
            InitializeMenu();
            InitializeTimer();

            FitFontToWindow();
            UpdateClock();
        }

        /// <summary>
        /// Configures the Form itself.
        ///
        /// The Form is borderless, transparent, always on top, and hidden from the
        /// taskbar. This makes it behave like a lightweight display overlay.
        /// </summary>
        private void InitializeWindow()
        {
            this.Text = "Digital Clock";
            this.StartPosition = FormStartPosition.CenterScreen;

            // Borderless gives the clean overlay appearance.
            // Custom hit-testing in WndProc provides resizing without a visible border.
            this.FormBorderStyle = FormBorderStyle.None;

            // Keeps the clock above PowerPoint, browser windows, dashboards, POS screens, etc.
            this.TopMost = true;

            // Public-display utility; avoid cluttering the taskbar.
            this.ShowInTaskbar = false;

            this.MinimumSize = new Size(MinClockWidth, MinClockHeight);

            // Default size chosen for a large public-display clock.
            // Users can resize after launch.
            this.Size = new Size(800, 600);

            // Color-key transparency. Any exact TransparentBackColor pixels are invisible.
            this.BackColor = TransparentBackColor;
            this.TransparencyKey = TransparentBackColor;

            // Recalculate the best font size when the window changes size.
            this.Resize += new EventHandler(ClockForm_Resize);

            // Allow mouse wheel font-size adjustment when the form itself has focus.
            this.MouseWheel += new MouseEventHandler(ClockForm_MouseWheel);

            // Double-click opens the Windows font dialog.
            this.DoubleClick += new EventHandler(ClockForm_DoubleClick);
        }

        /// <summary>
        /// Creates the Label used to draw the digital time.
        ///
        /// The Label fills the whole Form. Since the Label's background matches the
        /// transparency key, only the white text is visible.
        /// </summary>
        private void InitializeClockLabel()
        {
            clockLabel = new Label();

            clockLabel.Dock = DockStyle.Fill;
            clockLabel.TextAlign = ContentAlignment.MiddleCenter;
            clockLabel.Font = new Font("Arial", 36.0f, FontStyle.Bold);
            clockLabel.ForeColor = Color.White;
            clockLabel.BackColor = TransparentBackColor;

            /*
             * UseCompatibleTextRendering = false uses GDI text rendering.
             *
             * On XP-era systems with TransparencyKey, GDI text normally produces
             * cleaner color-key transparency than GDI+ compatible rendering.
             */
            clockLabel.UseCompatibleTextRendering = false;
            clockLabel.FlatStyle = FlatStyle.System;

            // Dragging, resizing cursor updates, mouse wheel sizing, and font dialog.
            clockLabel.MouseDown += new MouseEventHandler(ClockLabel_MouseDown);
            clockLabel.MouseMove += new MouseEventHandler(ClockLabel_MouseMove);
            clockLabel.MouseWheel += new MouseEventHandler(ClockForm_MouseWheel);
            clockLabel.DoubleClick += new EventHandler(ClockForm_DoubleClick);

            this.Controls.Add(clockLabel);
        }

        /// <summary>
        /// Builds the right-click options menu.
        ///
        /// This is intentionally simple so the app remains usable on older XP systems
        /// and easy to maintain in internal Git.
        /// </summary>
        private void InitializeMenu()
        {
            menu = new ContextMenuStrip();

            // Toggle whether the clock stays above all other applications.
            ToolStripMenuItem alwaysOnTop = new ToolStripMenuItem("Always on top");
            alwaysOnTop.Checked = this.TopMost;
            alwaysOnTop.CheckOnClick = true;
            alwaysOnTop.Click += delegate
            {
                this.TopMost = alwaysOnTop.Checked;
            };

            // Manual font-size controls.
            ToolStripMenuItem larger = new ToolStripMenuItem("Larger text");
            larger.Click += delegate
            {
                AdjustFontSize(6.0f);
            };

            ToolStripMenuItem smaller = new ToolStripMenuItem("Smaller text");
            smaller.Click += delegate
            {
                AdjustFontSize(-6.0f);
            };

            // Toggles seconds display.
            ToolStripMenuItem seconds = new ToolStripMenuItem("Show seconds");
            seconds.Checked = showSeconds;
            seconds.CheckOnClick = true;
            seconds.Click += delegate
            {
                showSeconds = seconds.Checked;
                FitFontToWindow();
                UpdateClock();
            };

            // Toggles 12-hour / 24-hour display.
            ToolStripMenuItem use24Hour = new ToolStripMenuItem("24-hour time");
            use24Hour.Checked = twentyFourHour;
            use24Hour.CheckOnClick = true;
            use24Hour.Click += delegate
            {
                twentyFourHour = use24Hour.Checked;
                FitFontToWindow();
                UpdateClock();
            };

            // Toggles large-display behavior where AM/PM is removed.
            ToolStripMenuItem autoHideAmPm = new ToolStripMenuItem("Auto-hide AM/PM when large");
            autoHideAmPm.Checked = autoHideAmPmWhenLarge;
            autoHideAmPm.CheckOnClick = true;
            autoHideAmPm.Click += delegate
            {
                autoHideAmPmWhenLarge = autoHideAmPm.Checked;
                FitFontToWindow();
                UpdateClock();
            };

            // Opens the standard Windows font/color picker.
            ToolStripMenuItem openFontSettings = new ToolStripMenuItem("Font settings");
            openFontSettings.Click += delegate
            {
                OpenFontSettings();
            };

            // Lock mode prevents accidental drag/resize on public-display systems.
            ToolStripMenuItem lockOverlay = new ToolStripMenuItem("Lock overlay");
            lockOverlay.Checked = locked;
            lockOverlay.CheckOnClick = true;
            lockOverlay.Click += delegate
            {
                locked = lockOverlay.Checked;
            };

            // Clean shutdown option.
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate
            {
                this.Close();
            };

            // Menu layout.
            menu.Items.Add(alwaysOnTop);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(larger);
            menu.Items.Add(smaller);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(seconds);
            menu.Items.Add(use24Hour);
            menu.Items.Add(autoHideAmPm);
            menu.Items.Add(openFontSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(lockOverlay);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            // Assign the menu to both the Form and the Label so right-click works
            // whether the click lands on transparent client space or clock text.
            this.ContextMenuStrip = menu;
            clockLabel.ContextMenuStrip = menu;
        }

        /// <summary>
        /// Starts the once-per-second clock refresh.
        ///
        /// WinForms Timer runs on the UI thread, so it is safe to update controls
        /// directly from the Tick event.
        /// </summary>
        private void InitializeTimer()
        {
            clockTimer = new Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate
            {
                UpdateClock();
            };
            clockTimer.Start();
        }

        /// <summary>
        /// Updates the visible time text.
        ///
        /// The exact format is based on:
        /// - showSeconds
        /// - twentyFourHour
        /// - autoHideAmPmWhenLarge
        /// </summary>
        private void UpdateClock()
        {
            if (clockLabel == null)
                return;

            string format;

            // AM/PM hides when enabled and the clock is considered large.
            bool hideAmPm = autoHideAmPmWhenLarge &&
                (this.ClientSize.Width >= AmPmHideWidth ||
                clockLabel.Font.Size >= AmPmHideFontSize);

            if (twentyFourHour)
            {
                format = showSeconds ? "HH:mm:ss" : "HH:mm";
            }
            else if (hideAmPm)
            {
                format = showSeconds ? "h:mm:ss" : "h:mm";
            }
            else
            {
                format = showSeconds ? "h:mm:ss tt" : "h:mm tt";
            }

            // InvariantCulture keeps AM/PM formatting consistent across machines.
            clockLabel.Text = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Finds the largest font size that fits inside the current window.
        ///
        /// Uses a binary-search style loop instead of incrementing one point at a
        /// time. This is faster on older hardware and avoids visible lag while
        /// resizing.
        /// </summary>
        private void FitFontToWindow()
        {
            if (clockLabel == null)
                return;

            if (clockLabel.ClientSize.Width <= 0 || clockLabel.ClientSize.Height <= 0)
                return;

            string sampleText = GetSampleText();

            float low = MinFontSize;
            float high = MaxFontSize;
            float best = MinFontSize;

            using (Graphics g = clockLabel.CreateGraphics())
            {
                while (high - low > 1.0f)
                {
                    float testSize = (low + high) / 2.0f;

                    using (Font testFont = new Font(clockLabel.Font.FontFamily, testSize, FontStyle.Bold))
                    {
                        SizeF measured = g.MeasureString(sampleText, testFont);

                        // Keep a few pixels of padding so the text does not clip
                        // against the transparent window bounds.
                        if (measured.Width <= clockLabel.ClientSize.Width - 8 &&
                            measured.Height <= clockLabel.ClientSize.Height - 4)
                        {
                            best = testSize;
                            low = testSize;
                        }
                        else
                        {
                            high = testSize;
                        }
                    }
                }
            }

            clockLabel.Font = new Font(clockLabel.Font.FontFamily, best, FontStyle.Bold);
        }

        /// <summary>
        /// Returns the widest likely clock text for the current display mode.
        ///
        /// The auto-fit routine measures this sample instead of measuring the exact
        /// current time. This prevents the clock from changing font size as time
        /// changes from narrow digits to wider text.
        /// </summary>
        private string GetSampleText()
        {
            bool hideAmPm = autoHideAmPmWhenLarge &&
                (this.ClientSize.Width >= AmPmHideWidth ||
                clockLabel.Font.Size >= AmPmHideFontSize);

            if (twentyFourHour)
            {
                return showSeconds ? "23:59:59" : "23:59";
            }

            if (hideAmPm)
            {
                return showSeconds ? "12:59:59" : "12:59";
            }

            return showSeconds ? "12:59:59 PM" : "12:59 PM";
        }

        /// <summary>
        /// Manually changes the font size.
        ///
        /// Used by:
        /// - Right-click Larger text / Smaller text
        /// - Mouse wheel up/down
        /// </summary>
        private void AdjustFontSize(float change)
        {
            if (clockLabel == null)
                return;

            float newSize = clockLabel.Font.Size + change;

            if (newSize < MinFontSize)
                newSize = MinFontSize;

            if (newSize > MaxFontSize)
                newSize = MaxFontSize;

            clockLabel.Font = new Font(clockLabel.Font.FontFamily, newSize, FontStyle.Bold);
            UpdateClock();
        }

        /// <summary>
        /// Resizes the text when the window is resized.
        /// </summary>
        private void ClockForm_Resize(object sender, EventArgs e)
        {
            FitFontToWindow();
            UpdateClock();
        }

        /// <summary>
        /// Mouse wheel shortcut for changing the text size.
        /// </summary>
        private void ClockForm_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
                AdjustFontSize(4.0f);
            else
                AdjustFontSize(-4.0f);
        }

        /// <summary>
        /// Double-click opens font settings.
        ///
        /// This is useful during setup on public-display systems where the operator
        /// may need to quickly adjust the font, style, or color.
        /// </summary>
        private void ClockForm_DoubleClick(object sender, EventArgs e)
        {
            OpenFontSettings();
        }

        /// <summary>
        /// Opens the standard Windows font dialog.
        ///
        /// Allows changing:
        /// - Font family
        /// - Font size
        /// - Font style
        /// - Text color
        /// </summary>
        private void OpenFontSettings()
        {
            if (clockLabel == null)
                return;

            using (FontDialog fontDialog = new FontDialog())
            {
                fontDialog.Font = clockLabel.Font;
                fontDialog.Color = clockLabel.ForeColor;

                fontDialog.ShowColor = true;
                fontDialog.ShowEffects = true;
                fontDialog.AllowScriptChange = false;

                DialogResult result = fontDialog.ShowDialog(this);

                if (result == DialogResult.OK)
                {
                    clockLabel.Font = fontDialog.Font;
                    clockLabel.ForeColor = fontDialog.Color;

                    UpdateClock();
                }
            }
        }

        /// <summary>
        /// Starts window dragging when the user left-clicks the clock text.
        ///
        /// A borderless window has no title bar, so this uses the native Windows
        /// HTCAPTION trick to tell Windows to move the form like a normal window.
        /// </summary>
        private void ClockLabel_MouseDown(object sender, MouseEventArgs e)
        {
            if (locked)
                return;

            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, 0xA1, HTCAPTION, 0);
            }
        }

        /// <summary>
        /// Updates the mouse cursor while hovering over the clock.
        ///
        /// - Bottom-right resize zone: diagonal resize cursor
        /// - Normal clock area: move cursor
        /// - Locked mode: default cursor
        /// </summary>
        private void ClockLabel_MouseMove(object sender, MouseEventArgs e)
        {
            if (locked)
            {
                clockLabel.Cursor = Cursors.Default;
                return;
            }

            Point clientPoint = this.PointToClient(clockLabel.PointToScreen(new Point(e.X, e.Y)));

            if (IsInBottomRightResizeZone(clientPoint))
                clockLabel.Cursor = Cursors.SizeNWSE;
            else
                clockLabel.Cursor = Cursors.SizeAll;
        }

        /// <summary>
        /// Handles native hit testing for the borderless form.
        ///
        /// This is what makes the invisible edges/corner resizable even though
        /// FormBorderStyle is None.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && !locked)
            {
                base.WndProc(ref m);

                if ((int)m.Result == HTCLIENT)
                {
                    /*
                     * m.LParam contains the mouse coordinates in screen space.
                     * The low word is X and the high word is Y.
                     *
                     * The unchecked/short casts preserve negative coordinates for
                     * multi-monitor layouts where a monitor may sit left/up of the
                     * primary display.
                     */
                    Point screenPoint = new Point(
                        unchecked((short)((int)m.LParam & 0xFFFF)),
                        unchecked((short)(((int)m.LParam >> 16) & 0xFFFF))
                    );

                    Point clientPoint = this.PointToClient(screenPoint);

                    if (IsInBottomRightResizeZone(clientPoint))
                    {
                        m.Result = new IntPtr(HTBOTTOMRIGHT);
                        return;
                    }

                    if (IsInRightResizeZone(clientPoint))
                    {
                        m.Result = new IntPtr(HTRIGHT);
                        return;
                    }

                    if (IsInBottomResizeZone(clientPoint))
                    {
                        m.Result = new IntPtr(HTBOTTOM);
                        return;
                    }
                }

                return;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// True when the mouse is inside the lower-right resize zone.
        /// </summary>
        private bool IsInBottomRightResizeZone(Point p)
        {
            return p.X >= this.ClientSize.Width - ResizeBorder &&
                   p.Y >= this.ClientSize.Height - ResizeBorder;
        }

        /// <summary>
        /// True when the mouse is inside the right-edge resize zone.
        /// </summary>
        private bool IsInRightResizeZone(Point p)
        {
            return p.X >= this.ClientSize.Width - ResizeBorder;
        }

        /// <summary>
        /// True when the mouse is inside the bottom-edge resize zone.
        /// </summary>
        private bool IsInBottomResizeZone(Point p)
        {
            return p.Y >= this.ClientSize.Height - ResizeBorder;
        }

        /*
         * Native Win32 calls used for dragging a borderless window.
         *
         * ReleaseCapture:
         *   Releases the current mouse capture so Windows can begin a normal move
         *   operation.
         *
         * SendMessage:
         *   Sends a WM_NCLBUTTONDOWN-style message with HTCAPTION so Windows treats
         *   the drag as if the user clicked the title bar.
         */
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
