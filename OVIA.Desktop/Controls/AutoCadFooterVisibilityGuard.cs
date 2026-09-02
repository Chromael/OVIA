using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OVIA.Desktop;

namespace OVIA.Desktop.Controls
{
    /// <summary>
    /// AutoCAD 실행 상태를 OVIA 전체 UI에 동일 기준으로 반영합니다.
    ///
    /// 핵심 규칙:
    /// - acad.exe 미실행: Power 아이콘 + "AutoCAD OFF" = 빨간색
    /// - acad.exe 실행:   Power 아이콘 + "AutoCAD ON"  = 녹색
    /// - 하단 버전: 실행 중이며 연도를 확인한 경우에만 " | AutoCAD V. : 2024" 형식으로 표시
    /// - AutoCAD 종료 시 AutoCAD V. 구간 자체를 제거
    /// - 별도 상태 Timer를 만들지 않으며, 기존 Header Timer 뒤에 연결하거나 Application.Idle에서 정규화
    /// - CAD 추출/Shape/로그인/ERP 로직에는 관여하지 않음
    /// </summary>
    public static class AutoCadFooterVisibilityGuard
    {
        private const string AttachMarker = "OVIA_AUTOCAD_RUNTIME_UI_20260902_10";
        private const string PowerIcon = "\uE7E8";

        // OVIA 기존 상태 의미를 명확히 유지하기 위해 theme primary/brown 계열을 사용하지 않습니다.
        private static readonly Color AutoCadOnColor = Color.FromArgb(22, 163, 74);   // #16A34A
        private static readonly Color AutoCadOffColor = Color.FromArgb(220, 38, 38);  // #DC2626

