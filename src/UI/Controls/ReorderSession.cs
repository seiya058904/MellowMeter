using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // One drag-to-reorder gesture over a list of same-type siblings in a StackPanel. The row lifts
    // into a floating sprite, neighbours spring aside, the viewport scrolls near its edges and Esc
    // cancels. Nothing about the data changes until a drop: the commit callback then performs exactly
    // the same child-order move as the Move up / Move down commands.
    internal sealed class ReorderSession : IMessageFilter
    {
        private readonly StackPanel _host;
        private readonly Control _item;
        private readonly ScrollSurface _surface;
        private readonly Action<Control, int> _commit;
        private readonly Action _finished;
        private readonly List<Control> _order; // visual order, top to bottom, excluding nothing
        private readonly int[] _heights;
        private readonly int _from;
        private readonly int _firstTop;
        private readonly ReorderGhost _ghost;
        private readonly int _pointerToItemTop;
        private readonly System.Windows.Forms.Timer _edge = new() { Interval = 12 };
        private long _lastEdge;
        private int _pointerScreenY;
        private int _to;
        private bool _ended;
        private readonly bool _followPointer;

        internal int InsertionIndex => _to;
        internal int SourceIndex => _from;
        internal static ReorderSession? Current { get; private set; }

        internal ReorderSession(StackPanel host, Control item, ScrollSurface surface, int pointerScreenY, Action<Control, int> commit, Action finished, bool followPointer = true)
        {
            _host = host; _item = item; _surface = surface; _commit = commit; _finished = finished;
            _order = host.Controls.Cast<Control>().Where(c => c.GetType() == item.GetType()).Reverse().ToList();
            _from = _to = _order.IndexOf(item);
            _heights = _order.Select(c => c.Height).ToArray();
            _firstTop = _order[0].Top;
            _pointerScreenY = pointerScreenY;
            _pointerToItemTop = pointerScreenY - item.PointToScreen(Point.Empty).Y;
            _ghost = new ReorderGhost(item);
            host.SuspendLayout();
            // The source stays laid out in the stack as a gap; the sprite is what moves.
            _ghost.Lift(item.PointToScreen(Point.Empty));
            // The pointer moves to the host first: hiding the row must not lose the capture.
            _followPointer = followPointer;
            if (followPointer) host.Capture = true;
            item.Visible = false;
            if (followPointer)
            {
                host.MouseCaptureChanged += HostCaptureChanged;
                Application.AddMessageFilter(this);
            }
            _edge.Tick += (_, _) => EdgeTick();
            Current = this;
            Update();
        }

        private int SlotTop(int index) // top of the dragged item if it were placed at index
        {
            int y = _firstTop;
            int passed = 0;
            for (int i = 0; passed < index && i < _order.Count; i++)
            {
                if (i == _from) continue;
                y += _heights[i];
                passed++;
            }
            return y;
        }

        internal void MoveTo(int pointerScreenY)
        {
            if (_ended) return;
            _pointerScreenY = pointerScreenY;
            Update();
            UpdateEdgeScroll();
        }

        private void Update()
        {
            int ghostTop = _pointerScreenY - _pointerToItemTop;
            // The sprite stays inside the viewport's vertical range.
            Rectangle viewport = _surface.RectangleToScreen(_surface.ClientRectangle);
            ghostTop = Math.Clamp(ghostTop, viewport.Top - _item.Height / 2, viewport.Bottom - _item.Height / 2);
            _ghost.MoveTo(ghostTop);
            int hostScreen = _host.PointToScreen(Point.Empty).Y;
            float center = ghostTop - hostScreen + _heights[_from] / 2f;
            int best = _to;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < _order.Count; k++)
            {
                float slot = SlotTop(k) + _heights[_from] / 2f;
                float distance = MathF.Abs(slot - center);
                if (distance < bestDistance) { bestDistance = distance; best = k; }
            }
            if (best != _to) { _to = best; Rearrange(); }
        }

        private void Rearrange()
        {
            int y = _firstTop, slot = 0;
            for (int i = 0; i < _order.Count; i++)
            {
                if (i == _from) continue;
                if (slot == _to) y += _heights[_from];
                AnimateRow(_order[i], y);
                y += _heights[i];
                slot++;
            }
        }

        private static void AnimateRow(Control row, int top)
        {
            if (row.Top == top && !InterfaceMotion.IsAnimating(row, "reorder")) return;
            InterfaceMotion.Spring(row, "reorder", SpringSpec.Reorder, row.Top, top, v => row.Top = (int)MathF.Round(v));
        }

        private void UpdateEdgeScroll()
        {
            Rectangle viewport = _surface.RectangleToScreen(_surface.ClientRectangle);
            int zone = UIUtils.S(44);
            bool near = _pointerScreenY < viewport.Top + zone || _pointerScreenY > viewport.Bottom - zone;
            if (near && !_edge.Enabled) { _lastEdge = System.Diagnostics.Stopwatch.GetTimestamp(); _edge.Start(); }
            else if (!near && _edge.Enabled) _edge.Stop();
        }

        private void EdgeTick()
        {
            if (_ended || _surface.IsDisposed || _host.IsDisposed) { _edge.Stop(); return; }
            Rectangle viewport = _surface.RectangleToScreen(_surface.ClientRectangle);
            int zone = UIUtils.S(44);
            float depth = _pointerScreenY < viewport.Top + zone
                ? -(viewport.Top + zone - _pointerScreenY) / (float)zone
                : (_pointerScreenY - (viewport.Bottom - zone)) / (float)zone;
            depth = Math.Clamp(depth, -1.5f, 1.5f);
            double dt = System.Diagnostics.Stopwatch.GetElapsedTime(_lastEdge).TotalSeconds;
            _lastEdge = System.Diagnostics.Stopwatch.GetTimestamp();
            float speed = UIUtils.S(620f) * depth;
            float before = _surface.Offset;
            _surface.ScrollTo(before + speed * (float)dt);
            if (_surface.Offset != before) Update();
        }

        internal void Drop()
        {
            if (_ended) return;
            Finish(commit: true);
        }

        internal void Cancel()
        {
            if (_ended) return;
            Finish(commit: false);
        }

        private void Finish(bool commit)
        {
            _ended = true;
            _edge.Stop();
            if (_followPointer)
            {
                Application.RemoveMessageFilter(this);
                _host.MouseCaptureChanged -= HostCaptureChanged;
                if (_host.Capture) _host.Capture = false;
            }
            if (ReferenceEquals(Current, this)) Current = null;
            if (!commit) { _to = _from; Rearrange(); }
            bool changed = commit && _to != _from;
            int targetSlotTop = SlotTop(_to);
            Point slotScreen = _host.PointToScreen(new Point(_item.Left, targetSlotTop));
            void Complete()
            {
                foreach (var row in _order) InterfaceMotion.Cancel(row, "reorder");
                _ghost.Dispose();
                _edge.Dispose();
                if (_item.IsDisposed || _host.IsDisposed) return;
                _item.Visible = true;
                if (changed) _commit(_item, _to);
                _host.ResumeLayout(true);
                _finished();
            }
            if (!InterfaceMotion.Enabled || !_surface.Visible) { Complete(); return; }
            _ghost.Settle(slotScreen, Complete);
        }

        private void HostCaptureChanged(object? sender, EventArgs e) { if (!_host.Capture) Cancel(); }

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0100: // WM_KEYDOWN
                    if ((Keys)(m.WParam.ToInt64() & 0xFFFF) == Keys.Escape) { Cancel(); return true; }
                    return false;
                case 0x0200: // WM_MOUSEMOVE
                    MoveTo(Cursor.Position.Y);
                    return true;
                case 0x0202: // WM_LBUTTONUP
                    Drop();
                    return true;
            }
            return false;
        }

        // The lifted row. It holds a one-off render of the row with a soft shadow, and moves as a
        // top-level sprite, so a drag frame re-renders nothing and repaints nothing underneath.
        private sealed class ReorderGhost : LayeredSprite
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
            private readonly Bitmap _frame, _scaled;
            private readonly int _margin;
            private readonly int _left;
            private readonly Size _source;
            private readonly Form? _form;
            private float _lift;
            private int _top;
            private readonly Action<float> _liftFrame;

            internal ReorderGhost(Control item) : base(clickThrough: true)
            {
                _source = new Size(item.Width, item is IReorderVisual visual ? visual.VisualHeight : item.Height);
                _form = item.FindForm();
                Owner = _form;
                _left = item.PointToScreen(Point.Empty).X;
                _margin = UIUtils.S(22);
                int radius = UIUtils.S(item is IReorderVisual ? DS.RadiusCard : DS.RadiusControl);
                var size = new Size(_source.Width + _margin * 2, _source.Height + _margin * 2);
                _frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                _scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                SoftShadow.Paint(_frame, new Rectangle(_margin, _margin, _source.Width, _source.Height), radius, UIUtils.S(18), UIUtils.S(6), UIColors.IsDark ? 0.5f : 0.26f);
                using var content = new GdiCanvas(item.Width, item.Height);
                // WM_PRINT into the DIB: the same request DrawToBitmap makes, but ClearType stays on.
                SendMessage(item.Handle, 0x0317, content.Hdc, new IntPtr(0x4 | 0x8 | 0x10)); // PRF_CLIENT | PRF_ERASEBKGND | PRF_CHILDREN
                using var g = Graphics.FromImage(_frame);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var texture = new TextureBrush(content.Pixels, WrapMode.Clamp);
                texture.TranslateTransform(_margin, _margin);
                g.FillPath(texture, Ink.Round(new RectangleF(_margin, _margin, _source.Width, _source.Height), radius));
                _liftFrame = v => { _lift = v; Render(); };
            }

            internal void Lift(Point itemScreen)
            {
                _top = itemScreen.Y;
                _lift = 0;
                Render();
                InterfaceMotion.Spring(this, "lift", SpringSpec.Lift, 0, 1, _liftFrame);
            }

            private Point FramePosition => new(_left - _margin, _top - _margin);

            private void Render()
            {
                if (_lift >= 0.995f && _lift <= 1.005f) { Present(_frame, FramePosition, 1); return; }
                // Scale from the centre by up to 2 %; the fade of the shadow happens through opacity.
                float scale = 1 + 0.02f * _lift;
                using (var g = Graphics.FromImage(_scaled))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    float w = _frame.Width * scale, h = _frame.Height * scale;
                    g.DrawImage(_frame, (_frame.Width - w) / 2f, (_frame.Height - h) / 2f, w, h);
                }
                Present(_scaled, FramePosition, 1);
            }

            internal void MoveTo(int screenTop)
            {
                _top = screenTop;
                if (InterfaceMotion.IsAnimating(this, "lift")) return; // the lift frame presents the new top
                Reposition(FramePosition, 1);
            }

            internal void Settle(Point slotScreen, Action completed)
            {
                InterfaceMotion.Cancel(this);
                int from = _top;
                InterfaceMotion.Spring(this, "lift", SpringSpec.Selection, _lift, 0, _liftFrame);
                InterfaceMotion.Spring(this, "settle", SpringSpec.Reorder, from, slotScreen.Y, v =>
                {
                    _top = (int)MathF.Round(v);
                    if (!InterfaceMotion.IsAnimating(this, "lift")) Reposition(FramePosition, 1);
                }, completed);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    InterfaceMotion.Cancel(this);
                    if (Visible) Hide();
                    _frame.Dispose();
                    _scaled.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
