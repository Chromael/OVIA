using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA 메인 워크스페이스 공통 입력 보조.
    /// F5는 현재 화면 상단의 새로고침 버튼과 동일한 Click 경로를 사용한다.
    /// 오른쪽 버튼을 누른 채 명확하게 좌/우로 드래그한 뒤 놓으면 이전/다음으로 이동한다.
    /// 짧은 이동과 수직 이동은 제스처로 처리하지 않아 기존 우클릭 사용을 보존한다.
    /// </summary>
    internal sealed class OviaWorkspaceInputGestureFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmContextMenu = 0x007B;
        private const int GestureThresholdPixels = 80;

        private readonly FrmMain owner;
        private bool rightButtonTracking;
        private Point rightButtonStartScreenPoint;
        private bool suppressNextContextMenu;

        public OviaWorkspaceInputGestureFilter(FrmMain owner)
        {
            this.owner = owner;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (owner == null || owner.IsDisposed || !owner.Visible)
            {
                return false;
            }

            // 로그인/설정/등록 등 별도 모달 창이 떠 있는 동안에는 공통 단축키/제스처를 가로채지 않는다.
            if (Form.ActiveForm != owner)
            {
                ResetRightGesture();
                return false;
            }

            if ((m.Msg == WmKeyDown || m.Msg == WmSysKeyDown) && (Keys)(int)m.WParam == Keys.F5)
            {
                if (owner.ExecuteWorkspaceRefreshShortcut())
                {
                    return true;
                }

                return false;
            }

            if (m.Msg == WmRButtonDown)
            {
                rightButtonTracking = true;
                rightButtonStartScreenPoint = Control.MousePosition;
                suppressNextContextMenu = false;
                return false;
            }

            if (m.Msg == WmRButtonUp && rightButtonTracking)
            {
                Point endPoint = Control.MousePosition;
                int dx = endPoint.X - rightButtonStartScreenPoint.X;
                int dy = endPoint.Y - rightButtonStartScreenPoint.Y;
                rightButtonTracking = false;

                int absX = Math.Abs(dx);
                int absY = Math.Abs(dy);

                // 80px 이상의 명확한 수평 제스처만 인정한다.
                // 수직 이동이 큰 경우 스크롤/선택 동작과 충돌하지 않도록 무시한다.
                if (absX >= GestureThresholdPixels && absX > absY)
                {
                    bool navigated = dx < 0
                        ? owner.ExecuteWorkspaceBackGesture()
                        : owner.ExecuteWorkspaceForwardGesture();

                    if (navigated)
                    {
                        suppressNextContextMenu = true;
                        return true;
                    }
                }

                return false;
            }

            // 성공한 우클릭 드래그 직후 Windows가 생성하는 컨텍스트 메뉴 메시지만 1회 차단한다.
            // 일반 우클릭은 이 플래그가 설정되지 않으므로 기존 메뉴가 그대로 열린다.
            if (m.Msg == WmContextMenu && suppressNextContextMenu)
            {
                suppressNextContextMenu = false;
                return true;
            }

            return false;
        }

        private void ResetRightGesture()
        {
            rightButtonTracking = false;
            suppressNextContextMenu = false;
        }
    }
}