        private static readonly Regex FooterVersionRegex = new Regex(
            @"\s*\|\s*AutoCAD\s*V\.\s*:\s*[^|\r\n]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly ConditionalWeakTable<Timer, Control> TimerRoots =
            new ConditionalWeakTable<Timer, Control>();

        private static readonly object GlobalSyncLock = new object();
        private static bool globalSynchronizationStarted;
        private static int lastIdleRefreshTick;
        private static bool refreshing;

        public static string Marker
        {
            get { return AttachMarker; }
        }

        /// <summary>
        /// 기존 소스 어디에서든 Resolver 또는 Guard가 한 번 사용되면 전체 열린 Form을 대상으로 동기화합니다.
        /// 별도의 Apply 스크립트로 OviaWorkspaceHeader.cs를 수정할 필요가 없습니다.
        /// </summary>
        public static void EnsureGlobalSynchronization()
        {
            if (globalSynchronizationStarted)
            {
                return;
            }

            lock (GlobalSyncLock)
            {
                if (globalSynchronizationStarted)
                {
                    return;
                }

                Application.Idle -= Application_Idle;
                Application.Idle += Application_Idle;
                globalSynchronizationStarted = true;
            }
        }

        public static void Attach(Control root)
        {
            EnsureGlobalSynchronization();

            if (root == null || root.IsDisposed)
            {
                return;
            }

            root.HandleCreated -= Root_HandleCreated;
            root.HandleCreated += Root_HandleCreated;
            root.ControlAdded -= Root_ControlAdded;
            root.ControlAdded += Root_ControlAdded;

            AttachControlRecursive(root);
            AttachToExistingTimers(root);

            if (root.IsHandleCreated)
            {
                RefreshRootAndForm(root);
            }
        }

        public static void Refresh(Control root)
        {
            EnsureGlobalSynchronization();
            RefreshRootAndForm(root);
        }

        private static void Application_Idle(object sender, EventArgs e)
        {
            if (refreshing)
            {
                return;
            }

            // Idle은 매우 자주 발생할 수 있으므로 250ms보다 자주 프로세스 검색을 하지 않습니다.
            int now = Environment.TickCount;
            int elapsed = unchecked(now - lastIdleRefreshTick);
            if (lastIdleRefreshTick != 0 && elapsed >= 0 && elapsed < 250)
            {
                return;
            }

            lastIdleRefreshTick = now;
            RefreshAllOpenForms();
        }

        private static void RefreshAllOpenForms()
        {
            if (refreshing)
            {
                return;
            }

            refreshing = true;
            try
            {
                AutoCadRuntimeSnapshot snapshot = AutoCadVersionResolver.GetRunningAutoCad();

                List<Form> forms = new List<Form>();
                try
                {
                    int i;
                    for (i = 0; i < Application.OpenForms.Count; i++)
                    {
                        Form form = Application.OpenForms[i];
                        if (form != null && !form.IsDisposed)
                        {
                            forms.Add(form);
                        }
                    }
                }
                catch
                {
                }

                int formIndex;
                for (formIndex = 0; formIndex < forms.Count; formIndex++)
                {
                    Form form = forms[formIndex];
                    if (form == null || form.IsDisposed)
                    {
                        continue;
                    }

                    AttachControlRecursive(form);
                    AttachHeaderTimersRecursive(form);
                    NormalizeTree(form, snapshot);
                }
            }
            catch
            {
                // UI 상태 표시 실패가 업무 기능을 중단시키지 않도록 방어합니다.
            }
            finally
            {
                refreshing = false;
            }
        }

        private static void Root_HandleCreated(object sender, EventArgs e)
        {
            Control root = sender as Control;
            if (root == null || root.IsDisposed)
            {
                return;
            }

            AttachToExistingTimers(root);
            RefreshRootAndForm(root);
        }

        private static void Root_ControlAdded(object sender, ControlEventArgs e)
        {
            if (e == null || e.Control == null)
            {
                return;
            }

            AttachControlRecursive(e.Control);
            AttachHeaderTimersRecursive(e.Control);

            AutoCadRuntimeSnapshot snapshot = AutoCadVersionResolver.GetRunningAutoCad();
            NormalizeTree(e.Control, snapshot);
        }

        private static void AttachControlRecursive(Control control)
        {
            if (control == null || control.IsDisposed)
            {
                return;
            }

            control.TextChanged -= Control_RuntimeVisualChanged;
            control.TextChanged += Control_RuntimeVisualChanged;
            control.ControlAdded -= Root_ControlAdded;
            control.ControlAdded += Root_ControlAdded;

            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                strip.ItemAdded -= ToolStrip_ItemAdded;
                strip.ItemAdded += ToolStrip_ItemAdded;

                int itemIndex;
                for (itemIndex = 0; itemIndex < strip.Items.Count; itemIndex++)
                {
                    AttachToolStripItemRecursive(strip.Items[itemIndex]);
                }
            }

            int i;
            for (i = 0; i < control.Controls.Count; i++)
            {
                AttachControlRecursive(control.Controls[i]);
            }
        }

        private static void ToolStrip_ItemAdded(object sender, ToolStripItemEventArgs e)
        {
            if (e == null || e.Item == null)
            {
                return;
            }

            AttachToolStripItemRecursive(e.Item);
            NormalizeToolStripItemRecursive(e.Item, AutoCadVersionResolver.GetRunningAutoCad());
        }

        private static void AttachToolStripItemRecursive(ToolStripItem item)
        {
            if (item == null)
            {
                return;
            }

            item.TextChanged -= ToolStripItem_RuntimeVisualChanged;
            item.TextChanged += ToolStripItem_RuntimeVisualChanged;

            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null && dropDown.HasDropDownItems)
            {
                int i;
                for (i = 0; i < dropDown.DropDownItems.Count; i++)
                {
                    AttachToolStripItemRecursive(dropDown.DropDownItems[i]);
                }
            }
        }

        private static void Control_RuntimeVisualChanged(object sender, EventArgs e)
        {
            if (refreshing)
            {
                return;
            }

            Control control = sender as Control;
            if (control == null || control.IsDisposed)
            {
                return;
            }

            NormalizeControl(control, AutoCadVersionResolver.GetRunningAutoCad());
        }

