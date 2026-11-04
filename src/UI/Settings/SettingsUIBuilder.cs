using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    /// <summary>
    /// Row builders of the settings design system. Every page composes these; each row binds
    /// immediately to the draft, and page refresh pushes draft values back without re-firing bindings.
    /// </summary>
    public static class SettingsUIBuilder
    {
        // =============================================================
        //  Switch and text rows
        // =============================================================

        public static LiteCheck AddToggle(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, Func<bool> get, Action<bool> set)
        {
            var chk = new LiteCheck(get(), LanguageManager.T("Menu.Enable"));
            chk.CheckedChanged += (s, e) => set(chk.Checked);
            page.RegisterRefresh(() => chk.Checked = get());
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), chk));
            return chk;
        }

        public static LiteUnderlineInput AddInput(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, Func<string> get, Action<string> set, string placeholder = "", int width = 100, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var input = new LiteUnderlineInput(get(), "", "", width, null, align) { ClearButtonEnabled = true };
            if (!string.IsNullOrEmpty(placeholder)) input.Placeholder = placeholder;
            input.Inner.TextChanged += (s, e) => set(input.Inner.Text);
            page.RegisterRefresh(() => input.Inner.Text = get());
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), input));
            return input;
        }

        // =============================================================
        //  Numeric rows (stepper + free typing)
        // =============================================================

        public static LiteNumberInput AddInt(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, string unit, Func<int> get, Action<int> set, int width = 78, Color? color = null, int step = 1, int min = -9999, int max = 99999)
        {
            var input = new LiteNumberInput(get().ToString(), unit, "", width, color);
            input.EnableStepper(step, min, max);
            input.Inner.TextChanged += (s, e) =>
            {
                if (int.TryParse(input.Inner.Text, out int val)) set(val);
            };
            page.RegisterRefresh(() => input.Inner.Text = get().ToString());
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), input));
            return input;
        }

        /// <param name="rawTitle">A ready-made title; otherwise <paramref name="titleKey"/> is translated.</param>
        public static LiteNumberInput AddDouble(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, string unit, Func<double> get, Action<double> set, int width = 92, string? rawTitle = null, double step = 0)
        {
            var input = new LiteNumberInput(get().ToString(), unit, "", width);
            if (step > 0) input.EnableStepper(step, 0, 999999);
            input.Inner.TextChanged += (s, e) =>
            {
                if (double.TryParse(input.Inner.Text, out double val)) set(val);
            };
            page.RegisterRefresh(() => input.Inner.Text = get().ToString());
            group.AddItem(new LiteSettingsItem(rawTitle ?? LanguageManager.T(titleKey), input));
            return input;
        }

        public static LiteColorInput AddColor(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, Func<string> get, Action<string> set)
        {
            var input = new LiteColorInput(get());
            input.Input.Inner.TextChanged += (s, e) => set(input.HexValue);
            page.RegisterRefresh(() => input.HexValue = get());
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), input));
            return input;
        }

        // =============================================================
        //  Choice rows
        // =============================================================

        /// <summary>A slider over the exact values the setting accepts (shown in ascending order).</summary>
        public static LiteSlider AddSlider(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, IEnumerable<double> stops, Func<double, string> format, Func<double> get, Action<double> set)
        {
            var slider = new LiteSlider(stops, format);
            slider.SetValue(get());
            slider.ValueCommitted += value => { if (!page.IsRefreshing) set(value); };
            page.RegisterRefresh(() => slider.SetValue(get()));
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), slider));
            return slider;
        }

        /// <summary>A segmented control for a small, exclusive, always-visible choice.</summary>
        public static LiteSelector AddSegmented(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, IEnumerable<string> items, Func<int> get, Action<int> set)
        {
            var selector = new LiteSelector(vertical: false);
            int index = 0;
            foreach (var item in items) selector.Add(index++.ToString(), item);
            selector.SetSelected(Math.Clamp(get(), 0, selector.Count - 1), animate: false);
            selector.SelectionCommitted += selected => { if (!page.IsRefreshing) set(selected); };
            page.RegisterRefresh(() => selector.SetSelected(Math.Clamp(get(), 0, selector.Count - 1), animate: false));
            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), selector));
            return selector;
        }

        public static LiteComboBox AddCombo(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, IEnumerable<string> items, Func<string> get, Action<string> set)
        {
            var cmb = new LiteComboBox();
            foreach (var i in items) cmb.Items.Add(i);

            string current = get();
            if (cmb.Items.Contains(current)) cmb.SelectedItem = current;
            else if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;

            // Refreshing an asynchronously populated list must not replace its saved value.
            bool refreshing = false;
            cmb.Inner.SelectedIndexChanged += (s, e) => { if (!refreshing && !page.IsRefreshing && cmb.SelectedIndex >= 0) set(cmb.Text); };
            page.RegisterRefresh(() =>
            {
                refreshing = true;
                try
                {
                    string value = get();
                    if (cmb.Items.Contains(value)) cmb.SelectedItem = value;
                    else if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
                }
                finally { refreshing = false; }
            });

            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), cmb));
            return cmb;
        }

        public static LiteComboBox AddComboIndex(this LiteSettingsGroup group, SettingsPageBase page, string titleKey, IEnumerable<string> items, Func<int> get, Action<int> set)
        {
            var cmb = new LiteComboBox();
            foreach (var i in items) cmb.Items.Add(i);

            int idx = get();
            if (idx >= 0 && idx < cmb.Items.Count) cmb.SelectedIndex = idx;

            // 只保存有效选项，避免初始化/刷新时的 -1 临时状态写坏配置
            cmb.Inner.SelectedIndexChanged += (s, e) =>
            {
                int selectedIndex = cmb.SelectedIndex;
                if (!page.IsRefreshing && selectedIndex >= 0 && selectedIndex < cmb.Items.Count)
                    set(selectedIndex);
            };
            page.RegisterRefresh(() =>
            {
                int value = get();
                if (value >= 0 && value < cmb.Items.Count) cmb.SelectedIndex = value;
            });

            group.AddItem(new LiteSettingsItem(LanguageManager.T(titleKey), cmb));
            return cmb;
        }

        public static LiteComboBox AddComboPair(this LiteSettingsGroup group, SettingsPageBase page, string title, IEnumerable<dynamic> options, Func<string> get, Action<string> set)
        {
            var cmb = new LiteComboBox();
            foreach (var opt in options)
            {
                string label = "";
                string val = "";

                Type t = opt.GetType();
                var pLabel = t.GetProperty("Label");
                var pValue = t.GetProperty("Value");

                if (pLabel != null) label = pLabel.GetValue(opt)?.ToString() ?? "";
                if (pValue != null) val = pValue.GetValue(opt)?.ToString() ?? "";

                cmb.AddItem(label, val);
            }

            cmb.SelectValue(get());
            bool refreshing = false;
            cmb.Inner.SelectedIndexChanged += (s, e) => { if (!refreshing && !page.IsRefreshing && cmb.SelectedIndex >= 0) set(cmb.SelectedValue); };
            page.RegisterRefresh(() =>
            {
                refreshing = true;
                try { cmb.SelectValue(get()); }
                finally { refreshing = false; }
            });

            group.AddItem(new LiteSettingsItem(title, cmb));
            return cmb;
        }

        // =============================================================
        //  Composite rows
        // =============================================================

        public static LiteThresholdRow AddThreshold(this LiteSettingsGroup group, SettingsPageBase page, string title, string unit, ValueRange range)
        {
            var row = new LiteThresholdRow(page, title, unit, range);
            group.AddFullItem(row);
            return row;
        }

        /// <summary>Explanatory text; shown as the section footer under the group.</summary>
        public static void AddHint(this LiteSettingsGroup group, string text) => group.AddFooter(text);
    }
}
