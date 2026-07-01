using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    /// <summary>
    /// Windows 기본 타이틀바를 유지한 상태에서 활성/비활성 창 색상만 OVIA 기준으로 부드럽게 전환한다.
    /// FormBorderStyle.None 기반의 완전 커스텀 타이틀바를 만들지 않아 창 이동/최소화/최대화/닫기 동작을 건드리지 않는다.
    /// </summary>
    internal sealed class OviaWindowCaptionTheme : IDisposable
    {
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int AnimationSteps = 8;
        private const int AnimationInterval = 18;

        private readonly Form form;
        private readonly Timer animationTimer;
        private readonly Color activeCaptionColor = Color.FromArgb(218, 218, 218);
        private readonly Color inactiveCaptionColor = Color.FromArgb(232, 232, 232);
        private static Icon cachedSymbolIcon;

        private Color startColor;
        private Color currentColor;
        private Color targetColor;
        private int animationStep;
        private bool disposed;

        private OviaWindowCaptionTheme(Form form)
        {
            this.form = form;
            ApplyOviaSymbolIcon(form);

            currentColor = inactiveCaptionColor;
            targetColor = inactiveCaptionColor;
            startColor = inactiveCaptionColor;

            animationTimer = new Timer();
            animationTimer.Interval = AnimationInterval;
            animationTimer.Tick += AnimationTimer_Tick;

            form.HandleCreated += Form_HandleCreated;
            form.Activated += Form_Activated;
            form.Deactivate += Form_Deactivate;
            form.Disposed += Form_Disposed;

            if (form.IsHandleCreated)
            {
                ApplyCaptionColor(currentColor);
            }
        }

        public static OviaWindowCaptionTheme Attach(Form form)
        {
            if (form == null)
            {
                return null;
            }

            return new OviaWindowCaptionTheme(form);
        }

        private void Form_HandleCreated(object sender, EventArgs e)
        {
            currentColor = form.ContainsFocus || Form.ActiveForm == form ? activeCaptionColor : inactiveCaptionColor;
            targetColor = currentColor;
            startColor = currentColor;
            ApplyCaptionColor(currentColor);
        }

        private void Form_Activated(object sender, EventArgs e)
        {
            BeginTransition(activeCaptionColor);
        }

        private void Form_Deactivate(object sender, EventArgs e)
        {
            BeginTransition(inactiveCaptionColor);
        }

        private void BeginTransition(Color nextColor)
        {
            if (disposed)
            {
                return;
            }

            if (!form.IsHandleCreated)
            {
                currentColor = nextColor;
                targetColor = nextColor;
                return;
            }

            startColor = currentColor;
            targetColor = nextColor;
            animationStep = 0;

            if (startColor.ToArgb() == targetColor.ToArgb())
            {
                ApplyCaptionColor(targetColor);
                return;
            }

            animationTimer.Stop();
            animationTimer.Start();
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            animationStep++;
            double ratio = Math.Min(1D, animationStep / (double)AnimationSteps);
            currentColor = Interpolate(startColor, targetColor, EaseOut(ratio));
            ApplyCaptionColor(currentColor);

            if (ratio >= 1D)
            {
                animationTimer.Stop();
                currentColor = targetColor;
                ApplyCaptionColor(currentColor);
            }
        }

        private static double EaseOut(double t)
        {
            return 1D - Math.Pow(1D - t, 2D);
        }

        private static Color Interpolate(Color from, Color to, double ratio)
        {
            int r = from.R + (int)Math.Round((to.R - from.R) * ratio);
            int g = from.G + (int)Math.Round((to.G - from.G) * ratio);
            int b = from.B + (int)Math.Round((to.B - from.B) * ratio);
            return Color.FromArgb(Clamp(r), Clamp(g), Clamp(b));
        }

        private static int Clamp(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        private static void ApplyOviaSymbolIcon(Form targetForm)
        {
            if (targetForm == null)
            {
                return;
            }

            try
            {
                Icon icon = LoadOviaSymbolIcon();
                if (icon != null)
                {
                    targetForm.Icon = (Icon)icon.Clone();
                }
            }
            catch
            {
                // 심볼 아이콘 로드에 실패하더라도 프로그램 실행을 막지 않는다.
                // 이 경우 Windows 기본 Form 아이콘을 유지한다.
            }
        }

        private static Icon LoadOviaSymbolIcon()
        {
            if (cachedSymbolIcon != null)
            {
                return cachedSymbolIcon;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string startupDirectory = Application.StartupPath;
            string[] candidates = new string[]
            {
                Path.Combine(startupDirectory, "Assets", "Icons", "ovia_symbol.ico"),
                Path.Combine(baseDirectory, "Assets", "Icons", "ovia_symbol.ico"),
                Path.Combine(baseDirectory, "..", "..", "Assets", "Icons", "ovia_symbol.ico"),
                Path.Combine(baseDirectory, "..", "..", "..", "OVIA.Desktop", "Assets", "Icons", "ovia_symbol.ico")
            };

            foreach (string candidate in candidates)
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    cachedSymbolIcon = new Icon(fullPath);
                    return cachedSymbolIcon;
                }
            }

            return null;
        }

        private void ApplyCaptionColor(Color color)
        {
            if (disposed || form == null || !form.IsHandleCreated)
            {
                return;
            }

            try
            {
                int colorRef = ToColorRef(color);
                DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref colorRef, Marshal.SizeOf(typeof(int)));

                int borderColorRef = ToColorRef(color);
                DwmSetWindowAttribute(form.Handle, DwmwaBorderColor, ref borderColorRef, Marshal.SizeOf(typeof(int)));
            }
            catch
            {
                // Windows 버전 또는 DWM 정책에 따라 캡션 색상 변경이 지원되지 않을 수 있다.
                // 지원되지 않는 환경에서는 기본 타이틀바를 유지하고 예외를 사용자에게 노출하지 않는다.
            }
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        private void Form_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            animationTimer.Stop();
            animationTimer.Tick -= AnimationTimer_Tick;
            animationTimer.Dispose();

            if (form != null)
            {
                form.HandleCreated -= Form_HandleCreated;
                form.Activated -= Form_Activated;
                form.Deactivate -= Form_Deactivate;
                form.Disposed -= Form_Disposed;
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
    }
}