        private static void ToolStripItem_RuntimeVisualChanged(object sender, EventArgs e)
        {
            if (refreshing)
            {
                return;
            }

            ToolStripItem item = sender as ToolStripItem;
            if (item == null)
            {
                return;
            }

            NormalizeToolStripItemRecursive(item, AutoCadVersionResolver.GetRunningAutoCad());
        }

        private static void AttachHeaderTimersRecursive(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            string typeName = root.GetType().Name;
            if (string.Equals(typeName, "OviaWorkspaceHeader", StringComparison.Ordinal) ||
                typeName.IndexOf("WorkspaceHeader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AttachToExistingTimers(root);
            }

            int i;
            for (i = 0; i < root.Controls.Count; i++)
            {
                AttachHeaderTimersRecursive(root.Controls[i]);
            }
        }

        /// <summary>
        /// OviaWorkspaceHeader가 보유한 기존 Timer에 뒤쪽 Tick 핸들러로 연결합니다.
        /// 새 Timer를 만들지 않으므로 갈색→초록/빨강 깜빡임의 원인이 되는 경쟁 Timer를 추가하지 않습니다.
        /// </summary>
        private static void AttachToExistingTimers(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            Type type = root.GetType();
            while (type != null && typeof(Control).IsAssignableFrom(type))
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(
                        BindingFlags.Instance |
                        BindingFlags.NonPublic |
                        BindingFlags.Public |
                        BindingFlags.DeclaredOnly);
                }
                catch
                {
                    fields = new FieldInfo[0];
                }

                int i;
                for (i = 0; i < fields.Length; i++)
                {
                    if (!typeof(Timer).IsAssignableFrom(fields[i].FieldType))
                    {
                        continue;
                    }

                    Timer timer = null;
                    try
                    {
                        timer = fields[i].GetValue(root) as Timer;
                    }
                    catch
                    {
                    }

                    if (timer == null)
                    {
                        continue;
                    }

                    Control ignored;
                    if (TimerRoots.TryGetValue(timer, out ignored))
                    {
                        continue;
                    }

                    try
                    {
                        TimerRoots.Add(timer, root);
                        timer.Tick += ExistingTimer_Tick;
                    }
                    catch
                    {
                    }
                }

                type = type.BaseType;
            }
        }

        private static void ExistingTimer_Tick(object sender, EventArgs e)
        {
            Timer timer = sender as Timer;
            if (timer == null)
            {
                return;
            }

            Control root;
            if (!TimerRoots.TryGetValue(timer, out root) || root == null || root.IsDisposed)
            {
                return;
            }

            // 기존 Header Tick이 먼저 실행된 뒤 이 핸들러가 최종 화면 상태를 확정합니다.
            RefreshRootAndForm(root);
        }

        private static void RefreshRootAndForm(Control root)
        {
            if (root == null || root.IsDisposed || refreshing)
            {
                return;
            }

            refreshing = true;
            try
            {
                AutoCadRuntimeSnapshot snapshot = AutoCadVersionResolver.GetRunningAutoCad();
                NormalizeTree(root, snapshot);

                Form form = null;
                try
                {
                    form = root.FindForm();
                }
                catch
                {
                }

                if (form != null && !form.IsDisposed && !object.ReferenceEquals(form, root))
                {
                    NormalizeTree(form, snapshot);
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        private static void NormalizeTree(Control root, AutoCadRuntimeSnapshot snapshot)
        {
            if (root == null || root.IsDisposed || snapshot == null)
            {
                return;
            }

            NormalizeControl(root, snapshot);

            ToolStrip strip = root as ToolStrip;
            if (strip != null)
            {
                int itemIndex;
                for (itemIndex = 0; itemIndex < strip.Items.Count; itemIndex++)
                {
                    NormalizeToolStripItemRecursive(strip.Items[itemIndex], snapshot);
                }
            }

            int i;
            for (i = 0; i < root.Controls.Count; i++)
            {
                NormalizeTree(root.Controls[i], snapshot);
            }
        }

        private static void NormalizeControl(Control control, AutoCadRuntimeSnapshot snapshot)
        {
            if (control == null || control.IsDisposed || snapshot == null)
            {
                return;
            }

            string text = control.Text == null ? string.Empty : control.Text;
            string trimmed = text.Trim();

            if (IsAutoCadStatusText(trimmed))
            {
                string expected = snapshot.IsRunning ? "AutoCAD ON" : "AutoCAD OFF";
                Color expectedColor = snapshot.IsRunning ? AutoCadOnColor : AutoCadOffColor;

                if (!string.Equals(text, expected, StringComparison.Ordinal))
                {
                    control.Text = expected;
                }

                ApplyVisualColor(control, expectedColor);
                NormalizePowerIconNear(control, expectedColor);
                return;
            }

            if (IsFooterText(text))
            {
                string expectedFooter = BuildFooterText(text, snapshot);
                if (!string.Equals(text, expectedFooter, StringComparison.Ordinal))
                {
                    control.Text = expectedFooter;
                }
            }
        }

        private static void NormalizeToolStripItemRecursive(ToolStripItem item, AutoCadRuntimeSnapshot snapshot)
        {
            if (item == null || snapshot == null)
            {
                return;
            }

            string text = item.Text == null ? string.Empty : item.Text;
            string trimmed = text.Trim();

            if (IsAutoCadStatusText(trimmed))
            {
                string expected = snapshot.IsRunning ? "AutoCAD ON" : "AutoCAD OFF";
                Color expectedColor = snapshot.IsRunning ? AutoCadOnColor : AutoCadOffColor;

                if (!string.Equals(text, expected, StringComparison.Ordinal))
                {
                    item.Text = expected;
                }

                if (item.ForeColor != expectedColor)
                {
                    item.ForeColor = expectedColor;
                }

                ApplyReflectionColor(item, expectedColor);
            }
            else if (IsFooterText(text))
            {
                string expectedFooter = BuildFooterText(text, snapshot);
                if (!string.Equals(text, expectedFooter, StringComparison.Ordinal))
                {
                    item.Text = expectedFooter;
                }
            }

            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null && dropDown.HasDropDownItems)
            {
                int i;
                for (i = 0; i < dropDown.DropDownItems.Count; i++)
                {
                    NormalizeToolStripItemRecursive(dropDown.DropDownItems[i], snapshot);
                }
            }
        }

        private static bool IsAutoCadStatusText(string text)
        {
            return string.Equals(text, "AutoCAD ON", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "AutoCAD OFF", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFooterText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (text.IndexOf("AutoCAD V.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return text.IndexOf("Biz ID", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   text.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsStandaloneAutoCadVersionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.TrimStart().StartsWith("AutoCAD V.", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFooterText(string source, AutoCadRuntimeSnapshot snapshot)
        {
            string text = source == null ? string.Empty : source;
            string baseText = FooterVersionRegex.Replace(text, string.Empty).TrimEnd();

            if (!snapshot.IsRunning || snapshot.Year <= 0)
            {
                if (IsStandaloneAutoCadVersionText(baseText))
                {
                    return string.Empty;
                }

                return baseText;
            }

            string versionText = "AutoCAD V. : " + snapshot.Year.ToString();

            if (IsStandaloneAutoCadVersionText(baseText) || baseText.Length == 0)
            {
                return versionText;
            }

            return baseText + " | " + versionText;
        }

        private static void NormalizePowerIconNear(Control statusControl, Color color)
        {
            if (statusControl == null || statusControl.IsDisposed)
            {
                return;
            }

            // 상태 Label 자신이 아이콘을 함께 그리는 커스텀 Control일 수 있습니다.
            ApplyReflectionColor(statusControl, color);

            Control searchRoot = statusControl.Parent;
            int level;
            for (level = 0; level < 3 && searchRoot != null; level++)
            {
                ApplyPowerIconInTree(searchRoot, statusControl, color);
                searchRoot = searchRoot.Parent;
            }
        }

        private static void ApplyPowerIconInTree(Control root, Control statusControl, Color color)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            if (!object.ReferenceEquals(root, statusControl) &&
                IsLikelyPowerIconControl(root, statusControl))
            {
                ApplyVisualColor(root, color);
            }

            int i;
            for (i = 0; i < root.Controls.Count; i++)
            {
                ApplyPowerIconInTree(root.Controls[i], statusControl, color);
            }
        }

        private static bool IsLikelyPowerIconControl(Control control, Control statusControl)
        {
            if (control == null || control.IsDisposed || statusControl == null)
            {
                return false;
            }

            string text = control.Text == null ? string.Empty : control.Text;
            if (text.IndexOf(PowerIcon, StringComparison.Ordinal) >= 0)
            {
                return IsControlNearStatus(control, statusControl);
            }

            string reflectedIconText = ReadStringMember(control, new string[]
            {
                "IconText", "IconGlyph", "Glyph", "Symbol", "IconSymbol"
            });

            if (!string.IsNullOrEmpty(reflectedIconText) &&
                reflectedIconText.IndexOf(PowerIcon, StringComparison.Ordinal) >= 0)
            {
                return IsControlNearStatus(control, statusControl);
            }

            string name = control.Name == null ? string.Empty : control.Name;
            bool nameLooksLikeAutoCadPower =
                name.IndexOf("autocad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("cadstatus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("cadpower", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("power", StringComparison.OrdinalIgnoreCase) >= 0;

            return nameLooksLikeAutoCadPower && IsControlNearStatus(control, statusControl);
        }

        private static bool IsControlNearStatus(Control candidate, Control statusControl)
        {
            try
            {
                Point a = candidate.PointToScreen(new Point(candidate.Width / 2, candidate.Height / 2));
                Point b = statusControl.PointToScreen(new Point(statusControl.Width / 2, statusControl.Height / 2));

                int dx = Math.Abs(a.X - b.X);
                int dy = Math.Abs(a.Y - b.Y);

                // AutoCAD 상태 아이콘은 상태 텍스트의 바로 옆에 위치합니다.
                return dx <= 140 && dy <= 50;
            }
            catch
            {
                return object.ReferenceEquals(candidate.Parent, statusControl.Parent);
            }
        }

        private static void ApplyVisualColor(Control control, Color color)
        {
            if (control == null || control.IsDisposed)
            {
                return;
            }

            try
            {
                if (control.ForeColor != color)
                {
                    control.ForeColor = color;
                }

                ApplyReflectionColor(control, color);
                control.Invalidate();

                if (control.Parent != null)
                {
                    control.Parent.Invalidate();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Ovia 커스텀 아이콘/상태 Control이 ForeColor가 아닌 별도 속성으로 그리는 경우까지 처리합니다.
        /// 갈색 theme 값이 계속 남는 문제를 막기 위한 시각 상태 전용 보정입니다.
        /// </summary>
        private static void ApplyReflectionColor(object target, Color color)
        {
            if (target == null)
            {
                return;
            }

            string[] names = new string[]
            {
                "IconColor",
                "TextColor",
                "GlyphColor",
                "SymbolColor",
                "StatusColor",
                "NormalColor",
                "NormalForeColor",
                "ContentColor"
            };

            Type type = target.GetType();
            int i;
            for (i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        names[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (property != null &&
                        property.CanWrite &&
                        property.PropertyType == typeof(Color))
                    {
                        property.SetValue(target, color, null);
                    }
                }
                catch
                {
                }

                try
                {
                    FieldInfo field = type.GetField(
                        names[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field != null && field.FieldType == typeof(Color))
                    {
                        field.SetValue(target, color);
                    }
                }
                catch
                {
                }
            }
        }

        private static string ReadStringMember(object target, string[] names)
        {
            if (target == null || names == null)
            {
                return string.Empty;
            }

            Type type = target.GetType();
            int i;
            for (i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        names[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(target, null) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    FieldInfo field = type.GetField(
                        names[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(target) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }
            }

            return string.Empty;
        }
    }
}
