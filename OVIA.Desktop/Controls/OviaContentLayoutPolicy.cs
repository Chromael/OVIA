using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop.Controls
{
    /// <summary>
    /// OVIA 내부 콘텐츠 레이아웃 공통 정책의 호환용 클래스입니다.
    /// 현재 화면별 안정 레이아웃을 유지하기 위해 이 클래스는 빌드 오류가 없는 보조 함수만 제공합니다.
    /// </summary>
    internal static class OviaContentLayoutPolicy
    {
        public const int WorkspaceMenuBottom = 98;
        public const int FixedAreaGap = 12;
        public const int FixedAreaMaxHeight = 50;
        public const int ContentHorizontalInset = 25;
        public const int ButtonGap = 10;
        public const int RightMargin = 25;

        public static Size GetLayoutClientSize(Control owner)
        {
            if (owner == null)
            {
                return new Size(1, 1);
            }

            Form form = owner as Form;
            if (form != null && !form.TopLevel && form.Parent != null && !form.Parent.IsDisposed)
            {
                Size parentSize = form.Parent.ClientSize;
                if (parentSize.Width > 0 && parentSize.Height > 0)
                {
                    return parentSize;
                }
            }

            return owner.ClientSize;
        }
    }
}
