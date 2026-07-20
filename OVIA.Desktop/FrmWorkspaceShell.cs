using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal interface IOviaWorkspaceNavigator
    {
        string CurrentCompanyId { get; }
        string CurrentUserId { get; }

        void NavigateToMain();
        void NavigateToProjectManager();
        void NavigateToProjectRegisterWebErp();
        void NavigateToErpModulePage(string menuKey);
        void NavigateToLegacyMainDashboard();
        void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus);
        void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath);
        void NavigateToBarListMapping();
        void NavigateToRebarUnitWeightTable();
        void NavigateToSystemSettings();
        void NavigateToMenuManager();
        void NavigateToVersionInfo();
        void NavigateToNotifications();
        void NavigateToWorkspaceInfoPage(string menuKey, string pathText, string title, string selectedMenu, string helpText, string bodyText);
        bool CanNavigateBackInWorkspace { get; }
        bool CanNavigateForwardInWorkspace { get; }
        bool CanNavigateUpInWorkspace { get; }
        bool NavigateBackInWorkspace();
        bool NavigateForwardInWorkspace();
        bool NavigateUpInWorkspace();
        void ShowAutoCadEnvironmentCheck();
        void ShowAutoCadExtractGuide();
        void RequestLogout();
    }

    internal interface IOviaWorkspaceScreen
    {
        bool CanLeaveWorkspaceScreen();
        void BeforeLeaveWorkspaceScreen();
    }

    internal interface IOviaWorkspaceUnsavedState
    {
        bool HasUnsavedWorkspaceData();
        string GetUnsavedWorkspaceDataName();
    }

    internal interface IOviaWorkspaceBrowserNavigation
    {
        bool CanNavigateBackInBrowser { get; }
        bool CanNavigateForwardInBrowser { get; }
        bool NavigateBackInBrowser();
        bool NavigateForwardInBrowser();
        bool RefreshBrowser();
    }

    internal static class OviaWorkspaceExitHelper
    {
        public static bool ConfirmSystemExit(IWin32Window owner, Form currentScreen)
        {
            string dataName = GetUnsavedDataName(currentScreen);
            bool hasUnsavedData = dataName != string.Empty;
            string message;

            if (hasUnsavedData)
            {
                message = "저장되지 않은 \"" + dataName + "\" 데이터가 있습니다.\r\n\r\n그래도 프로그램을 종료하시겠습니까?";
            }
            else
            {
                message = "프로그램을 종료하시겠습니까?";
            }

            return MessageBox.Show(
                owner,
                message,
                "OVIA 프로그램 종료",
                MessageBoxButtons.YesNo,
                hasUnsavedData ? MessageBoxIcon.Warning : MessageBoxIcon.Question
            ) == DialogResult.Yes;
        }

        public static bool ConfirmLogout(IWin32Window owner, Form currentScreen)
        {
            string dataName = GetUnsavedDataName(currentScreen);
            bool hasUnsavedData = dataName != string.Empty;
            string message;

            if (hasUnsavedData)
            {
                message = "저장되지 않은 \"" + dataName + "\" 데이터가 있습니다.\r\n\r\n그래도 프로그램을 종료하시겠습니까?";
            }
            else
            {
                message = "프로그램을 종료하시겠습니까?";
            }

            return MessageBox.Show(
                owner,
                message,
                "OVIA 프로그램 종료",
                MessageBoxButtons.YesNo,
                hasUnsavedData ? MessageBoxIcon.Warning : MessageBoxIcon.Question
            ) == DialogResult.Yes;
        }

        public static bool HasUnsavedData(Form currentScreen)
        {
            return GetUnsavedDataName(currentScreen) != string.Empty;
        }

        private static string GetUnsavedDataName(Form currentScreen)
        {
            IOviaWorkspaceUnsavedState unsavedState = currentScreen as IOviaWorkspaceUnsavedState;

            if (unsavedState == null)
            {
                return string.Empty;
            }

            if (!unsavedState.HasUnsavedWorkspaceData())
            {
                return string.Empty;
            }

            string dataName = unsavedState.GetUnsavedWorkspaceDataName();
            if (string.IsNullOrWhiteSpace(dataName))
            {
                return "현재 화면";
            }

            return dataName.Trim();
        }
    }

    internal interface IOviaWorkspaceLayout
    {
        void ApplyWorkspaceLayout();
    }

    internal static class OviaWorkspaceNavigation
    {
        public static IOviaWorkspaceNavigator FindNavigator(Control control)
        {
            Control current = control;

            while (current != null)
            {
                IOviaWorkspaceNavigator navigator = current as IOviaWorkspaceNavigator;

                if (navigator != null)
                {
                    return navigator;
                }

                current = current.Parent;
            }

            Form form = control == null ? null : control.FindForm();

            while (form != null)
            {
                IOviaWorkspaceNavigator navigator = form as IOviaWorkspaceNavigator;

                if (navigator != null)
                {
                    return navigator;
                }

                form = form.Owner;
            }

            return null;
        }

        public static IOviaWorkspaceBrowserNavigation FindBrowserNavigation(Control control)
        {
            Control current = control;

            while (current != null)
            {
                IOviaWorkspaceBrowserNavigation browserNavigation = current as IOviaWorkspaceBrowserNavigation;

                if (browserNavigation != null)
                {
                    return browserNavigation;
                }

                current = current.Parent;
            }

            Form form = control == null ? null : control.FindForm();

            while (form != null)
            {
                IOviaWorkspaceBrowserNavigation browserNavigation = form as IOviaWorkspaceBrowserNavigation;

                if (browserNavigation != null)
                {
                    return browserNavigation;
                }

                form = form.Owner;
            }

            return null;
        }
    }

    internal static class OviaWorkspaceCommandBar
    {
        private static OviaAnimatedDropDownMenu currentSettingsDropDown;

        public static void Populate(Control commandBar, string selectedMenu)
        {
            Populate(commandBar, selectedMenu, string.Empty, string.Empty);
        }

        public static void Populate(Control commandBar, string selectedMenu, string currentCompanyId, string currentUserId)
        {
            if (commandBar == null)
            {
                return;
            }

            commandBar.Controls.Clear();

            int left = 30;
            const int gap = 8;

            if (CanAccessMenu("PROJECT_MANAGER", currentCompanyId, currentUserId))
            {
                OviaMenuButton project = AddMenu(commandBar, GetTopMenuText("PROJECT_MANAGER", "공사관리", true), GetMenuIconText("PROJECT_MANAGER", "\uE74C"), left, 124, selectedMenu == "PROJECT", null);
                project.Click += delegate { ToggleProjectDropDown(project); };
                left += project.Width + gap;
            }

            if (CanAccessMenu("OPERATIONS", currentCompanyId, currentUserId))
            {
                OviaMenuButton operations = AddMenu(commandBar, GetTopMenuText("OPERATIONS", "운영현황", true), GetMenuIconText("OPERATIONS", "\uE9D2"), left, 118, selectedMenu == "OPERATIONS", null);
                operations.Click += delegate { ToggleOperationsDropDown(operations); };
                left += operations.Width + gap;
            }

            if (CanAccessMenu("MATERIAL_STOCK", currentCompanyId, currentUserId))
            {
                OviaMenuButton material = AddMenu(commandBar, GetTopMenuText("MATERIAL_STOCK", "자재/재고", true), GetMenuIconText("MATERIAL_STOCK", "\uE7BC"), left, 120, selectedMenu == "MATERIAL", null);
                material.Click += delegate { ToggleMaterialDropDown(material); };
                left += material.Width + gap;
            }

            if (CanAccessMenu("SHIPPING_INVOICE", currentCompanyId, currentUserId))
            {
                OviaMenuButton shipping = AddMenu(commandBar, GetTopMenuText("SHIPPING_INVOICE", "출하/송장", true), GetMenuIconText("SHIPPING_INVOICE", "\uE7C3"), left, 120, selectedMenu == "SHIPPING", null);
                shipping.Click += delegate { ToggleShippingDropDown(shipping); };
                left += shipping.Width + gap;
            }

            if (CanAccessMenu("MASTER_DATA", currentCompanyId, currentUserId))
            {
                OviaMenuButton master = AddMenu(commandBar, GetTopMenuText("MASTER_DATA", "기준정보", true), GetMenuIconText("MASTER_DATA", "\uE8EC"), left, 112, selectedMenu == "MASTER", null);
                master.Click += delegate { ToggleMasterDataDropDown(master); };
                left += master.Width + gap;
            }

        }

        private static bool CanAccessMenu(string key, string companyId, string userId)
        {
            return OviaMenuHelpStore.CanAccess(key, companyId, userId);
        }

        private static bool CanAccessMenu(Control source, string key)
        {
            IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
            string companyId = navigator == null ? string.Empty : navigator.CurrentCompanyId;
            string userId = navigator == null ? string.Empty : navigator.CurrentUserId;
            return CanAccessMenu(key, companyId, userId);
        }

        private static void AddAllowedDropDownItem(OviaAnimatedDropDownMenu menu, Control source, string key, string text, string iconText, Action action)
        {
            if (!CanAccessMenu(source, key))
            {
                return;
            }

            string displayText = OviaMenuHelpStore.GetMenuName(key, text);
            string displayIcon = OviaMenuHelpStore.GetIconGlyph(key, iconText);
            menu.AddItem(displayText, displayIcon, action);
        }

        private static void ToggleProjectDropDown(Control menuButton)
        {
            ToggleDropDown(menuButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddAllowedDropDownItem(menu, menuButton, "PROJECT_MANAGER", "공사목록", "\uE8A5", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(menuButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToProjectManager();
                    }
                });

                AddAllowedDropDownItem(menu, menuButton, "PROJECT_REGISTER", "공사등록", "\uE710", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(menuButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToProjectRegisterWebErp();
                    }
                });
            });
        }

        private static void ToggleOperationsDropDown(Control menuButton)
        {
            ToggleDropDown(menuButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddWorkspacePageItem(menu, menuButton, "전체 BarList", "\uE8A5", "OPERATIONS_ALL_BARLIST", "메인  ›  운영현황  ›  전체 BarList", "전체 BarList", "OPERATIONS", "모든 공사의 BarList를 통합 조회하고 검색, 필터, Excel 저장, 출력, 공사/BarList 이동을 처리합니다.", "전체 BarList는 SSBAR 첫 화면의 전체 조회 성격을 흡수한 운영현황 화면입니다. 등록과 수정은 해당 공사/BarList 화면에서 처리합니다.");
                AddWorkspacePageItem(menu, menuButton, "전체 생산오더", "\uE8F1", "OPERATIONS_ALL_ORDER", "메인  ›  운영현황  ›  전체 생산오더", "전체 생산오더", "OPERATIONS", "전체 공사의 생산오더와 작업지시 상태를 조회합니다.", "생산오더 등록과 분할은 공사 상세 내부에서 처리하고, 이 화면은 전체 조회와 이동 중심으로 운영합니다.");
                AddWorkspacePageItem(menu, menuButton, "입출고 현황", "\uE8CB", "OPERATIONS_INOUT", "메인  ›  운영현황  ›  입출고 현황", "입출고 현황", "OPERATIONS", "전체 입고와 출고 흐름을 기간, 공사, 거래처 기준으로 조회합니다.", "상세 편집은 자재/재고 화면에서 처리하고, 운영현황은 검색과 Excel/출력, 원 화면 이동을 담당합니다.");
                AddWorkspacePageItem(menu, menuButton, "재고 현황", "\uE8D5", "OPERATIONS_STOCK", "메인  ›  운영현황  ›  재고 현황", "재고 현황", "OPERATIONS", "규격별, 길이별, 공사별 재고 상태를 통합 조회합니다.", "ERP 재고 기준과 동기화될 예정이며, OVIA Desktop에서는 현장 조회와 출력 중심으로 사용합니다.");
                AddWorkspacePageItem(menu, menuButton, "송장/납품 현황", "\uE7C3", "OPERATIONS_INVOICE", "메인  ›  운영현황  ›  송장/납품 현황", "송장/납품 현황", "OPERATIONS", "전체 송장, 납품표, 미송장, 출하 상태를 통합 조회합니다.", "송장 발행과 수정은 출하/송장 메뉴의 송장관리에서 처리하고, 운영현황은 조회와 이동 중심으로 유지합니다.");
                AddWorkspacePageItem(menu, menuButton, "태그/QR 현황", "\uE8B3", "OPERATIONS_TAG_QR", "메인  ›  운영현황  ›  태그/QR 현황", "태그/QR 현황", "OPERATIONS", "태그 발행, QR 생성, 미발행, 재발행 상태를 조회합니다.", "최초 태그/QR 발행은 각 업무 화면 내부 버튼으로 처리하고, 이 화면은 상태 조회와 재출력 흐름으로 연결합니다.");
                AddWorkspacePageItem(menu, menuButton, "미처리 작업", "\uE7BA", "OPERATIONS_PENDING", "메인  ›  운영현황  ›  미처리 작업", "미처리 작업", "OPERATIONS", "미출력, 미송장, 미태그, 오류 데이터를 한 곳에 모아 확인합니다.", "업무 누락을 방지하기 위한 모니터링 화면입니다.");
                AddWorkspacePageItem(menu, menuButton, "출력센터", "\uE749", "OPERATIONS_PRINT_CENTER", "메인  ›  운영현황  ›  출력센터", "출력센터", "OPERATIONS", "재출력, 출력 이력, 프린터 오류, 태그/송장 재발행을 관리합니다.", "최초 출력은 각 업무 화면에서 처리하고, 출력센터는 재출력과 이력 확인을 담당합니다.");
            });
        }

        private static void ToggleMaterialDropDown(Control menuButton)
        {
            ToggleDropDown(menuButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddWorkspacePageItem(menu, menuButton, "입고관리", "\uE8CB", "MATERIAL_INBOUND", "메인  ›  자재/재고  ›  입고관리", "입고관리", "MATERIAL", "입고 자료 등록, 수정, 삭제, Excel 가져오기, 출력을 처리합니다.", "입고 등록은 드롭다운 메뉴가 아니라 입고관리 화면 내부 버튼으로 처리합니다.");
                AddWorkspacePageItem(menu, menuButton, "재고현황", "\uE8D5", "MATERIAL_STOCK_STATUS", "메인  ›  자재/재고  ›  재고현황", "재고현황", "MATERIAL", "규격별, 길이별, 공사별 재고를 조회하고 Excel 저장과 출력을 처리합니다.", "전체 재고 기준과 승인 흐름은 ERP와 연동될 예정입니다.");
                AddWorkspacePageItem(menu, menuButton, "재고조정", "\uE70F", "MATERIAL_STOCK_ADJUST", "메인  ›  자재/재고  ›  재고조정", "재고조정", "MATERIAL", "재고 추가, 차감, 보정과 조정 사유 입력을 관리합니다.", "재고조정은 권한이 필요한 업무이며 추후 ERP 승인 흐름과 연결합니다.");
                AddWorkspacePageItem(menu, menuButton, "출고사용내역", "\uE7C3", "MATERIAL_OUTBOUND_USAGE", "메인  ›  자재/재고  ›  출고사용내역", "출고사용내역", "MATERIAL", "출고 사용 상세 이력과 공사/자재별 사용 내역을 조회합니다.", "송장과 출하 데이터와 연결되는 이력 조회 화면입니다.");
            });
        }

        private static void ToggleShippingDropDown(Control menuButton)
        {
            ToggleDropDown(menuButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddWorkspacePageItem(menu, menuButton, "송장관리", "\uE7C3", "SHIPPING_INVOICE_MANAGE", "메인  ›  출하/송장  ›  송장관리", "송장관리", "SHIPPING", "송장 조회, 발행, 수정, 납품표, 인수증, 검수양식 출력과 차량/운전자 선택을 처리합니다.", "송장 발행과 수정, 출력은 송장관리 화면 내부 버튼으로 통합합니다.");
                AddWorkspacePageItem(menu, menuButton, "출하실적등록", "\uE9D9", "SHIPPING_RESULT_REGISTER", "메인  ›  출하/송장  ›  출하실적등록", "출하실적등록", "SHIPPING", "출하 실적 조회, 실적 등록, 거래처별 실적 양식 생성, ERP 전송을 처리합니다.", "대한제강, 동인철강, 스틸코리아 같은 업체별 양식은 화면 내부 템플릿 선택 방식으로 통합합니다.");
            });
        }

        private static void ToggleMasterDataDropDown(Control menuButton)
        {
            ToggleDropDown(menuButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddWorkspacePageItem(menu, menuButton, "거래처 관리", "\uE77B", "MASTER_COMPANY", "메인  ›  기준정보  ›  거래처 관리", "거래처 관리", "MASTER", "거래처, 가공사, 납품처 기준 데이터를 관리합니다.", "기준정보는 업무 마스터 데이터만 관리하고 ERP와 동기화될 예정입니다.");
                AddWorkspacePageItem(menu, menuButton, "철근메이커 관리", "\uE8EC", "MASTER_REBAR_MAKER", "메인  ›  기준정보  ›  철근메이커 관리", "철근메이커 관리", "MASTER", "철근메이커와 브랜드 기준 데이터를 관리합니다.", "입고, BarList, 송장 데이터와 연결됩니다.");
                AddWorkspacePageItem(menu, menuButton, "자재/규격 관리", "\uE8D5", "MASTER_MATERIAL_SPEC", "메인  ›  기준정보  ›  자재/규격 관리", "자재/규격 관리", "MASTER", "자재 코드와 철근 규격 기준 데이터를 관리합니다.", "단위중량표는 시스템관리의 별도 핵심 계산 기준으로 유지합니다.");
                AddWorkspacePageItem(menu, menuButton, "형상코드 관리", "\uE8A5", "MASTER_SHAPE_CODE", "메인  ›  기준정보  ›  형상코드 관리", "형상코드 관리", "MASTER", "형상 코드, 사용자 형상, 미리보기 기준 데이터를 관리합니다.", "OVIA BarList 형상 렌더링과 연결되는 기준 데이터입니다.");
                AddWorkspacePageItem(menu, menuButton, "차량/운전자 관리", "\uE804", "MASTER_CAR_DRIVER", "메인  ›  기준정보  ›  차량/운전자 관리", "차량/운전자 관리", "MASTER", "차량번호, 기사, 운전자 정보를 관리합니다.", "송장 발행과 출하 실적등록에 연결됩니다.");
                AddWorkspacePageItem(menu, menuButton, "작업자/작업반 관리", "\uE716", "MASTER_WORKER_TEAM", "메인  ›  기준정보  ›  작업자/작업반 관리", "작업자/작업반 관리", "MASTER", "작업자와 작업반 기준 정보를 관리합니다.", "생산오더와 작업지시 흐름에 연결됩니다.");
                AddWorkspacePageItem(menu, menuButton, "기계/위치 관리", "\uE950", "MASTER_MACHINE_LOCATION", "메인  ›  기준정보  ›  기계/위치 관리", "기계/위치 관리", "MASTER", "기계, 설비, 위치, 창고 기준 데이터를 관리합니다.", "생산, 입고, 재고 흐름과 연결될 예정입니다.");
            });
        }

        public static void CloseOpenDropDown()
        {
            if (currentSettingsDropDown != null && !currentSettingsDropDown.IsDisposed && currentSettingsDropDown.Visible)
            {
                currentSettingsDropDown.CloseAnimated();
                currentSettingsDropDown = null;
            }
        }

        public static bool CanAccessSettingsMenu(Control source)
        {
            return CanAccessMenu(source, "SETTINGS");
        }

        public static string GetSettingsMenuIcon()
        {
            return GetMenuIconText("SETTINGS", "\uE713");
        }

        public static void ToggleSettingsMenu(Control source)
        {
            if (source == null || source.IsDisposed || !CanAccessSettingsMenu(source))
            {
                return;
            }

            ToggleSettingsDropDown(source);
        }

        public static bool CanAccessErpMenu(Control source)
        {
            return CanAccessMenu(source, "ERP");
        }

        public static string GetErpMenuIcon()
        {
            return GetMenuIconText("ERP", "\uE774");
        }

        public static void OpenErpShortcut(Control source)
        {
            if (source == null || source.IsDisposed || !CanAccessErpMenu(source))
            {
                return;
            }

            CloseOpenDropDown();
            OpenErpInDefaultBrowser(source);
        }

        private static void ToggleDropDown(Control menuButton, Action<OviaAnimatedDropDownMenu> buildItems)
        {
            if (menuButton == null || menuButton.IsDisposed)
            {
                return;
            }

            if (currentSettingsDropDown != null && !currentSettingsDropDown.IsDisposed && currentSettingsDropDown.Visible)
            {
                currentSettingsDropDown.CloseAnimated();
                currentSettingsDropDown = null;
                return;
            }

            OviaAnimatedDropDownMenu menu = new OviaAnimatedDropDownMenu();
            currentSettingsDropDown = menu;

            if (buildItems != null)
            {
                buildItems(menu);
            }

            menu.Closed += delegate
            {
                if (currentSettingsDropDown == menu)
                {
                    currentSettingsDropDown = null;
                }
            };

            menu.ShowBelow(menuButton);
        }

        private static void AddWorkspacePageItem(OviaAnimatedDropDownMenu menu, Control source, string text, string iconText, string key, string pathText, string title, string selectedMenu, string helpText, string bodyText)
        {
            if (!CanAccessMenu(source, key))
            {
                return;
            }

            string displayText = OviaMenuHelpStore.GetMenuName(key, text);
            string displayTitle = OviaMenuHelpStore.GetMenuName(key, title);
            string displayPathText = ReplacePathLeaf(pathText, displayTitle);
            string displayIcon = OviaMenuHelpStore.GetIconGlyph(key, iconText);

            menu.AddItem(displayText, displayIcon, delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                menu.CloseImmediate();
                currentSettingsDropDown = null;

                if (navigator != null)
                {
                    if (OviaMenuHelpStore.IsErpLoadEnabled(key))
                    {
                        navigator.NavigateToErpModulePage(key);
                    }
                    else
                    {
                        navigator.NavigateToWorkspaceInfoPage(key, displayPathText, displayTitle, selectedMenu, helpText, bodyText);
                    }
                }
            });
        }

        private static void ToggleSettingsDropDown(Control settingsButton)
        {
            ToggleDropDown(settingsButton, delegate(OviaAnimatedDropDownMenu menu)
            {
                AddAllowedDropDownItem(menu, settingsButton, "LEGACY_MAIN_DASHBOARD", "기존 메인대시보드", "\uE9D2", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToLegacyMainDashboard();
                    }
                });

                AddAllowedDropDownItem(menu, settingsButton, "SYSTEM_SETTINGS", "시스템 설정", "\uE713", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToSystemSettings();
                    }
                });

                AddAllowedDropDownItem(menu, settingsButton, "BARLIST_MAPPING", "BarList 항목 매핑", "\uE8A5", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToBarListMapping();
                    }
                });

                AddAllowedDropDownItem(menu, settingsButton, "REBAR_UNIT_WEIGHT", "이형철근 단위중량표", "\uE9D9", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToRebarUnitWeightTable();
                    }
                });

                AddWorkspacePageItem(menu, settingsButton, "가져오기 양식 설정", "\uE8B7", "IMPORT_TEMPLATE", OviaMenuHelpStore.GetWorkspacePath("IMPORT_TEMPLATE", "메인  ›  환경설정  ›  가져오기 양식 설정"), "가져오기 양식 설정", "SETTINGS", "SSBAR, Tekla, Excel, DBF, BAR 등 외부 데이터 가져오기 템플릿을 관리합니다.", "외부 파일 가져오기 방식은 시스템관리에서 템플릿으로 통합 관리합니다.");
                AddWorkspacePageItem(menu, settingsButton, "출력 양식 설정", "\uE749", "PRINT_TEMPLATE", OviaMenuHelpStore.GetWorkspacePath("PRINT_TEMPLATE", "메인  ›  환경설정  ›  출력 양식 설정"), "출력 양식 설정", "SETTINGS", "송장, 납품표, 인수증, 검수양식, BarList 출력 템플릿을 관리합니다.", "SSBAR의 업체별 출력 메뉴는 OVIA에서 출력 양식 설정과 템플릿 선택 방식으로 통합합니다.");
                AddWorkspacePageItem(menu, settingsButton, "QR/바코드 양식 설정", "\uE8B3", "QR_BARCODE_TEMPLATE", OviaMenuHelpStore.GetWorkspacePath("QR_BARCODE_TEMPLATE", "메인  ›  환경설정  ›  QR/바코드 양식 설정"), "QR/바코드 양식 설정", "SETTINGS", "QR 데이터 구조, 바코드 종류, 태그 양식 연결 기준을 관리합니다.", "QR/바코드 양식은 기준정보가 아니라 시스템 출력 동작 설정으로 관리합니다.");
                AddWorkspacePageItem(menu, settingsButton, "프린터 설정", "\uE749", "PRINTER_SETTINGS", OviaMenuHelpStore.GetWorkspacePath("PRINTER_SETTINGS", "메인  ›  환경설정  ›  프린터 설정"), "프린터 설정", "SETTINGS", "라벨 프린터, 송장 프린터, 일반 프린터, 용지, 여백, 테스트 출력을 관리합니다.", "프린터 설정은 사용자 PC별 로컬 환경과 연결되는 OVIA Desktop 핵심 설정입니다.");
                AddWorkspacePageItem(menu, settingsButton, "백업/복원", "\uE74E", "BACKUP_RESTORE", OviaMenuHelpStore.GetWorkspacePath("BACKUP_RESTORE", "메인  ›  환경설정  ›  백업/복원"), "백업/복원", "SETTINGS", "로컬 데이터, 설정, 공사 데이터를 백업하거나 복원합니다.", "백업/복원은 운영 안전장치로 후속 개발에서 실제 ZIP 백업 생성과 복원 기능을 연결합니다.");

                AddAllowedDropDownItem(menu, settingsButton, "MENU_MANAGER", "메뉴관리", "\uE8A4", delegate
                {
                    IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;

                    if (navigator != null)
                    {
                        navigator.NavigateToMenuManager();
                    }
                });

                AddAllowedDropDownItem(menu, settingsButton, "VERSION_INFO", "버전정보", "\uE946", delegate
                {
                    menu.CloseImmediate();
                    currentSettingsDropDown = null;
                    ShowVersionInfo(settingsButton);
                });
            });
        }

        private static void OpenErpInDefaultBrowser(Control source)
        {
            string erpUrl = OviaSystemSettingsStore.GetErpConnectionUrl();

            if (erpUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소가 아직 설정되지 않았습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 연결 주소를 먼저 저장해주세요.",
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string browserUrl = NormalizeErpBrowserUrl(erpUrl);

            if (browserUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소 형식이 올바르지 않습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 연결 URL을 다시 확인해주세요.\r\n\r\n입력값: " + erpUrl,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = browserUrl;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP 연결 주소를 기본 웹 브라우저로 여는 중 오류가 발생했습니다.\r\n\r\n주소: " + browserUrl + "\r\n\r\n" + ex.Message,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private static string NormalizeErpBrowserUrl(string value)
        {
            string url = value == null ? "" : value.Trim();

            if (url == "")
            {
                return "";
            }

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return uri.AbsoluteUri;
                }

                return "";
            }

            string lower = url.ToLowerInvariant();
            string prefix = "https://";

            if (lower.StartsWith("localhost") ||
                lower.StartsWith("127.") ||
                lower.StartsWith("10.") ||
                lower.StartsWith("192.168.") ||
                lower.Contains(":"))
            {
                prefix = "http://";
            }

            string candidate = prefix + url;

            if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) &&
                (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return uri.AbsoluteUri;
            }

            return "";
        }

        private static void ShowBackupGuide(Control source)
        {
            string oviaFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA"
            );

            MessageBox.Show(
                "백업하기 메뉴가 준비되었습니다.\r\n\r\n" +
                "현재 백업 대상 기본 폴더:\r\n" + oviaFolder + "\r\n\r\n" +
                "다음 단계에서 이 메뉴를 실제 ZIP 백업 생성 기능으로 연결하겠습니다.",
                "OVIA 백업하기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private static void ShowVersionInfo(Control source)
        {
            IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
            if (navigator != null)
            {
                navigator.NavigateToVersionInfo();
                return;
            }

            Form owner = source == null ? null : source.FindForm();
            using (FrmVersionInfo form = new FrmVersionInfo(string.Empty, string.Empty))
            {
                if (owner == null)
                {
                    form.ShowDialog();
                }
                else
                {
                    form.ShowDialog(owner);
                }
            }
        }

        private static string GetTopMenuText(string key, string fallbackText, bool hasDropDown)
        {
            string text = OviaMenuHelpStore.GetMenuName(key, fallbackText);
            return hasDropDown ? text + " \uE70D" : text;
        }

        private static string GetMenuIconText(string key, string fallbackIcon)
        {
            return OviaMenuHelpStore.GetIconGlyph(key, fallbackIcon);
        }

        private static string ReplacePathLeaf(string pathText, string leaf)
        {
            string path = pathText == null ? string.Empty : pathText;
            string value = leaf == null ? string.Empty : leaf.Trim();
            if (path == string.Empty || value == string.Empty)
            {
                return path;
            }

            string delimiter = "›";
            int index = path.LastIndexOf(delimiter, StringComparison.Ordinal);
            if (index < 0)
            {
                return value;
            }

            return path.Substring(0, index + delimiter.Length) + "  " + value;
        }

        private static OviaMenuButton AddMenu(Control parent, string text, string iconText, int left, int width, bool selected, Action<Control> action)
        {
            OviaMenuButton menu = new OviaMenuButton();
            menu.Text = text;
            menu.IconText = iconText;
            menu.Location = new Point(left, 6);
            menu.Size = new Size(width, 38);
            menu.Selected = selected;
            menu.Click += delegate
            {
                if (action != null)
                {
                    action(menu);
                }
            };
            parent.Controls.Add(menu);
            return menu;
        }
    }

    internal class OviaAnimatedDropDownMenu : Panel, IMessageFilter
    {
        private readonly Timer animationTimer;
        private readonly int itemHeight = 38;
        private readonly int verticalPadding = 8;
        private readonly int menuWidth = 226;
        private int targetHeight;
        private bool opening;
        private Control anchorControl;
        private bool filterAttached;

        public event EventHandler Closed;

        public OviaAnimatedDropDownMenu()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Visible = false;
            this.Size = new Size(menuWidth, 0);
            this.Padding = new Padding(6, verticalPadding, 6, verticalPadding);

            animationTimer = new Timer();
            animationTimer.Interval = 12;
            animationTimer.Tick += AnimationTimer_Tick;
        }

        public void AddItem(string text, string iconText, Action action)
        {
            OviaDropDownMenuItem item = new OviaDropDownMenuItem();
            item.Text = text;
            item.IconText = iconText;
            item.Action = action;
            item.Size = new Size(menuWidth - 12, itemHeight);
            item.Location = new Point(6, verticalPadding + this.Controls.Count * itemHeight);
            this.Controls.Add(item);

            targetHeight = verticalPadding * 2 + this.Controls.Count * itemHeight;
        }

        public void ShowBelow(Control anchor)
        {
            if (anchor == null || anchor.FindForm() == null)
            {
                return;
            }

            anchorControl = anchor;
            Form form = anchor.FindForm();

            if (this.Parent != form)
            {
                if (this.Parent != null)
                {
                    this.Parent.Controls.Remove(this);
                }

                form.Controls.Add(this);
            }

            Point screenPoint = anchor.PointToScreen(new Point(0, anchor.Height + 4));
            Point formPoint = form.PointToClient(screenPoint);
            int left = formPoint.X;

            if (left + menuWidth > form.ClientSize.Width - 12)
            {
                left = Math.Max(12, form.ClientSize.Width - menuWidth - 12);
            }

            this.Location = new Point(left, formPoint.Y);
            this.Width = menuWidth;
            this.Height = 0;
            this.Visible = true;
            this.BringToFront();

            ApplyRoundedRegion();
            AttachFilter();

            opening = true;
            animationTimer.Start();
        }

        public void CloseAnimated()
        {
            if (this.IsDisposed)
            {
                return;
            }

            opening = false;
            animationTimer.Start();
        }

        public void CloseImmediate()
        {
            animationTimer.Stop();
            DetachFilter();
            this.Visible = false;
            OnClosed();
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            int step = 22;

            if (opening)
            {
                this.Height = Math.Min(targetHeight, this.Height + step);
                ApplyRoundedRegion();

                if (this.Height >= targetHeight)
                {
                    animationTimer.Stop();
                    this.Height = targetHeight;
                    ApplyRoundedRegion();
                }
            }
            else
            {
                this.Height = Math.Max(0, this.Height - step);
                ApplyRoundedRegion();

                if (this.Height <= 0)
                {
                    animationTimer.Stop();
                    DetachFilter();
                    this.Visible = false;
                    OnClosed();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 7))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen border = new Pen(Color.FromArgb(218, 223, 230), 1))
                {
                    e.Graphics.DrawPath(border, path);
                }
            }

            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            if (this.Width <= 0 || this.Height <= 0)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 7))
            {
                this.Region = new Region(path);
            }
        }

        private void AttachFilter()
        {
            if (!filterAttached)
            {
                Application.AddMessageFilter(this);
                filterAttached = true;
            }
        }

        private void DetachFilter()
        {
            if (filterAttached)
            {
                Application.RemoveMessageFilter(this);
                filterAttached = false;
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WmLButtonDown = 0x0201;
            const int WmRButtonDown = 0x0204;
            const int WmMButtonDown = 0x0207;

            if (m.Msg != WmLButtonDown && m.Msg != WmRButtonDown && m.Msg != WmMButtonDown)
            {
                return false;
            }

            if (!this.Visible)
            {
                return false;
            }

            Point mouse = Control.MousePosition;
            Rectangle menuRect = this.RectangleToScreen(this.ClientRectangle);
            Rectangle anchorRect = anchorControl == null
                ? Rectangle.Empty
                : anchorControl.RectangleToScreen(anchorControl.ClientRectangle);

            if (!menuRect.Contains(mouse) && !anchorRect.Contains(mouse))
            {
                CloseAnimated();
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachFilter();

                if (animationTimer != null)
                {
                    animationTimer.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void OnClosed()
        {
            EventHandler handler = Closed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    internal class OviaDropDownMenuItem : Control
    {
        public string IconText = "";
        public Action Action;
        private bool hover;

        public OviaDropDownMenuItem()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Action != null)
            {
                Action();
            }

            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle hoverRect = new Rectangle(4, 3, this.Width - 8, this.Height - 6);

            if (hover)
            {
                using (GraphicsPath path = MainDrawHelper.RoundRect(hoverRect, 5))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(243, 244, 246)))
                {
                    e.Graphics.FillPath(fill, path);
                }
            }

            Color iconColor = Color.FromArgb(96, 104, 116);
            Color textColor = OviaFluentTheme.TextPrimary;

            using (Font iconFont = new Font("Segoe MDL2 Assets", 12.5F, FontStyle.Regular))
            using (Font textFont = OviaFluentTheme.FontButton(9.2F, FontStyle.Regular))
            {
                Rectangle iconRect = new Rectangle(15, 0, 22, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    IconText,
                    iconFont,
                    iconRect,
                    iconColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                );

                Rectangle textRect = new Rectangle(47, 0, this.Width - 56, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this.Text,
                    textFont,
                    textRect,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                );
            }

            base.OnPaint(e);
        }
    }

    internal class OviaExplorerDropDownRenderer : ToolStripProfessionalRenderer
    {
        public OviaExplorerDropDownRenderer()
            : base(new OviaExplorerDropDownColorTable())
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                Rectangle rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }

    internal class OviaExplorerDropDownColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return OviaFluentTheme.NavigationHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return OviaFluentTheme.NavigationHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return OviaFluentTheme.NavigationHover; } }
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
    }

    internal class OviaPathEditExitFilter : IMessageFilter
    {
        private const int WmLButtonDown = 0x0201;
        private static OviaPathEditExitFilter current;

        private readonly LinkLabel breadcrumb;
        private readonly TextBox textBox;

        private OviaPathEditExitFilter(LinkLabel breadcrumb, TextBox textBox)
        {
            this.breadcrumb = breadcrumb;
            this.textBox = textBox;
        }

        public static void Attach(LinkLabel breadcrumb, TextBox textBox)
        {
            Detach();

            if (breadcrumb == null || textBox == null)
            {
                return;
            }

            current = new OviaPathEditExitFilter(breadcrumb, textBox);
            Application.AddMessageFilter(current);
        }

        public static void Detach()
        {
            if (current != null)
            {
                Application.RemoveMessageFilter(current);
                current = null;
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmLButtonDown)
            {
                return false;
            }

            if (textBox == null || textBox.IsDisposed || !textBox.Visible)
            {
                Detach();
                return false;
            }

            Rectangle textBounds = textBox.RectangleToScreen(textBox.ClientRectangle);

            if (textBounds.Contains(Control.MousePosition))
            {
                return false;
            }

            textBox.Visible = false;

            if (breadcrumb != null && !breadcrumb.IsDisposed)
            {
                breadcrumb.Visible = true;
                breadcrumb.BringToFront();
            }

            Detach();
            return false;
        }
    }

    public class FrmWorkspaceShell : Form, IOviaWorkspaceNavigator
    {
        private readonly string companyId;
        private readonly string userId;
        private Panel hostPanel;
        private Form currentScreen;
        private OviaWindowCaptionTheme captionTheme;
        private bool systemExitConfirmed;
        private bool navigateToMainClose;
        private readonly Stack<OviaWorkspaceNavigationEntry> backHistory = new Stack<OviaWorkspaceNavigationEntry>();
        private readonly Stack<OviaWorkspaceNavigationEntry> forwardHistory = new Stack<OviaWorkspaceNavigationEntry>();
        private OviaWorkspaceNavigationEntry currentNavigationEntry;
        private bool suppressNavigationHistory;

        private sealed class OviaWorkspaceNavigationEntry
        {
            public OviaWorkspaceNavigationEntry(string kind, params string[] values)
            {
                Kind = kind == null ? string.Empty : kind.Trim().ToUpperInvariant();
                Values = values == null ? new string[0] : values;
            }

            public string Kind { get; private set; }
            public string[] Values { get; private set; }

            public string Get(int index)
            {
                if (Values == null || index < 0 || index >= Values.Length || Values[index] == null)
                {
                    return string.Empty;
                }

                return Values[index];
            }
        }

        public string CurrentCompanyId
        {
            get { return companyId; }
        }

        public string CurrentUserId
        {
            get { return userId; }
        }

        public FrmWorkspaceShell(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;

            BuildUI();
            NavigateToProjectManager();
        }

        private void BuildUI()
        {
            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA 공사관리";
            this.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1240, 760);
            this.MinimumSize = new Size(1100, 750);
            this.BackColor = OviaFluentTheme.AppBackground;
            this.FormClosing += FrmWorkspaceShell_FormClosing;
            captionTheme = OviaWindowCaptionTheme.Attach(this);

            hostPanel = new Panel();
            hostPanel.Dock = DockStyle.Fill;
            hostPanel.BackColor = OviaFluentTheme.AppBackground;
            hostPanel.Resize += HostPanel_Resize;
            this.Controls.Add(hostPanel);
        }

        private void HostPanel_Resize(object sender, EventArgs e)
        {
            FillCurrentWorkspaceScreen();
        }

        private void FillCurrentWorkspaceScreen()
        {
            if (currentScreen == null || currentScreen.IsDisposed || hostPanel == null)
            {
                return;
            }

            Rectangle bounds = hostPanel.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            currentScreen.Dock = DockStyle.Fill;
            currentScreen.Location = Point.Empty;
            currentScreen.Size = bounds.Size;
            ApplyWorkspaceLayout(currentScreen);
        }

        private bool CanNavigateToMenu(string menuKey, string menuName)
        {
            if (OviaMenuHelpStore.CanAccess(menuKey, companyId, userId))
            {
                return true;
            }

            MessageBox.Show(
                menuName + " 메뉴에 접근할 수 없습니다.\r\n\r\n메뉴관리의 사용 여부와 사용자 권한 레벨을 확인해주세요.",
                "OVIA 권한 확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return false;
        }

        public void NavigateToMain()
        {
            navigateToMainClose = true;
            this.Close();
        }

        public bool CanNavigateBackInWorkspace
        {
            get { return backHistory.Count > 0; }
        }

        public bool CanNavigateForwardInWorkspace
        {
            get { return forwardHistory.Count > 0; }
        }

        public bool CanNavigateUpInWorkspace
        {
            get { return ResolveParentNavigationEntry(currentNavigationEntry) != null; }
        }

        public bool NavigateBackInWorkspace()
        {
            if (backHistory.Count == 0)
            {
                NavigateToMain();
                return true;
            }

            OviaWorkspaceNavigationEntry entry = backHistory.Pop();
            OviaWorkspaceNavigationEntry forwardEntry = currentNavigationEntry;
            bool oldSuppress = suppressNavigationHistory;
            suppressNavigationHistory = true;

            try
            {
                bool navigated = NavigateToStoredEntry(entry);
                if (navigated && forwardEntry != null)
                {
                    forwardHistory.Push(forwardEntry);
                }

                return navigated;
            }
            finally
            {
                suppressNavigationHistory = oldSuppress;
            }
        }

        public bool NavigateForwardInWorkspace()
        {
            if (forwardHistory.Count == 0)
            {
                return false;
            }

            OviaWorkspaceNavigationEntry entry = forwardHistory.Pop();
            OviaWorkspaceNavigationEntry backEntry = currentNavigationEntry;
            bool oldSuppress = suppressNavigationHistory;
            suppressNavigationHistory = true;

            try
            {
                bool navigated = NavigateToStoredEntry(entry);
                if (navigated && backEntry != null)
                {
                    backHistory.Push(backEntry);
                }

                return navigated;
            }
            finally
            {
                suppressNavigationHistory = oldSuppress;
            }
        }

        public bool NavigateUpInWorkspace()
        {
            OviaWorkspaceNavigationEntry parentEntry = ResolveParentNavigationEntry(currentNavigationEntry);

            if (parentEntry == null)
            {
                NavigateToMain();
                return true;
            }

            return NavigateToStoredEntry(parentEntry);
        }

        public void NavigateToProjectManager()
        {
            ShowScreenWithHistory(
                new FrmProjectManager(companyId, userId),
                "OVIA 공사목록",
                new OviaWorkspaceNavigationEntry("PROJECT_MANAGER")
            );
        }

        public void NavigateToProjectRegisterWebErp()
        {
            ShowScreenWithHistory(
                new FrmOviaWebErpPage(
                    companyId,
                    userId,
                    "PROJECT_REGISTER",
                    "공사등록",
                    "메인  ›  공사관리  ›  공사등록",
                    "PROJECT",
                    "projects/register",
                    "ERP 공사등록 페이지를 WebView2로 불러옵니다. Web ERP에서 공사를 등록하면 공사목록에 표시되는 구조로 전환합니다."
                ),
                "OVIA 공사등록",
                new OviaWorkspaceNavigationEntry("PROJECT_REGISTER")
            );
        }


        private static void OpenErpInDefaultBrowser(Control source)
        {
            string erpUrl = OviaSystemSettingsStore.GetErpConnectionUrl();

            if (erpUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소가 아직 설정되지 않았습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 연결 주소를 먼저 저장해주세요.",
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string browserUrl = NormalizeErpBrowserUrl(erpUrl);

            if (browserUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소 형식이 올바르지 않습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 연결 URL을 다시 확인해주세요.\r\n\r\n입력값: " + erpUrl,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = browserUrl;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP 연결 주소를 기본 웹 브라우저로 여는 중 오류가 발생했습니다.\r\n\r\n주소: " + browserUrl + "\r\n\r\n" + ex.Message,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private static string NormalizeErpBrowserUrl(string value)
        {
            string url = value == null ? "" : value.Trim();

            if (url == "")
            {
                return string.Empty;
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return "https://" + url.TrimStart('/');
        }

        public void NavigateToErpModulePage(string menuKey)
        {
            string key = string.IsNullOrWhiteSpace(menuKey) ? "ERP" : menuKey.Trim();
            if (OviaMenuHelpStore.IsBrowserOnlyErpShortcut(key))
            {
                OpenErpInDefaultBrowser(this);
                return;
            }

            string title = OviaMenuHelpStore.GetMenuName(key, key == "ERP" ? "ERP" : "ERP 모듈");
            string selected = OviaMenuHelpStore.GetSelectedMenuKey(key);
            string path = key == "ERP"
                ? OviaMenuHelpStore.GetWorkspacePath("ERP", "메인  ›  ERP")
                : OviaMenuHelpStore.GetWorkspacePath(key, "메인  ›  " + title);

            ShowScreenWithHistory(
                new FrmOviaWebErpPage(
                    companyId,
                    userId,
                    key,
                    title,
                    path,
                    selected,
                    OviaMenuHelpStore.GetErpModuleName(key),
                    title + " ERP 모듈 페이지를 WebView2로 불러옵니다."
                ),
                "OVIA " + title,
                new OviaWorkspaceNavigationEntry("ERP_MODULE_PAGE", key)
            );
        }

        public void NavigateToLegacyMainDashboard()
        {
            if (!CanNavigateToMenu("LEGACY_MAIN_DASHBOARD", "기존 메인대시보드"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmOviaMenuPage(
                    companyId,
                    userId,
                    "LEGACY_MAIN_DASHBOARD",
                    "기존 메인대시보드",
                    OviaMenuHelpStore.GetWorkspacePath("LEGACY_MAIN_DASHBOARD", "메인  ›  환경설정  ›  기존 메인대시보드"),
                    "SETTINGS",
                    "WebView2 전환 전 메인에서 사용하던 대시보드 화면입니다.",
                    "기존 메인대시보드는 메인 창의 시스템관리 메뉴에서 확인하는 것을 기준으로 합니다. WebView2 전환 완료 후 정리 여부를 결정합니다."
                ),
                "OVIA 기존 메인대시보드",
                new OviaWorkspaceNavigationEntry("LEGACY_MAIN_DASHBOARD")
            );
        }

        public void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus)
        {
            ShowScreenWithHistory(
                new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus),
                "OVIA 공사별 BarList",
                new OviaWorkspaceNavigationEntry("PROJECT_BARLIST_LIST", projectNo, projectName, clientName, projectStatus)
            );
        }

        public void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath)
        {
            string filePath = initialFilePath == null ? "" : initialFilePath;
            ShowScreenWithHistory(
                new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath),
                filePath.Trim() == "" ? "OVIA 신규 BarList 등록" : "OVIA BarList",
                new OviaWorkspaceNavigationEntry("BARLIST", projectNo, projectName, clientName, projectStatus, filePath)
            );
        }

        public void NavigateToBarListMapping()
        {
            if (!CanNavigateToMenu("BARLIST_MAPPING", "BarList 항목 매핑"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmBarListMappingManager(companyId, userId),
                "OVIA BarList 항목 매핑",
                new OviaWorkspaceNavigationEntry("BARLIST_MAPPING")
            );
        }

        public void NavigateToRebarUnitWeightTable()
        {
            if (!CanNavigateToMenu("REBAR_UNIT_WEIGHT", "이형철근 단위중량표"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmRebarUnitWeightTable(companyId, userId),
                "OVIA 이형철근 단위중량표",
                new OviaWorkspaceNavigationEntry("REBAR_UNIT_WEIGHT")
            );
        }

        public void NavigateToSystemSettings()
        {
            if (!CanNavigateToMenu("SYSTEM_SETTINGS", "시스템 설정"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmSystemSettings(companyId, userId),
                "OVIA 시스템 설정",
                new OviaWorkspaceNavigationEntry("SYSTEM_SETTINGS")
            );
        }

        public void NavigateToMenuManager()
        {
            if (!CanNavigateToMenu("MENU_MANAGER", "메뉴관리"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmMenuManager(companyId, userId),
                "OVIA 메뉴관리",
                new OviaWorkspaceNavigationEntry("MENU_MANAGER")
            );
        }

        public void NavigateToVersionInfo()
        {
            if (!CanNavigateToMenu("VERSION_INFO", "버전정보"))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmVersionInfo(companyId, userId),
                "OVIA 버전정보",
                new OviaWorkspaceNavigationEntry("VERSION_INFO")
            );
        }

        public void NavigateToNotifications()
        {
            ShowScreenWithHistory(
                new FrmNotificationList(companyId, userId),
                "OVIA 알림",
                new OviaWorkspaceNavigationEntry("NOTIFICATIONS")
            );
        }

        public void NavigateToWorkspaceInfoPage(string menuKey, string pathText, string title, string selectedMenu, string helpText, string bodyText)
        {
            string displayTitle = string.IsNullOrWhiteSpace(title) ? "메뉴" : title.Trim();
            if (!CanNavigateToMenu(menuKey, displayTitle))
            {
                return;
            }

            ShowScreenWithHistory(
                new FrmOviaMenuPage(companyId, userId, menuKey, displayTitle, pathText, selectedMenu, helpText, bodyText),
                "OVIA " + displayTitle,
                new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", menuKey, pathText, displayTitle, selectedMenu, helpText, bodyText)
            );
        }

        private bool NavigateToStoredEntry(OviaWorkspaceNavigationEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            switch (entry.Kind)
            {
                case "PROJECT_MANAGER":
                    NavigateToProjectManager();
                    return true;
                case "PROJECT_REGISTER":
                    NavigateToProjectRegisterWebErp();
                    return true;
                case "ERP_MODULE_PAGE":
                    NavigateToErpModulePage(entry.Get(0));
                    return true;
                case "LEGACY_MAIN_DASHBOARD":
                    NavigateToLegacyMainDashboard();
                    return true;
                case "PROJECT_BARLIST_LIST":
                    NavigateToProjectBarListList(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3));
                    return true;
                case "BARLIST":
                    NavigateToBarList(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3), entry.Get(4));
                    return true;
                case "BARLIST_MAPPING":
                    NavigateToBarListMapping();
                    return true;
                case "REBAR_UNIT_WEIGHT":
                    NavigateToRebarUnitWeightTable();
                    return true;
                case "SYSTEM_SETTINGS":
                    NavigateToSystemSettings();
                    return true;
                case "MENU_MANAGER":
                    NavigateToMenuManager();
                    return true;
                case "VERSION_INFO":
                    NavigateToVersionInfo();
                    return true;
                case "NOTIFICATIONS":
                    NavigateToNotifications();
                    return true;
                case "WORKSPACE_INFO":
                    NavigateToWorkspaceInfoPage(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3), entry.Get(4), entry.Get(5));
                    return true;
                default:
                    return false;
            }
        }

        private OviaWorkspaceNavigationEntry ResolveParentNavigationEntry(OviaWorkspaceNavigationEntry entry)
        {
            if (entry == null || entry.Kind == "")
            {
                return null;
            }

            switch (entry.Kind)
            {
                case "ERP_MODULE_PAGE":
                    return ResolveErpModuleParentEntry(entry.Get(0));
                case "PROJECT_REGISTER":
                case "PROJECT_BARLIST_LIST":
                    return new OviaWorkspaceNavigationEntry("PROJECT_MANAGER");
                case "BARLIST":
                    return new OviaWorkspaceNavigationEntry("PROJECT_BARLIST_LIST", entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3));
                case "LEGACY_MAIN_DASHBOARD":
                case "BARLIST_MAPPING":
                case "REBAR_UNIT_WEIGHT":
                case "MENU_MANAGER":
                case "VERSION_INFO":
                    return CreateEnvironmentSettingsParentEntry();
                case "SYSTEM_SETTINGS":
                    return CreateEnvironmentSettingsParentEntry();
                case "WORKSPACE_INFO":
                    return ResolveWorkspaceInfoParentEntry(entry);
                case "NOTIFICATIONS":
                case "PROJECT_MANAGER":
                default:
                    return null;
            }
        }

        private OviaWorkspaceNavigationEntry ResolveErpModuleParentEntry(string menuKey)
        {
            string key = menuKey == null ? string.Empty : menuKey.Trim().ToUpperInvariant();
            if (key == "" || key == "ERP")
            {
                return null;
            }

            string selectedMenu = OviaMenuHelpStore.GetSelectedMenuKey(key).Trim().ToUpperInvariant();
            if (selectedMenu == "PROJECT") return new OviaWorkspaceNavigationEntry("PROJECT_MANAGER");
            if (selectedMenu == "SETTINGS") return CreateEnvironmentSettingsParentEntry();
            if (selectedMenu == "OPERATIONS") return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", "OPERATIONS", OviaMenuHelpStore.GetWorkspacePath("OPERATIONS", "메인  ›  운영현황"), OviaMenuHelpStore.GetMenuName("OPERATIONS", "운영현황"), "OPERATIONS", "운영현황 메뉴입니다.", "운영현황 하위 메뉴에서 업무 데이터를 조회합니다.");
            if (selectedMenu == "MATERIAL") return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", "MATERIAL_STOCK", OviaMenuHelpStore.GetWorkspacePath("MATERIAL_STOCK", "메인  ›  자재/재고"), OviaMenuHelpStore.GetMenuName("MATERIAL_STOCK", "자재/재고"), "MATERIAL", "자재/재고 메뉴입니다.", "자재/재고 하위 메뉴에서 데이터를 조회합니다.");
            if (selectedMenu == "SHIPPING") return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", "SHIPPING_INVOICE", OviaMenuHelpStore.GetWorkspacePath("SHIPPING_INVOICE", "메인  ›  출하/송장"), OviaMenuHelpStore.GetMenuName("SHIPPING_INVOICE", "출하/송장"), "SHIPPING", "출하/송장 메뉴입니다.", "출하/송장 하위 메뉴에서 데이터를 조회합니다.");
            if (selectedMenu == "MASTER") return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", "MASTER_DATA", OviaMenuHelpStore.GetWorkspacePath("MASTER_DATA", "메인  ›  기준정보"), OviaMenuHelpStore.GetMenuName("MASTER_DATA", "기준정보"), "MASTER", "기준정보 메뉴입니다.", "기준정보 하위 메뉴에서 데이터를 관리합니다.");
            return null;
        }

        private OviaWorkspaceNavigationEntry ResolveWorkspaceInfoParentEntry(OviaWorkspaceNavigationEntry entry)
        {
            string selectedMenu = entry.Get(3).Trim().ToUpperInvariant();
            string menuKey = entry.Get(0).Trim().ToUpperInvariant();
            string pathText = entry.Get(1);
            string[] segments = SplitWorkspacePath(pathText);

            if (segments.Length <= 2)
            {
                return null;
            }

            if (selectedMenu == "SETTINGS" || menuKey == "SETTINGS")
            {
                return CreateSystemManagementParentEntry();
            }

            string parentTitle = segments[segments.Length - 2];
            string parentPath = JoinWorkspacePath(segments, segments.Length - 1);
            string parentKey = GetTopMenuKeyBySelectedMenu(selectedMenu);
            string parentHelp = parentTitle + " 메뉴의 하위 업무를 선택할 수 있는 안내 화면입니다.";
            return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", parentKey, parentPath, parentTitle, selectedMenu, parentHelp, parentHelp);
        }

        private OviaWorkspaceNavigationEntry CreateSystemManagementParentEntry()
        {
            return new OviaWorkspaceNavigationEntry(
                "WORKSPACE_INFO",
                "SETTINGS",
                OviaMenuHelpStore.GetWorkspacePath("SETTINGS", "메인  ›  환경설정"),
                OviaMenuHelpStore.GetMenuName("SETTINGS", "환경설정"),
                "SETTINGS",
                "OVIA 전체 환경값, 메뉴, 권한, 출력 기준을 관리하는 상위 메뉴입니다.",
                "환경설정 하위 메뉴에서 필요한 설정 화면을 선택해 작업합니다."
            );
        }

        private OviaWorkspaceNavigationEntry CreateEnvironmentSettingsParentEntry()
        {
            return new OviaWorkspaceNavigationEntry(
                "WORKSPACE_INFO",
                "SETTINGS",
                "메인  ›  환경설정",
                "환경설정",
                "SETTINGS",
                "OVIA 전체 환경값을 관리하는 상위 설정 메뉴입니다.",
                "환경설정 하위 메뉴에서 ERP 연결, 회사 로고, 페이지 로딩 설정 같은 전체 적용값을 관리합니다."
            );
        }

        private string GetTopMenuKeyBySelectedMenu(string selectedMenu)
        {
            switch (selectedMenu)
            {
                case "PROJECT": return "PROJECT_MANAGER";
                case "OPERATIONS": return "OPERATIONS";
                case "MATERIAL": return "MATERIAL_STOCK";
                case "SHIPPING": return "SHIPPING_INVOICE";
                case "MASTER": return "MASTER_DATA";
                case "SETTINGS": return "SETTINGS";
                case "ERP": return "ERP";
                default: return "MAIN";
            }
        }

        private string[] SplitWorkspacePath(string pathText)
        {
            if (string.IsNullOrWhiteSpace(pathText))
            {
                return new string[0];
            }

            string[] raw = pathText.Split(new char[] { '›' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> parts = new List<string>();
            int i;
            for (i = 0; i < raw.Length; i++)
            {
                string part = raw[i] == null ? string.Empty : raw[i].Trim();
                if (part != string.Empty)
                {
                    parts.Add(part);
                }
            }

            return parts.ToArray();
        }

        private string JoinWorkspacePath(string[] segments, int count)
        {
            if (segments == null || count <= 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            int limit = Math.Min(count, segments.Length);
            int i;
            for (i = 0; i < limit; i++)
            {
                if (!string.IsNullOrWhiteSpace(segments[i]))
                {
                    parts.Add(segments[i].Trim());
                }
            }

            return string.Join("  ›  ", parts.ToArray());
        }

        public void ShowAutoCadEnvironmentCheck()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.Check();
            MessageBoxIcon icon = MessageBoxIcon.Information;

            if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                icon = MessageBoxIcon.Error;
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                icon = MessageBoxIcon.Warning;
            }

            MessageBox.Show(
                report.GetDisplayText(),
                "OVIA 설치 전 환경 점검 결과",
                MessageBoxButtons.OK,
                icon
            );
        }

        public void ShowAutoCadExtractGuide()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();

            if (!report.IsCurrentDevelopmentAutoCadReady())
            {
                MessageBox.Show(
                    report.GetAutoCadExtractionBlockMessage() + "\r\n\r\n" + report.GetDisplayText(),
                    "OVIA AutoCAD 추출 준비",
                    MessageBoxButtons.OK,
                    report.OverallStatus == OviaEnvironmentStatus.Blocked ? MessageBoxIcon.Error : MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                report.GetAutoCadExtractionReadyMessage(),
                "OVIA AutoCAD 활성",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        public void RequestLogout()
        {
            if (!OviaWorkspaceExitHelper.ConfirmLogout(this, currentScreen))
            {
                return;
            }

            systemExitConfirmed = true;
            this.Close();
        }

        private void ShowScreenWithHistory(Form nextScreen, string title, OviaWorkspaceNavigationEntry entry)
        {
            OviaWorkspaceNavigationEntry previousEntry = currentNavigationEntry;

            if (!ShowScreen(nextScreen))
            {
                return;
            }

            this.Text = title == null ? "OVIA" : title;

            if (!suppressNavigationHistory && previousEntry != null)
            {
                backHistory.Push(previousEntry);
                forwardHistory.Clear();
            }

            currentNavigationEntry = entry;
        }

        private bool ShowScreen(Form nextScreen)
        {
            if (nextScreen == null)
            {
                return false;
            }

            IOviaWorkspaceScreen currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
            {
                nextScreen.Dispose();
                return false;
            }

            this.SuspendLayout();
            hostPanel.SuspendLayout();

            try
            {
                if (currentScreen != null)
                {
                    currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

                    if (currentWorkspaceScreen != null)
                    {
                        currentWorkspaceScreen.BeforeLeaveWorkspaceScreen();
                    }

                    hostPanel.Controls.Remove(currentScreen);
                    currentScreen.Dispose();
                    currentScreen = null;
                }

                nextScreen.TopLevel = false;
                nextScreen.FormBorderStyle = FormBorderStyle.None;
                nextScreen.Dock = DockStyle.Fill;
                nextScreen.StartPosition = FormStartPosition.Manual;
                nextScreen.WindowState = FormWindowState.Normal;

                currentScreen = nextScreen;
                hostPanel.Controls.Add(nextScreen);
                nextScreen.Show();
                FillCurrentWorkspaceScreen();
                nextScreen.BringToFront();

                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (currentScreen == nextScreen && !nextScreen.IsDisposed)
                        {
                            FillCurrentWorkspaceScreen();
                        }
                    }));
                }
                catch
                {
                }
            }
            finally
            {
                hostPanel.ResumeLayout(false);
                this.ResumeLayout(false);
            }

            return true;
        }

        private void ApplyWorkspaceLayout(Form screen)
        {
            IOviaWorkspaceLayout workspaceLayout = screen as IOviaWorkspaceLayout;

            if (workspaceLayout != null)
            {
                workspaceLayout.ApplyWorkspaceLayout();
            }
        }

        private void FrmWorkspaceShell_FormClosing(object sender, FormClosingEventArgs e)
        {
            IOviaWorkspaceScreen currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (navigateToMainClose)
            {
                if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
                {
                    navigateToMainClose = false;
                    e.Cancel = true;
                    return;
                }
            }
            else if (!systemExitConfirmed)
            {
                if (!OviaWorkspaceExitHelper.ConfirmLogout(this, currentScreen))
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (currentWorkspaceScreen != null)
            {
                currentWorkspaceScreen.BeforeLeaveWorkspaceScreen();
            }
        }
    }
}

