using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // OVIA 주요 업무 화면은 최소 크기 이하로 줄어들지 않도록 공통 적용합니다.
            // 로그인창/Form1, 형상 선택 팝업/FrmShapePicker 같은 보조 팝업은 제외합니다.
            Application.Idle += delegate
            {
                OviaWindowSizePolicy.ApplyToOpenForms();
            };

            Application.Run(new Form1());
        }
    }

    internal static class OviaWindowSizePolicy
    {
        private const int MinFormWidth = 1100;
        private const int MinFormHeight = 750;

        private static readonly HashSet<string> TargetFormNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FrmMain",
            "FrmProjectManager",
            "FrmProjectBarListList",
            "FrmBarList"
        };

        public static void ApplyToOpenForms()
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
            {
                Apply(form);
            }
        }

        private static void Apply(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            string formName = form.GetType().Name;

            if (!TargetFormNames.Contains(formName))
            {
                return;
            }

            Size minSize = new Size(MinFormWidth, MinFormHeight);

            if (form.MinimumSize.Width != MinFormWidth || form.MinimumSize.Height != MinFormHeight)
            {
                form.MinimumSize = minSize;
            }

            if (form.WindowState == FormWindowState.Normal &&
                (form.Width < MinFormWidth || form.Height < MinFormHeight))
            {
                form.Size = new Size(
                    Math.Max(form.Width, MinFormWidth),
                    Math.Max(form.Height, MinFormHeight)
                );
            }
        }
    }
}
