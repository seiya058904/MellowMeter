using System.Runtime.InteropServices;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // A purpose-built scrolling viewport. One content window is moved, so a frame costs one
    // SetWindowPos however many rows the page has, and the system blits the content and asks only
    // for the exposed strip. There is no native scrollbar to suppress: an overlay indicator lives
    // in the page gutter, and overscroll simply shows the viewport's own page colour.
    public sealed class ScrollSurface : Panel
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const string Channel = "scroll";
        internal readonly StackPanel Content;
        private readonly ScrollIndicator _indicator;
        private readonly Action<float> _present;
        private float _offset;
        private float _destination;
        private int _riseOffset;
        private long _lastWheel;
        private float _wheelBoost = 1;

        // Raised after every applied offset, including overscroll.
        internal event Action<float>? Scrolled;

        internal int MaxScroll => Math.Max(0, Content.Height - ClientSize.Height);
        internal float Offset => _offset;
        internal bool IndicatorVisible => _indicator.Visible;
        internal bool IndicatorRevealed => _indicator.Revealed;

        public ScrollSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            TabStop = false;
            BackColor = UIColors.MainBg;
            _present = Present;
            Content = new StackPanel { BackColor = UIColors.MainBg, Location = Point.Empty };
            Content.ContentHeightChanged += SyncRange;
            _indicator = new ScrollIndicator(this);
            Controls.Add(_indicator);
            Controls.Add(Content);
            _indicator.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e) => e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);

        // Children are placed explicitly; moving the content must not run a layout pass.
        protected override void OnLayout(LayoutEventArgs e) => ArrangeChildren();

        private void ArrangeChildren()
        {
            if (!Visible) return;
            if (Content.Width != ClientSize.Width) Content.Width = ClientSize.Width;
            _indicator.Place();
            SyncRange();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) ArrangeChildren();
        }

        private void SyncRange()
        {
            int max = MaxScroll;
            _indicator.Sync();
            if (!InterfaceMotion.IsAnimating(this, Channel))
            {
                if (_offset > max) { _destination = max; Present(max); }
                else _destination = _offset;
            }
            else if (_destination > max)
            {
                // Content shrank during a glide: keep moving, but toward the new end.
                _destination = max;
                InterfaceMotion.Spring(this, Channel, SpringSpec.Scroll, _offset, max, _present);
            }
        }

        private void Present(float offset)
        {
            _offset = offset;
            int y = _riseOffset - (int)MathF.Round(offset);
            if (Content.Top != y) Content.Top = y;
            _indicator.Sync();
            Scrolled?.Invoke(offset);
        }

        // Direct positioning for drag, restore and tests. No motion, no overscroll.
        internal void ScrollTo(float offset)
        {
            InterfaceMotion.Cancel(this, Channel);
            _destination = Math.Clamp(offset, 0, MaxScroll);
            Present(_destination);
        }

        internal void GlideTo(float offset)
        {
            _destination = Math.Clamp(offset, 0, MaxScroll);
            InterfaceMotion.Spring(this, Channel, SpringSpec.Scroll, _offset, _destination, _present);
        }

        // Continues a release with its momentum, decelerating as if under friction. Reaching an
        // edge turns the remaining speed into a small bounce.
        internal void Fling(float velocity)
        {
            const float glideSeconds = 0.22f;
            float raw = _offset + velocity * glideSeconds;
            MoveToward(raw, velocity);
        }

        private void MoveToward(float raw, float velocity)
        {
            int max = MaxScroll;
            float clamped = Math.Clamp(raw, 0, max);
            _destination = clamped;
            bool past = raw < 0 || raw > max;
            float limit = UIUtils.S(900f);
            InterfaceMotion.Spring(this, Channel, past ? SpringSpec.Edge : SpringSpec.Scroll, _offset, clamped, _present, null,
                Math.Clamp(past ? velocity + (raw - clamped) * 6f : velocity, -limit, limit));
        }

        // Each notch moves the destination; the spring carries the current offset (and speed) there.
        // Quick successive notches accelerate; the user's lines-per-notch and page mode are respected.
        private bool HandleWheel(int delta, int keys)
        {
            if (MaxScroll <= 0 || (keys & 0x4) != 0 /* MK_CONTROL */) return false;
            if (delta == 0) return true;
            int lines = SystemInformation.MouseWheelScrollLines;
            float step = lines < 0 ? ClientSize.Height * 0.875f : lines * UIUtils.S(19f);
            if (lines == 0) return true;
            float notches = delta / 120f;
            bool precise = Math.Abs(delta) < 120 || delta % 120 != 0; // touchpads and hi-res wheels
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double since = System.Diagnostics.Stopwatch.GetElapsedTime(_lastWheel, now).TotalMilliseconds;
            _lastWheel = now;
            _wheelBoost = precise || since > 140 ? 1 : Math.Min(3f, _wheelBoost + (float)(140 - since) / 180f);
            float amount = notches * step * _wheelBoost;
            if (!InterfaceMotion.Enabled) { ScrollTo(_destination - amount); return true; }
            bool gliding = InterfaceMotion.IsAnimating(this, Channel);
            float origin = gliding ? _destination : _offset;
            float raw = origin - amount;
            float velocity = gliding ? InterfaceMotion.VelocityOf(this, Channel) : 0;
            int max = MaxScroll;
            if (raw < 0 || raw > max) { MoveToward(raw, velocity); return true; }
            _destination = raw;
            if (precise) InterfaceMotion.Spring(this, Channel, SpringSpec.Scroll with { Response = 0.12 }, _offset, _destination, _present);
            else InterfaceMotion.Spring(this, Channel, SpringSpec.Scroll, _offset, _destination, _present);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL && HandleWheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF), (int)(m.WParam.ToInt64() & 0xFFFF)))
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // Keyboard paging; returns true when the key was a scrolling key for this surface.
        internal bool HandleKey(Keys key)
        {
            int max = MaxScroll;
            if (max <= 0) return false;
            float page = ClientSize.Height * 0.875f;
            float? target = key switch
            {
                Keys.PageDown => _destination + page,
                Keys.PageUp => _destination - page,
                Keys.Home => 0,
                Keys.End => max,
                _ => null
            };
            if (target == null) return false;
            GlideTo(target.Value);
            return true;
        }

        // Tab order and programmatic focus keep the focused control on screen.
        internal void EnsureVisible(Control control)
        {
            if (!Content.Contains(control) || !control.IsHandleCreated || !IsHandleCreated) return;
            Rectangle area = RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            int margin = UIUtils.S(12);
            float target = _destination;
            float top = area.Top + _offset - _riseOffset, bottom = area.Bottom + _offset - _riseOffset;
            if (top - margin < target) target = top - margin;
            else if (bottom + margin > target + ClientSize.Height) target = bottom + margin - ClientSize.Height;
            if (target != _destination) GlideTo(target);
        }

        // Page transition: the page starts slightly low and settles, moving only the one content
        // window; the page itself is already fully laid out and interactive.
        internal void PlayRise()
        {
            int from = UIUtils.S(10);
            if (!InterfaceMotion.Enabled || !IsHandleCreated || !Visible) return;
            _riseOffset = from;
            Present(_offset);
            InterfaceMotion.Spring(this, "rise", SpringSpec.PageRise, from, 0, v => { _riseOffset = (int)MathF.Round(v); Present(_offset); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) InterfaceMotion.Cancel(this);
            base.Dispose(disposing);
        }

        // Slim rounded indicator in the page gutter: appears while scrolling, fades at rest, thickens
        // under the pointer, drags with momentum on release and has a generous hit area.
        internal sealed class ScrollIndicator : Control
        {
            private readonly ScrollSurface _surface;
            private readonly MotionValue _alpha, _grow;
            private readonly System.Windows.Forms.Timer _hide = new() { Interval = 900 };
            private bool _dragging;
            private int _dragOffset;
            private float _lastOffset = float.NaN;
            private int _lastMax = -1;
            private long _lastMoveStamp;
            private float _dragVelocity;
            private float _lastDragOffset;

            internal bool Revealed => _alpha.Value > 0.01f;

            internal ScrollIndicator(ScrollSurface surface)
            {
                _surface = surface;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Visible = false;
                AccessibleRole = AccessibleRole.ScrollBar;
                AccessibleName = "Scroll";
                _alpha = new MotionValue(this, "fade", SpringSpec.Fade);
                _grow = new MotionValue(this, "grow", SpringSpec.Hover);
                _hide.Tick += (_, _) => { _hide.Stop(); if (!_dragging && !PointerInside) _alpha.To(0, SpringSpec.PressOut); };
            }

            private bool PointerInside => IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));

            internal void Place()
            {
                int width = UIUtils.S(16); // hit slop: the visible thumb is far thinner
                var target = new Rectangle(_surface.ClientSize.Width - width, 0, width, _surface.ClientSize.Height);
                if (Bounds != target) Bounds = target;
            }

            internal void Sync()
            {
                if (IsDisposed || !_surface.Visible) return;
                int max = _surface.MaxScroll;
                bool needed = max > 0;
                if (Visible != needed) { Visible = needed; if (needed) BringToFront(); }
                if (!needed) { _lastMax = max; return; }
                float offset = _surface.Offset;
                if (max == _lastMax && Math.Abs(offset - _lastOffset) < 0.5f) return;
                bool moved = !float.IsNaN(_lastOffset) && Math.Abs(offset - _lastOffset) >= 0.5f;
                _lastOffset = offset; _lastMax = max;
                Invalidate();
                if (moved) Reveal();
            }

            private void Reveal()
            {
                _alpha.To(1, SpringSpec.Fade);
                _hide.Stop();
                if (!_dragging && !PointerInside) _hide.Start();
            }

            private (int Track, int Height, int Top) Geometry()
            {
                int margin = UIUtils.S(4);
                int track = Math.Max(1, Height - margin * 2);
                int contentHeight = Math.Max(1, _surface.Content.Height);
                int thumb = Math.Clamp((int)(track * (float)_surface.ClientSize.Height / contentHeight), Math.Min(UIUtils.S(36), track), track);
                int max = _surface.MaxScroll;
                float offset = _surface.Offset;
                // Overscroll compresses the thumb against the end of the track, like iOS.
                float over = offset < 0 ? -offset : offset > max ? offset - max : 0;
                int height = Math.Max(UIUtils.S(12), thumb - (int)over);
                float ratio = max <= 0 ? 0 : Math.Clamp(offset / max, 0, 1);
                return (track, height, margin + (int)Math.Round((track - height) * ratio));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color page = UIColors.MainBg;
                e.Graphics.Clear(page);
                float visibility = Math.Clamp(_alpha.Value, 0f, 1f);
                if (visibility < 0.01f) return;
                var (_, height, top) = Geometry();
                float grow = Math.Clamp(_grow.Value, 0f, 1f);
                float width = UIUtils.S(3f) + UIUtils.S(3f) * grow;
                var thumb = new RectangleF(Width - UIUtils.S(4f) - width, top, width, height);
                Ink.Fill(e.Graphics, thumb, width / 2f, UIColors.Blend(page, UIColors.TextMain, (0.30f + 0.22f * grow) * visibility));
            }

            private void DragTo(int pointerY)
            {
                var (track, height, _) = Geometry();
                float ratio = Math.Clamp((pointerY - _dragOffset - UIUtils.S(4f)) / Math.Max(1f, track - height), 0f, 1f);
                float next = ratio * _surface.MaxScroll;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double dt = System.Diagnostics.Stopwatch.GetElapsedTime(_lastMoveStamp, now).TotalSeconds;
                if (dt > 0.001 && dt < 0.2) _dragVelocity = _dragVelocity * 0.5f + (float)((next - _lastDragOffset) / dt) * 0.5f;
                else if (dt >= 0.2) _dragVelocity = 0;
                _lastMoveStamp = now; _lastDragOffset = next;
                _surface.ScrollTo(next);
                Update();
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hide.Stop(); _alpha.To(1, SpringSpec.Fade); _grow.To(1); }
            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_dragging) return;
                _grow.To(0); _hide.Stop(); _hide.Start();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                var (_, height, top) = Geometry();
                _dragOffset = e.Y >= top && e.Y <= top + height ? e.Y - top : height / 2; // track press recentres the thumb
                _dragging = true; Capture = true;
                _dragVelocity = 0; _lastMoveStamp = System.Diagnostics.Stopwatch.GetTimestamp(); _lastDragOffset = _surface.Offset;
                DragTo(e.Y);
            }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) DragTo(e.Y); }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!_dragging) return;
                _dragging = false; Capture = false;
                // A fast release keeps gliding; a pause before release means the user chose this spot.
                double idle = System.Diagnostics.Stopwatch.GetElapsedTime(_lastMoveStamp).TotalSeconds;
                if (idle < 0.06 && Math.Abs(_dragVelocity) > UIUtils.S(120f) && InterfaceMotion.Enabled)
                    _surface.Fling(_dragVelocity);
                if (!PointerInside) { _grow.To(0); _hide.Stop(); _hide.Start(); }
            }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _dragging = false; }
            protected override void Dispose(bool disposing)
            {
                if (disposing) _hide.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    // Host of the retained settings pages. Painting is skipped entirely: every pixel belongs to a
    // child page, so WM_ERASEBKGND must not clear the window under them.
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) { m.Result = (IntPtr)1; return; } // WM_ERASEBKGND
            base.WndProc(ref m);
        }
    }

    // Base of the top-level per-pixel-alpha windows (menu, drag ghost, value bubble, reveal veil).
    // They never activate, so the dialog keeps focus and its title bar stays active.
    internal class LayeredSprite : Form
    {
        [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; public Pt(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct Sz { public int W, H; public Sz(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Pt pos, ref Sz size, IntPtr src, ref Pt srcPos, int key, ref Blend blend, int flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Pt pos, IntPtr size, IntPtr src, IntPtr srcPos, int key, ref Blend blend, int flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

        private readonly bool _clickThrough;
        protected Size SurfaceSize { get; private set; }

        protected LayeredSprite(bool clickThrough)
        {
            _clickThrough = clickThrough;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Size.Empty;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000); // WS_POPUP
                cp.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080; // WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                if (_clickThrough) cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        // Shows a premultiplied 32-bit bitmap at a screen position with a uniform opacity.
        protected void Present(Bitmap premultiplied, Point screen, float opacity)
        {
            if (!IsHandleCreated) { var _ = Handle; }
            IntPtr memory = CreateCompatibleDC(IntPtr.Zero);
            IntPtr hBitmap = premultiplied.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memory, hBitmap);
            try
            {
                var pos = new Pt(screen.X, screen.Y);
                var size = new Sz(premultiplied.Width, premultiplied.Height);
                var src = new Pt(0, 0);
                var blend = new Blend { Op = 0, Flags = 0, Alpha = (byte)Math.Clamp((int)(opacity * 255f + 0.5f), 0, 255), Format = 1 };
                UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, memory, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                SurfaceSize = premultiplied.Size;
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(hBitmap);
                DeleteDC(memory);
            }
            if (!Visible) ShowNoActivate();
        }

        // Moves and fades without touching the pixels: no re-render, the cheapest possible frame.
        protected void Reposition(Point screen, float opacity)
        {
            if (!IsHandleCreated || SurfaceSize.IsEmpty) return;
            var pos = new Pt(screen.X, screen.Y);
            var blend = new Blend { Op = 0, Flags = 0, Alpha = (byte)Math.Clamp((int)(opacity * 255f + 0.5f), 0, 255), Format = 1 };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
        }

        private void ShowNoActivate()
        {
            SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW
            Visible = true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }
    }

    // An opaque drawing surface backed by a real GDI DIB section. GDI only uses ClearType on such a
    // surface (a GDI+ bitmap's HDC falls back to greyscale), so floating sprites match the window's
    // text. Bitmap wraps the same pixels for composition, without copying them.
    internal sealed class GdiCanvas : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

        private readonly IntPtr _dc, _dib, _previous;
        internal Graphics Graphics { get; }
        internal Bitmap Pixels { get; }
        internal IntPtr Hdc => _dc;

        internal GdiCanvas(int width, int height)
        {
            var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            _dc = CreateCompatibleDC(IntPtr.Zero);
            _dib = CreateDIBSection(_dc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            _previous = SelectObject(_dc, _dib);
            Graphics = Graphics.FromHdc(_dc);
            Pixels = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppRgb, bits);
        }

        public void Dispose()
        {
            Pixels.Dispose();
            Graphics.Dispose();
            SelectObject(_dc, _previous);
            DeleteObject(_dib);
            DeleteDC(_dc);
        }
    }

    // Soft shadow painted directly into a premultiplied bitmap: alpha is an analytic falloff from a
    // rounded rectangle, so a menu or ghost pays well under a millisecond instead of stacked blurs.
    internal static class SoftShadow
    {
        internal static void Paint(Bitmap target, Rectangle body, int radius, int blur, int offsetY, float strength)
        {
            int width = target.Width, height = target.Height;
            int[] pixels = System.Buffers.ArrayPool<int>.Shared.Rent(width * height);
            try
            {
                float cx = body.Left + body.Width / 2f, cy = body.Top + offsetY + body.Height / 2f;
                float hx = body.Width / 2f - radius, hy = body.Height / 2f - radius;
                int maxAlpha = (int)(255 * Math.Clamp(strength, 0f, 1f));
                for (int y = 0, i = 0; y < height; y++)
                {
                    float dy = MathF.Abs(y + 0.5f - cy) - hy;
                    bool crossesBody = y >= body.Top + radius && y < body.Bottom - radius;
                    for (int x = 0; x < width; x++, i++)
                    {
                        if (crossesBody && x == body.Left + radius)
                        {
                            int covered = Math.Max(0, body.Width - radius * 2);
                            Array.Clear(pixels, i, covered);
                            x += covered - 1; i += covered - 1;
                            continue;
                        }
                        float dx = MathF.Abs(x + 0.5f - cx) - hx;
                        float qx = MathF.Max(dx, 0), qy = MathF.Max(dy, 0);
                        float distance = MathF.Sqrt(qx * qx + qy * qy) + MathF.Min(MathF.Max(dx, dy), 0) - radius;
                        float t = 1f - Math.Clamp((distance + blur * 0.35f) / (blur * 1.35f), 0f, 1f);
                        pixels[i] = (int)(maxAlpha * t * t * (3 - 2 * t)) << 24; // premultiplied black
                    }
                }
                var data = target.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
                }
                finally { target.UnlockBits(data); }
            }
            finally { System.Buffers.ArrayPool<int>.Shared.Return(pixels); }
        }
    }
}
