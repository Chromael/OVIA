using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace OVIA.Desktop.Controls
{
    public class OviaWebViewHost : Panel
    {
        private WebView2 webView;
        private Panel messagePanel;
        private Label messageTitle;
        private Label messageBody;
        private Button retryButton;
        private Button openExternalButton;
        private Panel loadingOverlay;
        private OviaLoadingSymbolControl loadingSymbol;
        private Timer showLoadingTimer;
        private Timer hideLoadingTimer;
        private bool loadingPending;
        private bool navigationInProgress;
        private bool initializationStarted;
        private bool navigationEventsAttached;
        private bool webViewBridgeEventsAttached;
        private bool webViewClickScriptInjected;
        private bool celmonWwwRetryAttempted;
        private bool documentHeightMeasureInProgress;
        private int documentHeightMeasureCount;
        private int lastDocumentHeight;
        private Timer documentHeightTimer;
        private string initialUrl = "https://celmon.com";

        public bool AutoResizeToDocumentHeight { get; set; }
        // WebView2 안에서는 웹페이지 자체 스크롤을 우선 사용한다.
        // 이전 외부 AutoScroll 전달 방식은 Web ERP 스크롤 충돌 때문에 기본 비활성화한다.
        public bool ForwardMouseWheelToParentScroll { get; set; }
        public int MinimumDocumentHeight { get; set; }
        public int MaximumDocumentHeight { get; set; }
        public int LastDocumentHeight { get { return lastDocumentHeight; } }
        public event EventHandler<OviaWebViewDocumentHeightChangedEventArgs> DocumentHeightChanged;
        public event EventHandler NavigationStateChanged;

        public string InitialUrl
        {
            get { return initialUrl; }
            set { initialUrl = NormalizeUrl(value); }
        }

        public bool CanGoBackInWebView
        {
            get
            {
                try
                {
                    return webView != null && webView.CoreWebView2 != null && webView.CoreWebView2.CanGoBack;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool CanGoForwardInWebView
        {
            get
            {
                try
                {
                    return webView != null && webView.CoreWebView2 != null && webView.CoreWebView2.CanGoForward;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryGoBackInWebView()
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null || !webView.CoreWebView2.CanGoBack)
                {
                    return false;
                }

                ShowLoadingOverlay();
                webView.CoreWebView2.GoBack();
                RaiseNavigationStateChanged();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGoForwardInWebView()
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null || !webView.CoreWebView2.CanGoForward)
                {
                    return false;
                }

                ShowLoadingOverlay();
                webView.CoreWebView2.GoForward();
                RaiseNavigationStateChanged();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryReloadCurrentWebViewPage()
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null)
                {
                    return false;
                }

                ShowLoadingOverlay();
                webView.CoreWebView2.Reload();
                RaiseNavigationStateChanged();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public OviaWebViewHost()
        {
            MinimumDocumentHeight = 480;
            MaximumDocumentHeight = 20000;
            ForwardMouseWheelToParentScroll = false;
            BuildUI();
        }

        private void BuildUI()
        {
            this.BackColor = Color.White;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = Color.White;
            webView.Visible = false;
            webView.GotFocus += delegate { NotifyWebViewPointerInteraction(); };
            webView.MouseDown += delegate { NotifyWebViewPointerInteraction(); };
            this.Controls.Add(webView);

            messagePanel = new Panel();
            messagePanel.Dock = DockStyle.Fill;
            messagePanel.BackColor = Color.White;
            messagePanel.Visible = false;
            this.Controls.Add(messagePanel);

            messageTitle = new Label();
            messageTitle.AutoSize = false;
            messageTitle.Text = "WebView2 준비 중";
            messageTitle.Font = OviaFluentTheme.FontTitle(11.5F, FontStyle.Bold);
            messageTitle.ForeColor = OviaFluentTheme.TextPrimary;
            messageTitle.BackColor = Color.Transparent;
            messageTitle.TextAlign = ContentAlignment.MiddleLeft;
            messageTitle.Location = new Point(22, 20);
            messageTitle.Size = new Size(600, 30);
            messagePanel.Controls.Add(messageTitle);

            messageBody = new Label();
            messageBody.AutoSize = false;
            messageBody.Text = "웹 ERP 테스트 페이지를 불러오고 있습니다.";
            messageBody.Font = OviaFluentTheme.FontSystem(9.2F, FontStyle.Regular);
            messageBody.ForeColor = OviaFluentTheme.TextSecondary;
            messageBody.BackColor = Color.Transparent;
            messageBody.TextAlign = ContentAlignment.TopLeft;
            messageBody.Location = new Point(22, 58);
            messageBody.Size = new Size(700, 190);
            messagePanel.Controls.Add(messageBody);

            retryButton = CreateSmallButton("다시 시도", true);
            retryButton.Location = new Point(22, 264);
            retryButton.Click += delegate
            {
                initializationStarted = false;
                EnsureInitialized();
            };
            messagePanel.Controls.Add(retryButton);

            openExternalButton = CreateSmallButton("브라우저로 열기", false);
            openExternalButton.Location = new Point(118, 264);
            openExternalButton.Click += delegate
            {
                OpenExternalBrowser(InitialUrl);
            };
            messagePanel.Controls.Add(openExternalButton);

            loadingOverlay = new OviaTransparentLoadingPanel();
            loadingOverlay.Dock = DockStyle.None;
            loadingOverlay.Size = new Size(112, 112);
            loadingOverlay.BackColor = Color.Transparent;
            loadingOverlay.Visible = false;
            loadingOverlay.Margin = Padding.Empty;
            loadingOverlay.Padding = Padding.Empty;
            this.Controls.Add(loadingOverlay);

            loadingSymbol = new OviaLoadingSymbolControl();
            loadingSymbol.Size = new Size(112, 112);
            loadingSymbol.BackColor = Color.Transparent;
            loadingSymbol.Location = Point.Empty;
            loadingOverlay.Controls.Add(loadingSymbol);

            this.MouseDown += delegate { NotifyWebViewPointerInteraction(); };
            this.Enter += delegate { NotifyWebViewPointerInteraction(); };

            this.Resize += delegate
            {
                messageTitle.Width = Math.Max(100, this.ClientSize.Width - 44);
                messageBody.Width = Math.Max(100, this.ClientSize.Width - 44);
                messageBody.Height = Math.Max(88, this.ClientSize.Height - 132);
                int buttonTop = Math.Min(Math.Max(154, messageBody.Bottom + 10), Math.Max(154, this.ClientSize.Height - 44));
                retryButton.Top = buttonTop;
                openExternalButton.Top = buttonTop;
                LayoutLoadingOverlay();
            };

            documentHeightTimer = new Timer();
            documentHeightTimer.Interval = 300;
            documentHeightTimer.Tick += DocumentHeightTimer_Tick;

            showLoadingTimer = new Timer();
            showLoadingTimer.Interval = Math.Max(1, OVIA.Desktop.OviaSystemSettingsStore.GetLoadingDelayMilliseconds());
            showLoadingTimer.Tick += delegate
            {
                showLoadingTimer.Stop();
                if (navigationInProgress && loadingPending)
                {
                    ShowLoadingOverlayNow();
                }
            };

            hideLoadingTimer = new Timer();
            hideLoadingTimer.Interval = 1;
            hideLoadingTimer.Tick += delegate
            {
                hideLoadingTimer.Stop();
                HideLoadingOverlay();
            };

            LayoutLoadingOverlay();

        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            EnsureInitialized();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (documentHeightTimer != null)
                {
                    documentHeightTimer.Stop();
                    documentHeightTimer.Dispose();
                    documentHeightTimer = null;
                }

                if (showLoadingTimer != null)
                {
                    showLoadingTimer.Stop();
                    showLoadingTimer.Dispose();
                    showLoadingTimer = null;
                }

                if (hideLoadingTimer != null)
                {
                    hideLoadingTimer.Stop();
                    hideLoadingTimer.Dispose();
                    hideLoadingTimer = null;
                }

                if (loadingSymbol != null)
                {
                    loadingSymbol.Stop();
                    loadingSymbol.Dispose();
                    loadingSymbol = null;
                }

            }

            base.Dispose(disposing);
        }

        public void Navigate(string url)
        {
            InitialUrl = url;
            celmonWwwRetryAttempted = false;

            if (webView != null && webView.CoreWebView2 != null)
            {
                NavigateCore(InitialUrl);
            }
        }

        private async void EnsureInitialized()
        {
            if (initializationStarted)
            {
                return;
            }

            initializationStarted = true;

            OviaWebView2RuntimeInfo runtime = OviaWebView2RuntimeChecker.GetRuntimeInfo();
            if (runtime == null || !runtime.IsAvailable)
            {
                ShowMessage(
                    "WebView2 Runtime이 필요합니다.",
                    "OVIA가 웹 ERP 화면을 내부에서 표시하려면 Microsoft Edge WebView2 Runtime이 필요합니다.\r\n\r\n설치 프로그램 단계에서 Runtime 존재 여부를 확인하고, 없으면 설치하도록 연결해야 합니다."
                );
                return;
            }

            try
            {
                ShowLoadingOverlay();
                await webView.EnsureCoreWebView2Async(null);

                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    AttachNavigationEvents();
                    AttachWebViewBridgeEvents();
                    InjectWebViewPointerScript();
                    NavigateCore(InitialUrl);
                }
            }
            catch (Exception ex)
            {
                ShowMessage(
                    "WebView2 초기화 실패",
                    "웹 ERP 테스트 페이지를 불러오는 중 오류가 발생했습니다.\r\n\r\n주소: " + InitialUrl + "\r\n\r\n" + ex.Message
                );
            }
        }

        private void AttachWebViewBridgeEvents()
        {
            if (webViewBridgeEventsAttached || webView == null || webView.CoreWebView2 == null)
            {
                return;
            }

            webViewBridgeEventsAttached = true;
            webView.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;
        }

        private async void InjectWebViewPointerScript()
        {
            if (webViewClickScriptInjected || webView == null || webView.CoreWebView2 == null)
            {
                return;
            }

            webViewClickScriptInjected = true;

            try
            {
                string script =
                    "(() => {" +
                    "const notify = () => { try { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('ovia.webview.pointerdown'); } } catch(e) {} };" +
                    "window.addEventListener('pointerdown', notify, true);" +
                    "window.addEventListener('mousedown', notify, true);" +
                    "window.addEventListener('touchstart', notify, true);" +
                    "window.addEventListener('focus', notify, true);" +
                    "})();";

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }
            catch
            {
            }
        }

        private void HandleWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = string.Empty;

            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch
            {
                message = string.Empty;
            }

            if (String.Equals(message, "ovia.webview.pointerdown", StringComparison.OrdinalIgnoreCase))
            {
                NotifyWebViewPointerInteraction();
            }
        }

        private void NotifyWebViewPointerInteraction()
        {
            try
            {
                OVIA.Desktop.OviaWorkspaceCommandBar.CloseOpenDropDown();
            }
            catch
            {
            }
        }

        private void AttachNavigationEvents()
        {
            if (navigationEventsAttached || webView == null || webView.CoreWebView2 == null)
            {
                return;
            }

            navigationEventsAttached = true;
            webView.CoreWebView2.NavigationStarting += HandleNavigationStarting;
            webView.CoreWebView2.NavigationCompleted += HandleNavigationCompleted;
            webView.CoreWebView2.SourceChanged += HandleSourceChanged;
            webView.CoreWebView2.HistoryChanged += HandleHistoryChanged;
            RaiseNavigationStateChanged();
        }

        private void HandleNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            ShowLoadingOverlay();
            RaiseNavigationStateChanged();
        }

        private void HandleSourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            RaiseNavigationStateChanged();
        }

        private void HandleHistoryChanged(object sender, object e)
        {
            RaiseNavigationStateChanged();
        }

        private void RaiseNavigationStateChanged()
        {
            EventHandler handler = NavigationStateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void NavigateCore(string url)
        {
            if (webView == null || webView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                string target = NormalizeUrl(url);
                ShowLoadingOverlay();
                webView.CoreWebView2.Navigate(target);
                ShowWebView();
                RaiseNavigationStateChanged();
            }
            catch
            {
                ShowMessage("WebView2 이동 실패", "요청한 웹 ERP 주소로 이동하지 못했습니다.\r\n\r\n주소: " + NormalizeUrl(url));
            }
        }

        private void HandleNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            int statusCode = 0;
            try
            {
                statusCode = e.HttpStatusCode;
            }
            catch
            {
                statusCode = 0;
            }

            if (e.IsSuccess && statusCode < 400)
            {
                ShowWebView();
                ScheduleDocumentHeightMeasurements();
                BeginHideLoadingOverlay();
                RaiseNavigationStateChanged();
                return;
            }

            string currentUrl = GetCurrentWebViewUrl();
            string fallbackUrl;
            if (!celmonWwwRetryAttempted && statusCode == 403 && TryGetCelmonWwwFallback(currentUrl, out fallbackUrl))
            {
                celmonWwwRetryAttempted = true;
                initialUrl = fallbackUrl;
                ShowLoadingOverlay();
                NavigateCore(fallbackUrl);
                return;
            }

            ShowWebAccessMessage(currentUrl, statusCode, e.WebErrorStatus.ToString());
            RaiseNavigationStateChanged();
        }

        private string GetCurrentWebViewUrl()
        {
            try
            {
                if (webView != null && webView.CoreWebView2 != null && !string.IsNullOrWhiteSpace(webView.CoreWebView2.Source))
                {
                    return webView.CoreWebView2.Source;
                }
            }
            catch
            {
            }

            try
            {
                if (webView != null && webView.Source != null)
                {
                    return webView.Source.AbsoluteUri;
                }
            }
            catch
            {
            }

            return InitialUrl;
        }

        private void ShowWebAccessMessage(string url, int statusCode, string webErrorStatus)
        {
            string statusText = statusCode > 0 ? statusCode.ToString() : webErrorStatus;
            string body =
                "WebView2는 정상 실행되었지만, 테스트 주소의 서버가 페이지 표시를 허용하지 않았습니다.\r\n\r\n" +
                "HTTP 상태: " + statusText + "\r\n" +
                "주소: " + NormalizeUrl(url) + "\r\n\r\n" +
                "이 메시지는 OVIA 오류가 아니라 웹 서버의 접근 권한/보안 설정 응답입니다.\r\n" +
                "실제 Web ERP 주소가 준비되면 환경설정 > 시스템 설정의 ERP 연결 주소에 해당 주소를 입력해서 테스트하세요.";

            ShowMessage("웹 페이지 접근 실패", body);
        }

        private static bool TryGetCelmonWwwFallback(string url, out string fallbackUrl)
        {
            fallbackUrl = null;

            Uri uri;
            if (!Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out uri))
            {
                return false;
            }

            if (!uri.Host.Equals("celmon.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            UriBuilder builder = new UriBuilder(uri);
            builder.Host = "www.celmon.com";
            fallbackUrl = builder.Uri.AbsoluteUri;
            return true;
        }

        private void ScheduleDocumentHeightMeasurements()
        {
            if (!AutoResizeToDocumentHeight || documentHeightTimer == null)
            {
                return;
            }

            documentHeightMeasureCount = 0;
            documentHeightTimer.Stop();
            documentHeightTimer.Interval = 300;
            documentHeightTimer.Start();
            MeasureDocumentHeightOnce();
        }

        private void DocumentHeightTimer_Tick(object sender, EventArgs e)
        {
            if (!AutoResizeToDocumentHeight)
            {
                documentHeightTimer.Stop();
                return;
            }

            documentHeightMeasureCount++;
            MeasureDocumentHeightOnce();

            if (documentHeightMeasureCount == 1)
            {
                documentHeightTimer.Interval = 700;
            }
            else if (documentHeightMeasureCount == 3)
            {
                documentHeightTimer.Interval = 1500;
            }
            else if (documentHeightMeasureCount >= 7)
            {
                documentHeightTimer.Stop();
            }
        }

        private async void MeasureDocumentHeightOnce()
        {
            if (documentHeightMeasureInProgress || webView == null || webView.CoreWebView2 == null)
            {
                return;
            }

            documentHeightMeasureInProgress = true;

            try
            {
                string script =
                    "(() => {" +
                    "const body=document.body||{};" +
                    "const html=document.documentElement||{};" +
                    "const app=document.querySelector('[data-ovia-page-root]')||document.querySelector('#app')||document.querySelector('#root')||document.querySelector('main');" +
                    "let rootHeight=0;" +
                    "if(app){const r=app.getBoundingClientRect();rootHeight=Math.max(app.scrollHeight||0,app.offsetHeight||0,Math.ceil(r.bottom));}" +
                    "const h=Math.max(rootHeight,body.scrollHeight||0,body.offsetHeight||0,html.clientHeight||0,html.scrollHeight||0,html.offsetHeight||0,window.innerHeight||0);" +
                    "return Math.ceil(h);" +
                    "})()";

                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                int height;
                if (TryParseScriptInt(result, out height))
                {
                    ApplyDocumentHeight(height);
                }
            }
            catch
            {
            }
            finally
            {
                documentHeightMeasureInProgress = false;
            }
        }

        private static bool TryParseScriptInt(string value, out int result)
        {
            result = 0;
            string text = value == null ? "" : value.Trim().Trim('"');
            double numeric;
            if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out numeric))
            {
                result = (int)Math.Ceiling(numeric);
                return result > 0;
            }

            return false;
        }

        private void ApplyDocumentHeight(int measuredHeight)
        {
            int minHeight = MinimumDocumentHeight <= 0 ? 480 : MinimumDocumentHeight;
            int maxHeight = MaximumDocumentHeight <= 0 ? 20000 : MaximumDocumentHeight;
            int targetHeight = Math.Max(minHeight, Math.Min(maxHeight, measuredHeight + 2));

            if (Math.Abs(targetHeight - lastDocumentHeight) <= 6)
            {
                return;
            }

            lastDocumentHeight = targetHeight;

            if (this.Dock == DockStyle.None)
            {
                this.Height = targetHeight;
            }

            EventHandler<OviaWebViewDocumentHeightChangedEventArgs> handler = DocumentHeightChanged;
            if (handler != null)
            {
                handler(this, new OviaWebViewDocumentHeightChangedEventArgs(targetHeight));
            }
        }

        private void ShowWebView()
        {
            if (messagePanel != null)
            {
                messagePanel.Visible = false;
            }

            if (webView != null)
            {
                webView.Visible = true;
                webView.BringToFront();
            }

            if (loadingOverlay != null && loadingOverlay.Visible)
            {
                loadingOverlay.BringToFront();
            }
        }

        private void LayoutLoadingOverlay()
        {
            if (loadingOverlay == null || loadingSymbol == null)
            {
                return;
            }

            loadingOverlay.Size = loadingSymbol.Size;
            int x = Math.Max(0, (this.ClientSize.Width - loadingOverlay.Width) / 2);
            int y = Math.Max(0, (this.ClientSize.Height - loadingOverlay.Height) / 2);
            loadingOverlay.Location = new Point(x, y);
            loadingSymbol.Location = Point.Empty;
        }

        private void ApplyLoadingSettings()
        {
            int delay = OVIA.Desktop.OviaSystemSettingsStore.GetLoadingDelayMilliseconds();
            if (delay < 1)
            {
                delay = 1;
            }

            if (showLoadingTimer != null)
            {
                showLoadingTimer.Interval = delay;
            }

            if (loadingSymbol != null)
            {
                loadingSymbol.SetImagePath(OVIA.Desktop.OviaSystemSettingsStore.GetConfiguredLoadingAnimationImagePath());
            }
        }

        private void ShowLoadingOverlay()
        {
            ApplyLoadingSettings();

            if (hideLoadingTimer != null)
            {
                hideLoadingTimer.Stop();
            }

            loadingPending = true;
            navigationInProgress = true;

            if (showLoadingTimer != null)
            {
                showLoadingTimer.Stop();
                showLoadingTimer.Start();
                return;
            }

            ShowLoadingOverlayNow();
        }

        private void ShowLoadingOverlayNow()
        {
            if (!navigationInProgress || !loadingPending)
            {
                return;
            }

            if (messagePanel != null)
            {
                messagePanel.Visible = false;
            }

            if (webView != null)
            {
                webView.Visible = true;
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.Visible = true;
                loadingOverlay.BringToFront();
            }

            if (loadingSymbol != null)
            {
                LayoutLoadingOverlay();
                loadingSymbol.Start();
            }
        }

        private void BeginHideLoadingOverlay()
        {
            HideLoadingOverlay();
        }

        private void HideLoadingOverlay()
        {
            loadingPending = false;
            navigationInProgress = false;

            if (showLoadingTimer != null)
            {
                showLoadingTimer.Stop();
            }

            if (hideLoadingTimer != null)
            {
                hideLoadingTimer.Stop();
            }

            if (loadingSymbol != null)
            {
                loadingSymbol.Stop();
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.Visible = false;
            }

            if (webView != null)
            {
                webView.Visible = true;
                webView.BringToFront();
            }
        }

        private void ShowMessage(string title, string body)
        {
            if (messageTitle != null)
            {
                messageTitle.Text = title == null ? "" : title;
            }

            if (messageBody != null)
            {
                messageBody.Text = body == null ? "" : body;
            }

            loadingPending = false;
            navigationInProgress = false;

            if (showLoadingTimer != null)
            {
                showLoadingTimer.Stop();
            }

            if (hideLoadingTimer != null)
            {
                hideLoadingTimer.Stop();
            }

            if (loadingSymbol != null)
            {
                loadingSymbol.Stop();
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.Visible = false;
            }

            if (webView != null)
            {
                webView.Visible = false;
            }

            if (messagePanel != null)
            {
                messagePanel.Visible = true;
                messagePanel.BringToFront();
            }
        }

        private static Button CreateSmallButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(86, 28);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = OviaFluentTheme.FontButton(8.5F, FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.UseVisualStyleBackColor = false;

            if (primary)
            {
                button.BackColor = OviaFluentTheme.PrimaryActionBack;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = OviaFluentTheme.PrimaryActionBack;
                button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.PrimaryActionHoverBack;
                button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.PrimaryActionHoverBack;
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = OviaFluentTheme.TextSecondary;
                button.FlatAppearance.BorderColor = OviaFluentTheme.ControlBorder;
                button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.NeutralButton;
                button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.NeutralButton;
            }

            return button;
        }

        private static void OpenExternalBrowser(string url)
        {
            string target = NormalizeUrl(url);

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = target;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
            }
        }

        public static string NormalizeUrl(string value)
        {
            string url = value == null ? "" : value.Trim();

            if (url == "")
            {
                return "https://celmon.com";
            }

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return uri.AbsoluteUri;
                }
            }

            string candidate = "https://" + url;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out uri))
            {
                return uri.AbsoluteUri;
            }

            return "https://celmon.com";
        }

        private sealed class OviaTransparentLoadingPanel : Panel
        {
            public OviaTransparentLoadingPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                PaintParentBackground(e);
            }

            private void PaintParentBackground(PaintEventArgs e)
            {
                if (Parent == null)
                {
                    base.OnPaintBackground(e);
                    return;
                }

                GraphicsState state = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Left, -Top);
                    using (PaintEventArgs parentArgs = new PaintEventArgs(e.Graphics, new Rectangle(Left, Top, Width, Height)))
                    {
                        InvokePaintBackground(Parent, parentArgs);
                        InvokePaint(Parent, parentArgs);
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }
        }

        private sealed class OviaLoadingSymbolControl : Control
        {
            private readonly Timer animationTimer;
            private Image symbolImage;
            private string symbolImagePath = "";
            private int angle;

            public OviaLoadingSymbolControl()
            {
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                this.TabStop = false;
                this.Cursor = Cursors.Default;
                SetImagePath(OVIA.Desktop.OviaSystemSettingsStore.GetConfiguredLoadingAnimationImagePath());

                animationTimer = new Timer();
                animationTimer.Interval = 32;
                animationTimer.Tick += delegate
                {
                    angle = (angle + 6) % 360;
                    this.Invalidate();
                };
            }

            public void SetImagePath(string path)
            {
                string normalizedPath = path == null ? "" : path.Trim();
                if (string.Equals(symbolImagePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Image old = symbolImage;
                symbolImage = null;
                symbolImagePath = normalizedPath;

                if (old != null)
                {
                    old.Dispose();
                }

                symbolImage = LoadSymbolImage(normalizedPath);
                Invalidate();
            }

            public void Start()
            {
                if (animationTimer != null && !animationTimer.Enabled)
                {
                    animationTimer.Start();
                }
            }

            public void Stop()
            {
                if (animationTimer != null)
                {
                    animationTimer.Stop();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (animationTimer != null)
                    {
                        animationTimer.Stop();
                        animationTimer.Dispose();
                    }

                    if (symbolImage != null)
                    {
                        symbolImage.Dispose();
                    }
                }

                base.Dispose(disposing);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                PaintParentBackground(e);
            }

            private void PaintParentBackground(PaintEventArgs e)
            {
                if (Parent == null)
                {
                    base.OnPaintBackground(e);
                    return;
                }

                GraphicsState state = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Left, -Top);
                    using (PaintEventArgs parentArgs = new PaintEventArgs(e.Graphics, new Rectangle(Left, Top, Width, Height)))
                    {
                        InvokePaintBackground(Parent, parentArgs);
                        InvokePaint(Parent, parentArgs);
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float cx = this.ClientSize.Width / 2F;
                float cy = this.ClientSize.Height / 2F;
                float size = Math.Min(this.ClientSize.Width, this.ClientSize.Height) * 0.62F;

                if (symbolImage != null)
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.TranslateTransform(cx, cy);
                    e.Graphics.RotateTransform(angle);
                    RectangleF rect = new RectangleF(-size / 2F, -size / 2F, size, size);
                    e.Graphics.DrawImage(symbolImage, rect);
                    e.Graphics.Restore(state);
                    return;
                }

                using (Pen pen = new Pen(OviaFluentTheme.Accent, 5F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    RectangleF arcRect = new RectangleF(cx - size / 2F, cy - size / 2F, size, size);
                    e.Graphics.DrawArc(pen, arcRect, angle, 270);
                }
            }

            private static Image LoadSymbolImage(string preferredPath)
            {
                string defaultPath = OVIA.Desktop.OviaSystemSettingsStore.GetDefaultLoadingSymbolPath();
                string[] candidates = new string[]
                {
                    preferredPath,
                    defaultPath
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    string path = candidates[i];
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            using (Image loaded = Image.FromFile(path))
                            {
                                Bitmap bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppPArgb);
                                using (Graphics g = Graphics.FromImage(bitmap))
                                {
                                    g.Clear(Color.Transparent);
                                    g.CompositingMode = CompositingMode.SourceCopy;
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    g.DrawImage(loaded, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                                }

                                return bitmap;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return null;
            }
        }
    }



    public sealed class OviaContentLoadingOverlay : Panel
    {
        private readonly OviaContentLoadingSymbolControl loadingSymbol;
        private readonly Timer showTimer;
        private Control resizeSubscribedParent;
        private bool loadingPending;
        private bool loadingInProgress;

        public OviaContentLoadingOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Dock = DockStyle.None;
            Size = new Size(112, 112);
            BackColor = Color.Transparent;
            Visible = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;

            loadingSymbol = new OviaContentLoadingSymbolControl();
            loadingSymbol.Size = new Size(112, 112);
            loadingSymbol.BackColor = Color.Transparent;
            loadingSymbol.Location = Point.Empty;
            Controls.Add(loadingSymbol);

            showTimer = new Timer();
            showTimer.Interval = Math.Max(1, OVIA.Desktop.OviaSystemSettingsStore.GetLoadingDelayMilliseconds());
            showTimer.Tick += delegate
            {
                showTimer.Stop();
                if (loadingInProgress && loadingPending)
                {
                    ShowOverlayNow();
                }
            };

            Resize += delegate { LayoutLoadingSymbol(); };
        }

        protected override void OnParentChanged(EventArgs e)
        {
            if (resizeSubscribedParent != null)
            {
                resizeSubscribedParent.Resize -= Parent_Resize;
                resizeSubscribedParent = null;
            }

            base.OnParentChanged(e);

            if (Parent != null)
            {
                resizeSubscribedParent = Parent;
                resizeSubscribedParent.Resize += Parent_Resize;
            }

            LayoutLoadingSymbol();
        }

        private void Parent_Resize(object sender, EventArgs e)
        {
            LayoutLoadingSymbol();
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (resizeSubscribedParent != null)
                {
                    resizeSubscribedParent.Resize -= Parent_Resize;
                    resizeSubscribedParent = null;
                }

                if (showTimer != null)
                {
                    showTimer.Stop();
                    showTimer.Dispose();
                }

                if (loadingSymbol != null)
                {
                    loadingSymbol.Stop();
                    loadingSymbol.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public void BeginLoading()
        {
            ApplyLoadingSettings();
            loadingPending = true;
            loadingInProgress = true;

            if (showTimer != null)
            {
                showTimer.Stop();
                showTimer.Start();
                return;
            }

            ShowOverlayNow();
        }

        public void EndLoading()
        {
            loadingPending = false;
            loadingInProgress = false;

            if (showTimer != null)
            {
                showTimer.Stop();
            }

            if (loadingSymbol != null)
            {
                loadingSymbol.Stop();
            }

            Visible = false;
        }

        public void ShowOverlayNow()
        {
            if (!loadingInProgress || !loadingPending)
            {
                return;
            }

            LayoutLoadingSymbol();
            Visible = true;
            BringToFront();

            if (loadingSymbol != null)
            {
                loadingSymbol.Start();
            }
        }

        private void ApplyLoadingSettings()
        {
            int delay = OVIA.Desktop.OviaSystemSettingsStore.GetLoadingDelayMilliseconds();
            if (delay < 1)
            {
                delay = 1;
            }

            if (showTimer != null)
            {
                showTimer.Interval = delay;
            }

            if (loadingSymbol != null)
            {
                loadingSymbol.SetImagePath(OVIA.Desktop.OviaSystemSettingsStore.GetConfiguredLoadingAnimationImagePath());
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent == null)
            {
                base.OnPaintBackground(e);
                return;
            }

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.TranslateTransform(-Left, -Top);
                using (PaintEventArgs parentArgs = new PaintEventArgs(e.Graphics, new Rectangle(Left, Top, Width, Height)))
                {
                    InvokePaintBackground(Parent, parentArgs);
                    InvokePaint(Parent, parentArgs);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        private void LayoutLoadingSymbol()
        {
            if (loadingSymbol == null)
            {
                return;
            }

            Size = loadingSymbol.Size;

            if (Parent != null)
            {
                int x = Math.Max(0, (Parent.ClientSize.Width - Width) / 2);
                int y = Math.Max(0, (Parent.ClientSize.Height - Height) / 2);
                Location = new Point(x, y);
            }

            loadingSymbol.Location = Point.Empty;
        }

        private sealed class OviaContentLoadingSymbolControl : Control
        {
            private readonly Timer animationTimer;
            private Image symbolImage;
            private string symbolImagePath = "";
            private int angle;

            public OviaContentLoadingSymbolControl()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                TabStop = false;
                Cursor = Cursors.Default;
                SetImagePath(OVIA.Desktop.OviaSystemSettingsStore.GetConfiguredLoadingAnimationImagePath());

                animationTimer = new Timer();
                animationTimer.Interval = 32;
                animationTimer.Tick += delegate
                {
                    angle = (angle + 6) % 360;
                    Invalidate();
                };
            }

            public void SetImagePath(string path)
            {
                string normalizedPath = path == null ? "" : path.Trim();
                if (string.Equals(symbolImagePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Image old = symbolImage;
                symbolImage = null;
                symbolImagePath = normalizedPath;

                if (old != null)
                {
                    old.Dispose();
                }

                symbolImage = LoadSymbolImage(normalizedPath);
                Invalidate();
            }

            public void Start()
            {
                if (animationTimer != null && !animationTimer.Enabled)
                {
                    animationTimer.Start();
                }
            }

            public void Stop()
            {
                if (animationTimer != null)
                {
                    animationTimer.Stop();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (animationTimer != null)
                    {
                        animationTimer.Stop();
                        animationTimer.Dispose();
                    }

                    if (symbolImage != null)
                    {
                        symbolImage.Dispose();
                    }
                }

                base.Dispose(disposing);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                PaintParentBackground(e);
            }

            private void PaintParentBackground(PaintEventArgs e)
            {
                if (Parent == null)
                {
                    base.OnPaintBackground(e);
                    return;
                }

                GraphicsState state = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Left, -Top);
                    using (PaintEventArgs parentArgs = new PaintEventArgs(e.Graphics, new Rectangle(Left, Top, Width, Height)))
                    {
                        InvokePaintBackground(Parent, parentArgs);
                        InvokePaint(Parent, parentArgs);
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float cx = ClientSize.Width / 2F;
                float cy = ClientSize.Height / 2F;
                float size = Math.Min(ClientSize.Width, ClientSize.Height) * 0.62F;

                if (symbolImage != null)
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.TranslateTransform(cx, cy);
                    e.Graphics.RotateTransform(angle);
                    RectangleF rect = new RectangleF(-size / 2F, -size / 2F, size, size);
                    e.Graphics.DrawImage(symbolImage, rect);
                    e.Graphics.Restore(state);
                    return;
                }

                using (Pen pen = new Pen(OviaFluentTheme.Accent, 5F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    RectangleF arcRect = new RectangleF(cx - size / 2F, cy - size / 2F, size, size);
                    e.Graphics.DrawArc(pen, arcRect, angle, 270);
                }
            }

            private static Image LoadSymbolImage(string preferredPath)
            {
                string defaultPath = OVIA.Desktop.OviaSystemSettingsStore.GetDefaultLoadingSymbolPath();
                string[] candidates = new string[] { preferredPath, defaultPath };

                for (int i = 0; i < candidates.Length; i++)
                {
                    string path = candidates[i];
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            using (Image loaded = Image.FromFile(path))
                            {
                                Bitmap bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppPArgb);
                                using (Graphics g = Graphics.FromImage(bitmap))
                                {
                                    g.Clear(Color.Transparent);
                                    g.CompositingMode = CompositingMode.SourceCopy;
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    g.DrawImage(loaded, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                                }

                                return bitmap;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return null;
            }
        }
    }

    public class OviaWebViewDocumentHeightChangedEventArgs : EventArgs
    {
        public OviaWebViewDocumentHeightChangedEventArgs(int documentHeight)
        {
            DocumentHeight = documentHeight;
        }

        public int DocumentHeight { get; private set; }
    }
}
