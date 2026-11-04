using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public interface ISettingsPage
    {
        void Save();
        void OnShow();
    }

    public class SettingsPageBase : UserControl, ISettingsPage
    {
        protected Settings? Config;
        protected MainForm? MainForm;
        protected UIController? UI;

        // ★★★ Refresh Mechanism for Deferred Load ★★★
        protected List<Action> _refreshActions = new List<Action>();

        // The page's scrolling surface; null only for pages that build their own structure first.
        internal ScrollSurface? Surface { get; private set; }
        private CompactTitleBar? _titleBar;
        private int _titleBottom;

        public static Color GlobalBackColor => UIColors.MainBg;

        public SettingsPageBase()
        {
            this.BackColor = GlobalBackColor;
            this.Dock = DockStyle.Fill;
            this.DoubleBuffered = true;
            this.Padding = Padding.Empty;
        }

        // Creates the scrolling surface and returns the stack that holds the page's groups.
        // With a title, the page opens with a large title that collapses into a compact bar on scroll.
        internal StackPanel CreateSurface(string? largeTitle, Padding? contentPadding = null)
        {
            var surface = new ScrollSurface { Dock = DockStyle.Fill };
            Surface = surface;
            var content = surface.Content;
            content.Padding = contentPadding ?? UIUtils.S(new Padding(DS.PageGutter, DS.PageTop, DS.PageGutter, DS.PageBottom));
            if (!string.IsNullOrEmpty(largeTitle))
            {
                var title = new LargeTitle(largeTitle);
                content.Controls.Add(title);
                _titleBottom = content.Padding.Top + title.Height;
                _titleBar = new CompactTitleBar(largeTitle);
                surface.Controls.Add(_titleBar);
                _titleBar.BringToFront();
                surface.Scrolled += UpdateTitleBar;
            }
            Controls.Add(surface);
            return content;
        }

        private void UpdateTitleBar(float offset)
        {
            if (_titleBar == null || Surface == null) return;
            int full = UIUtils.S(40);
            int height = Math.Clamp((int)offset - _titleBottom + UIUtils.S(6), 0, full);
            if (height == 0) { if (_titleBar.Visible) _titleBar.Visible = false; return; }
            int width = Surface.ClientSize.Width - UIUtils.S(16);
            if (_titleBar.Height != height || _titleBar.Width != width) _titleBar.SetBounds(0, 0, width, height);
            _titleBar.Progress = height / (float)full;
            if (!_titleBar.Visible) _titleBar.Visible = true;
        }

        protected void AddGroupToPage(LiteSettingsGroup group)
        {
            if (Surface == null) return;
            Surface.Content.Controls.Add(group);
            Surface.Content.Controls.SetChildIndex(group, 0); // stack order: index 0 is the bottom
        }

        public void SetContext(Settings cfg, MainForm? form, UIController? ui)
        {
            Config = cfg;
            MainForm = form;
            UI = ui;

            // ★★★ Fix: Ensure UI controls reflect the Config values immediately ★★★
            // This prevents controls from defaulting to false/0 and overwriting the config on Save()
            if (Config != null)
            {
                RefreshBindings(() => { foreach (var action in _refreshActions) action(); });
            }
        }

        internal bool IsRefreshing { get; private set; }
        internal void RefreshBindings(Action action)
        {
            bool previous = IsRefreshing;
            IsRefreshing = true;
            try { action(); } finally { IsRefreshing = previous; }
        }

        public void RegisterRefresh(Action action)
        {
            _refreshActions.Add(action);
        }

        public virtual void OnShow()
        {
            // Execute refresh actions to ensure UI reflects the latest Config
            if (Config != null)
            {
                RefreshBindings(() => { foreach (var action in _refreshActions) action(); });
            }
        }

        public virtual void Save()
        {
            // Immediate binding means we don't need to do anything here.
            // But we keep the method because ISettingsPage requires it.
        }

        internal void PlayEntrance() => Surface?.PlayRise();

        // Keyboard: paging keys scroll when the focused control does not use them itself (text boxes
        // and pickers declare them as input keys and never reach this), and Tab keeps focus on screen.
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (Surface != null && keyData is Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End && Surface.HandleKey(keyData)) return true;
            return base.ProcessDialogKey(keyData);
        }

        internal bool ScrollKey(Keys keyData) => Surface != null && Surface.HandleKey(keyData);

        protected override bool ProcessTabKey(bool forward)
        {
            bool moved = base.ProcessTabKey(forward);
            if (moved) RevealActiveControl();
            return moved;
        }

        internal void RevealActiveControl()
        {
            Control? active = ActiveControl;
            while (active is ContainerControl { ActiveControl: { } inner }) active = inner;
            if (active != null) Surface?.EnsureVisible(active);
        }

        public sealed record ViewState(Point[] Scroll, bool[] Collapsed)
        {
            public bool TaskbarTab { get; init; }
            public bool OnlyVisible { get; init; }
            public bool EditLabels { get; init; }
        }
        private IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var item in Descendants(child)) yield return item;
            }
        }
        public virtual ViewState CaptureView() => new(
            Surface == null ? Array.Empty<Point>() : new[] { new Point(0, (int)Surface.Offset) },
            Descendants(this).OfType<LiteSettingsGroup>().Select(c => c.Collapsed).ToArray());
        public virtual void RestoreView(ViewState state)
        {
            int i = 0;
            foreach (var group in Descendants(this).OfType<LiteSettingsGroup>())
                if (i < state.Collapsed.Length) group.RestoreCollapsed(state.Collapsed[i++]);
            PerformLayout();
            if (Surface != null && state.Scroll.Length > 0)
            {
                Surface.Content.PerformLayout();
                Surface.ScrollTo(state.Scroll[0].Y);
            }
        }

        protected void ClearAndDispose(Control.ControlCollection controls)
        {
            // Clear pending actions whenever we destroy the UI controls
            _refreshActions.Clear();

            while (controls.Count > 0)
            {
                var c = controls[0];
                controls.RemoveAt(0);
                c.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            var fonts = disposing ? ControlResources.CaptureFonts(this) : null;
            base.Dispose(disposing);
            if (fonts != null) foreach (var font in fonts) font.Dispose();
        }

        protected void EnsureSafeVisibility(LiteCheck? chkHideMain, LiteCheck? chkHideTray, LiteCheck? chkShowTaskbar)
        {
            if (Config == null) return;
            bool hideMain = chkHideMain != null ? chkHideMain.Checked : Config.HideMainForm;
            bool hideTray = chkHideTray != null ? chkHideTray.Checked : Config.HideTrayIcon;
            bool showBar = chkShowTaskbar != null ? chkShowTaskbar.Checked : Config.ShowTaskbar;

            if (hideMain && hideTray && !showBar)
            {
                MessageBox.Show(LanguageManager.T("Menu.VisibilitySafety"),
                                "MellowMeter", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                if (chkHideMain != null) chkHideMain.Checked = false;
                if (chkHideTray != null) chkHideTray.Checked = false;
                if (chkShowTaskbar != null) chkShowTaskbar.Checked = true;
            }
        }

        // Page header: scrolls with the content like an iOS large title.
        private sealed class LargeTitle : Control
        {
            public LargeTitle(string text)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Text = text;
                Font = UiFonts.Get(TextRole.LargeTitle);
                Height = UIUtils.S(46);
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = text;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), ClientRectangle);
                Ink.Line(e.Graphics, Text, Font, new Rectangle(UIUtils.S(DS.RowInset), 0, Math.Max(0, Width - UIUtils.S(DS.RowInset)), Height - UIUtils.S(8)), UIColors.TextMain);
            }
        }

        // Grows in from the top once the large title has scrolled away.
        private sealed class CompactTitleBar : Control
        {
            private float _progress;
            internal float Progress { set { if (Math.Abs(_progress - value) < 0.01f) return; _progress = value; Invalidate(); } }

            public CompactTitleBar(string text)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Text = text;
                Visible = false;
                Font = UiFonts.Get(TextRole.Headline);
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = text;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Color page = UIColors.MainBg;
                g.FillRectangle(Ink.B(page), ClientRectangle);
                int full = UIUtils.S(40);
                Ink.Line(g, Text, Font, new Rectangle(UIUtils.S(16), Height - full, Math.Max(0, Width - UIUtils.S(32)), full), UIColors.Blend(page, UIColors.TextMain, _progress), TextFormatFlags.HorizontalCenter);
                g.DrawLine(Ink.P(UIColors.Blend(page, UIColors.TextMain, 0.10f * _progress), 1), 0, Height - 1, Width, Height - 1);
            }
        }
    }
}
