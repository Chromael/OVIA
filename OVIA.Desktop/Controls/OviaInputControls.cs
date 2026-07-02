using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop.Controls
{
    /// <summary>
    /// OVIA 공통 검색 입력창.
    /// Windows 11 검색창과 유사한 옅은 회색 테두리, 라운드, 우측 검색 아이콘을 사용한다.
    /// 마우스 오버/포커스 시 배경을 흰색으로 전환한다.
    /// </summary>
    public class OviaSearchBox : UserControl
    {
        private readonly TextBox innerTextBox;
        private readonly Label placeholderLabel;
        private readonly Label searchIconLabel;
        private bool isFocused;
        private bool isHovered;

        public new event EventHandler TextChanged;

        public OviaSearchBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Size = new Size(360, OviaFluentTheme.CommonInputHeight);
            MinimumSize = new Size(120, OviaFluentTheme.CommonInputHeight);
            Padding = Padding.Empty;
            TabStop = true;

            innerTextBox = new TextBox();
            innerTextBox.BorderStyle = BorderStyle.None;
            innerTextBox.Font = OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            innerTextBox.ForeColor = OviaFluentTheme.TextPrimary;
            innerTextBox.BackColor = CurrentBackgroundColor;
            innerTextBox.Location = new Point(12, 10);
            innerTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            innerTextBox.TextChanged += InnerTextBox_TextChanged;
            innerTextBox.Enter += FocusChanged;
            innerTextBox.Leave += FocusChanged;
            AttachHoverEvents(innerTextBox);
            Controls.Add(innerTextBox);

            placeholderLabel = new Label();
            placeholderLabel.AutoSize = false;
            placeholderLabel.Text = "검색";
            placeholderLabel.Font = OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            placeholderLabel.ForeColor = OviaFluentTheme.CommonInputPlaceholder;
            placeholderLabel.BackColor = CurrentBackgroundColor;
            placeholderLabel.Cursor = Cursors.IBeam;
            placeholderLabel.Click += delegate { innerTextBox.Focus(); };
            AttachHoverEvents(placeholderLabel);
            Controls.Add(placeholderLabel);

            searchIconLabel = new Label();
            searchIconLabel.AutoSize = false;
            searchIconLabel.TextAlign = ContentAlignment.MiddleCenter;
            searchIconLabel.Text = "\uE721";
            searchIconLabel.Font = OVIA.Desktop.OviaSystemIconManager.CreateIconFont(10.5F);
            searchIconLabel.ForeColor = OviaFluentTheme.CommonInputIcon;
            searchIconLabel.BackColor = CurrentBackgroundColor;
            searchIconLabel.Cursor = Cursors.IBeam;
            searchIconLabel.Click += delegate { innerTextBox.Focus(); };
            AttachHoverEvents(searchIconLabel);
            Controls.Add(searchIconLabel);

            Resize += delegate { LayoutChildren(); };
            Click += delegate { innerTextBox.Focus(); };
            AttachHoverEvents(this);
            LayoutChildren();
            UpdateChildBackgrounds();
        }

        [Browsable(true)]
        [DefaultValue("검색")]
        public string PlaceholderText
        {
            get { return placeholderLabel.Text; }
            set
            {
                placeholderLabel.Text = value == null ? "" : value;
                UpdatePlaceholder();
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get { return innerTextBox == null ? base.Text : innerTextBox.Text; }
            set
            {
                string safeValue = value == null ? "" : value;
                base.Text = safeValue;

                if (innerTextBox != null)
                {
                    innerTextBox.Text = safeValue;
                }

                UpdatePlaceholder();
            }
        }

        public TextBox InnerTextBox
        {
            get { return innerTextBox; }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (innerTextBox != null)
                {
                    innerTextBox.Font = value;
                }
                if (placeholderLabel != null)
                {
                    placeholderLabel.Font = value;
                }
                LayoutChildren();
            }
        }

        public override bool Focused
        {
            get { return base.Focused || (innerTextBox != null && innerTextBox.Focused); }
        }

        public new bool Focus()
        {
            if (innerTextBox != null)
            {
                return innerTextBox.Focus();
            }

            return base.Focus();
        }

        private Color CurrentBackgroundColor
        {
            get { return (isFocused || isHovered) ? Color.White : OviaFluentTheme.CommonInputBackground; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = CreateRoundRectangle(bounds, OviaFluentTheme.CommonInputRadius))
            using (SolidBrush brush = new SolidBrush(CurrentBackgroundColor))
            using (Pen pen = new Pen(isFocused ? OviaFluentTheme.CommonInputBorderFocus : OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (innerTextBox != null)
            {
                innerTextBox.Focus();
            }
        }

        private void AttachHoverEvents(Control control)
        {
            control.MouseEnter += HoverChanged;
            control.MouseLeave += HoverChanged;
        }

        private void HoverChanged(object sender, EventArgs e)
        {
            bool inside = ClientRectangle.Contains(PointToClient(Control.MousePosition));
            if (isHovered != inside)
            {
                isHovered = inside;
                UpdateChildBackgrounds();
                Invalidate();
            }
        }

        private void FocusChanged(object sender, EventArgs e)
        {
            isFocused = innerTextBox.Focused;
            UpdateChildBackgrounds();
            Invalidate();
        }

        private void InnerTextBox_TextChanged(object sender, EventArgs e)
        {
            UpdatePlaceholder();
            EventHandler handler = TextChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void UpdateChildBackgrounds()
        {
            Color backColor = CurrentBackgroundColor;

            if (innerTextBox != null)
            {
                innerTextBox.BackColor = backColor;
            }
            if (placeholderLabel != null)
            {
                placeholderLabel.BackColor = backColor;
            }
            if (searchIconLabel != null)
            {
                searchIconLabel.BackColor = backColor;
            }
        }

        private void UpdatePlaceholder()
        {
            if (placeholderLabel != null && innerTextBox != null)
            {
                placeholderLabel.Visible = innerTextBox.Text.Trim().Length == 0;
            }
        }

        private void LayoutChildren()
        {
            int iconWidth = 30;
            int left = 12;
            int top = Math.Max(8, (Height - innerTextBox.Height) / 2);

            searchIconLabel.Location = new Point(Math.Max(left, Width - iconWidth - 7), 1);
            searchIconLabel.Size = new Size(iconWidth, Math.Max(1, Height - 2));

            innerTextBox.Location = new Point(left, top);
            innerTextBox.Width = Math.Max(20, Width - left - iconWidth - 15);

            placeholderLabel.Location = new Point(innerTextBox.Left, innerTextBox.Top - 1);
            placeholderLabel.Size = new Size(innerTextBox.Width, innerTextBox.Height + 2);
            UpdatePlaceholder();
            UpdateChildBackgrounds();
        }

        private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// OVIA 공통 셀렉트 박스.
    /// 네이티브 ComboBox의 회색 화살표 버튼을 사용하지 않고, 흰색 배경/공통 테두리/커스텀 화살표/일체형 드롭다운으로 렌더링한다.
    /// </summary>
    public class OviaSelectBox : UserControl
    {
        private readonly Label textLabel;
        private readonly ListBox dropList;
        private readonly Panel dropPanel;
        private readonly ToolStripDropDown dropDown;
        private readonly ToolStripControlHost dropHost;
        private bool isFocused;
        private bool isHovered;
        private bool isDropDownOpen;
        private int selectedIndex = -1;
        private ComboBoxStyle dropDownStyle = ComboBoxStyle.DropDownList;

        public event EventHandler SelectedIndexChanged;

        public OviaSelectBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);

            BackColor = Color.Transparent;
            Size = new Size(150, OviaFluentTheme.CommonInputHeight);
            MinimumSize = new Size(90, OviaFluentTheme.CommonInputHeight);
            TabStop = true;

            textLabel = new Label();
            textLabel.AutoSize = false;
            textLabel.TextAlign = ContentAlignment.MiddleLeft;
            textLabel.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            textLabel.ForeColor = OviaFluentTheme.TextPrimary;
            textLabel.BackColor = Color.White;
            textLabel.Cursor = Cursors.Default;
            textLabel.Click += delegate { ToggleDropDown(); };
            textLabel.MouseEnter += ChildMouseEnter;
            textLabel.MouseLeave += ChildMouseLeave;
            Controls.Add(textLabel);

            dropList = new ListBox();
            dropList.BorderStyle = BorderStyle.None;
            dropList.DrawMode = DrawMode.OwnerDrawFixed;
            dropList.ItemHeight = 28;
            dropList.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            dropList.ForeColor = OviaFluentTheme.TextPrimary;
            dropList.BackColor = Color.White;
            dropList.IntegralHeight = false;
            dropList.MouseMove += DropList_MouseMove;
            dropList.Click += DropList_Click;
            dropList.KeyDown += DropList_KeyDown;
            dropList.DrawItem += DropList_DrawItem;

            dropPanel = new Panel();
            dropPanel.BackColor = Color.White;
            dropPanel.Margin = Padding.Empty;
            dropPanel.Padding = Padding.Empty;
            dropPanel.Paint += DropPanel_Paint;
            dropPanel.Controls.Add(dropList);

            dropHost = new ToolStripControlHost(dropPanel);
            dropHost.Margin = Padding.Empty;
            dropHost.Padding = Padding.Empty;
            dropHost.AutoSize = false;

            dropDown = new ToolStripDropDown();
            dropDown.Padding = Padding.Empty;
            dropDown.Margin = Padding.Empty;
            dropDown.AutoSize = false;
            dropDown.BackColor = Color.White;
            dropDown.DropShadowEnabled = false;
            dropDown.Items.Add(dropHost);
            dropDown.Closed += DropDown_Closed;

            Resize += delegate { LayoutChildren(); };
            Click += delegate { ToggleDropDown(); };
            MouseEnter += ChildMouseEnter;
            MouseLeave += ChildMouseLeave;
            LayoutChildren();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ListBox.ObjectCollection Items
        {
            get { return dropList.Items; }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object SelectedItem
        {
            get
            {
                if (selectedIndex < 0 || selectedIndex >= dropList.Items.Count)
                {
                    return null;
                }

                return dropList.Items[selectedIndex];
            }
            set
            {
                int foundIndex = -1;
                for (int index = 0; index < dropList.Items.Count; index++)
                {
                    object item = dropList.Items[index];
                    if ((item == null && value == null) || (item != null && item.Equals(value)))
                    {
                        foundIndex = index;
                        break;
                    }
                }

                ApplySelectedIndex(foundIndex, true);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set { ApplySelectedIndex(value, true); }
        }

        [Browsable(true)]
        [DefaultValue(ComboBoxStyle.DropDownList)]
        public ComboBoxStyle DropDownStyle
        {
            get { return dropDownStyle; }
            set { dropDownStyle = value; }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (textLabel != null)
                {
                    textLabel.Font = value;
                }
                if (dropList != null)
                {
                    dropList.Font = value;
                }
                LayoutChildren();
            }
        }

        public override bool Focused
        {
            get { return base.Focused || isFocused || isDropDownOpen; }
        }

        public new bool Focus()
        {
            return base.Focus();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            isFocused = true;
            Invalidate();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            if (!isDropDownOpen)
            {
                isFocused = false;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                ShowDropDown();
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = CreateRoundRectangle(bounds, OviaFluentTheme.CommonInputRadius))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen((isFocused || isDropDownOpen) ? OviaFluentTheme.CommonInputBorderFocus : OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            DrawChevron(e.Graphics);
        }

        private void ChildMouseEnter(object sender, EventArgs e)
        {
            if (!isHovered)
            {
                isHovered = true;
                Invalidate();
            }
        }

        private void ChildMouseLeave(object sender, EventArgs e)
        {
            bool inside = ClientRectangle.Contains(PointToClient(Control.MousePosition));
            if (isHovered != inside)
            {
                isHovered = inside;
                Invalidate();
            }
        }

        private void DrawChevron(Graphics graphics)
        {
            int centerX = Width - 17;
            int centerY = Height / 2 + 1;

            using (Pen pen = new Pen(OviaFluentTheme.CommonInputIcon, 1.25F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, centerX - 4, centerY - 2, centerX, centerY + 2);
                graphics.DrawLine(pen, centerX, centerY + 2, centerX + 4, centerY - 2);
            }
        }

        private void ToggleDropDown()
        {
            Focus();

            if (isDropDownOpen)
            {
                dropDown.Close(ToolStripDropDownCloseReason.CloseCalled);
                return;
            }

            ShowDropDown();
        }

        private void ShowDropDown()
        {
            if (dropList.Items.Count == 0)
            {
                return;
            }

            int itemCount = Math.Min(Math.Max(dropList.Items.Count, 1), 12);
            int dropHeight = (itemCount * dropList.ItemHeight) + 2;
            int dropWidth = Math.Max(Width, 90);

            dropPanel.Size = new Size(dropWidth, dropHeight);
            dropList.Location = new Point(1, 1);
            dropList.Size = new Size(Math.Max(1, dropWidth - 2), Math.Max(1, dropHeight - 2));
            dropHost.Size = dropPanel.Size;
            dropDown.Size = dropPanel.Size;
            dropDown.MinimumSize = dropPanel.Size;
            dropDown.MaximumSize = dropPanel.Size;

            if (selectedIndex >= 0 && selectedIndex < dropList.Items.Count)
            {
                dropList.SelectedIndex = selectedIndex;
            }

            isDropDownOpen = true;
            isFocused = true;
            Invalidate();
            dropPanel.Invalidate();
            dropDown.Show(this, new Point(0, Height - 1));
            dropList.Focus();
        }

        private void DropDown_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            isDropDownOpen = false;
            isFocused = ContainsFocus;
            Invalidate();
        }

        private void DropList_MouseMove(object sender, MouseEventArgs e)
        {
            int index = dropList.IndexFromPoint(e.Location);
            if (index >= 0 && index < dropList.Items.Count && dropList.SelectedIndex != index)
            {
                dropList.SelectedIndex = index;
            }
        }

        private void DropList_Click(object sender, EventArgs e)
        {
            if (dropList.SelectedIndex >= 0)
            {
                ApplySelectedIndex(dropList.SelectedIndex, true);
            }

            dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void DropList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                if (dropList.SelectedIndex >= 0)
                {
                    ApplySelectedIndex(dropList.SelectedIndex, true);
                }
                dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
                e.Handled = true;
            }
        }

        private void DropList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color backColor = selected ? OviaFluentTheme.CommonInputItemHover : Color.White;

            using (SolidBrush backBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(backBrush, e.Bounds);
            }

            string text = dropList.Items[e.Index] == null ? "" : dropList.Items[e.Index].ToString();
            Rectangle textBounds = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 20), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, dropList.Font, textBounds, OviaFluentTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void DropPanel_Paint(object sender, PaintEventArgs e)
        {
            Rectangle bounds = new Rectangle(0, 0, dropPanel.Width - 1, dropPanel.Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillRectangle(brush, bounds);
                e.Graphics.DrawRectangle(pen, bounds);
            }
        }

        private void ApplySelectedIndex(int value, bool raiseEvent)
        {
            if (value < -1)
            {
                value = -1;
            }
            if (value >= dropList.Items.Count)
            {
                value = dropList.Items.Count - 1;
            }

            if (selectedIndex == value)
            {
                UpdateSelectedText();
                return;
            }

            selectedIndex = value;
            if (selectedIndex >= 0 && selectedIndex < dropList.Items.Count)
            {
                dropList.SelectedIndex = selectedIndex;
            }

            UpdateSelectedText();

            if (raiseEvent)
            {
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void UpdateSelectedText()
        {
            object selectedItem = SelectedItem;
            textLabel.Text = selectedItem == null ? "" : selectedItem.ToString();
            Invalidate();
        }

        private void LayoutChildren()
        {
            int left = 10;
            int rightArrowWidth = 26;
            textLabel.Location = new Point(left, 1);
            textLabel.Size = new Size(Math.Max(1, Width - left - rightArrowWidth), Math.Max(1, Height - 2));
            textLabel.BackColor = Color.White;
        }

        private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// OVIA 공통 체크박스.
    /// 기본 WinForms CheckBox를 상속하지 않고 직접 렌더링하여 포커스/호버 시 컨트롤 전체 영역에 희미한 테두리가 생기지 않게 한다.
    /// 체크 영역은 약 15px 기준이며 작은 라운드/OVIA 블루 채움/흰색 체크 표시를 사용한다.
    /// </summary>
    public class OviaCheckBox : Control
    {
        private bool isHovered;
        private bool isPressed;
        private bool isChecked;

        private const int BoxSize = 15;
        private const int BoxRadius = 3;

        public event EventHandler CheckedChanged;

        public OviaCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);

            AutoSize = false;
            Size = new Size(132, 24);
            BackColor = Color.Transparent;
            ForeColor = OviaFluentTheme.TextPrimary;
            Font = OviaFluentTheme.FontInput(9.6F, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        [Browsable(true)]
        [DefaultValue(false)]
        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value)
                {
                    return;
                }

                isChecked = value;
                Invalidate();

                EventHandler handler = CheckedChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                isPressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            bool shouldToggle = isPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            isPressed = false;

            if (shouldToggle)
            {
                Checked = !Checked;
            }

            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            // OnMouseUp에서 토글하므로 기본 Click에서는 추가 토글하지 않는다.
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            isPressed = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            PaintParentBackground(pevent.Graphics);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PaintParentBackground(e.Graphics);

            int boxTop = Math.Max(0, (Height - BoxSize) / 2);
            Rectangle boxBounds = new Rectangle(0, boxTop, BoxSize, BoxSize);

            Color borderColor = (isHovered || Focused) ? OviaFluentTheme.CommonInputBorderFocus : OviaFluentTheme.ControlBorder;
            Color fillColor = Checked ? OviaFluentTheme.Accent : Color.White;
            Color currentBorderColor = Checked ? OviaFluentTheme.Accent : borderColor;

            if (isPressed && !Checked)
            {
                fillColor = OviaFluentTheme.NeutralButton;
            }

            using (GraphicsPath boxPath = CreateRoundRectangle(boxBounds, BoxRadius))
            using (SolidBrush boxBrush = new SolidBrush(fillColor))
            using (Pen borderPen = new Pen(currentBorderColor, 1F))
            {
                e.Graphics.FillPath(boxBrush, boxPath);
                e.Graphics.DrawPath(borderPen, boxPath);
            }

            if (Checked)
            {
                using (Pen checkPen = new Pen(Color.White, 1.9F))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    checkPen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLine(checkPen, boxBounds.Left + 3, boxBounds.Top + 8, boxBounds.Left + 6, boxBounds.Top + 11);
                    e.Graphics.DrawLine(checkPen, boxBounds.Left + 6, boxBounds.Top + 11, boxBounds.Left + 12, boxBounds.Top + 4);
                }
            }

            Rectangle textBounds = new Rectangle(BoxSize + 8, 0, Math.Max(1, Width - BoxSize - 8), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            );
        }

        private void PaintParentBackground(Graphics graphics)
        {
            Color background = Color.White;

            if (Parent != null)
            {
                background = Parent.BackColor;

                if (background == Color.Transparent && Parent.Parent != null)
                {
                    background = Parent.Parent.BackColor;
                }
            }
            else if (BackColor != Color.Transparent)
            {
                background = BackColor;
            }

            using (SolidBrush backgroundBrush = new SolidBrush(background))
            {
                graphics.FillRectangle(backgroundBrush, ClientRectangle);
            }
        }

        private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }


    /// <summary>
    /// OVIA 공통 버튼.
    /// 신규/저장/입력은 Primary, 삭제는 Danger, 취소/닫기/기타는 Neutral 규칙으로 사용한다.
    /// </summary>
    public class OviaButton : Button
    {
        private bool isHovered;
        private bool isPressed;
        private OVIA.Desktop.OviaButtonRole role = OVIA.Desktop.OviaButtonRole.Neutral;

        public OviaButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);

            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            Size = new Size(OviaFluentTheme.ButtonMinWidth, OviaFluentTheme.ButtonHeight);
            MinimumSize = new Size(OviaFluentTheme.ButtonMinWidth, OviaFluentTheme.ButtonHeight);
            Font = OviaFluentTheme.FontButton(OviaFluentTheme.ButtonFontSize, FontStyle.Bold);
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Color.Transparent;
            ForeColor = OviaFluentTheme.ButtonNeutralText;
        }

        [Browsable(true)]
        [DefaultValue(OVIA.Desktop.OviaButtonRole.Neutral)]
        public OVIA.Desktop.OviaButtonRole Role
        {
            get { return role; }
            set
            {
                if (role == value)
                {
                    return;
                }

                role = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                isPressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isPressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            isPressed = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (Parent != null)
            {
                using (SolidBrush brush = new SolidBrush(Parent.BackColor))
                {
                    pevent.Graphics.FillRectangle(brush, ClientRectangle);
                }
            }
            else
            {
                base.OnPaintBackground(pevent);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            Color parentBackColor = Parent == null ? OviaFluentTheme.AppBackground : Parent.BackColor;
            if (parentBackColor == Color.Transparent && Parent != null && Parent.Parent != null)
            {
                parentBackColor = Parent.Parent.BackColor;
            }

            using (SolidBrush parentBrush = new SolidBrush(parentBackColor))
            {
                e.Graphics.FillRectangle(parentBrush, ClientRectangle);
            }

            ButtonPalette palette = GetPalette();
            Color backColor = palette.BackColor;
            Color borderColor = palette.BorderColor;
            Color textColor = palette.TextColor;

            if (!Enabled)
            {
                backColor = OviaFluentTheme.NeutralLight;
                borderColor = OviaFluentTheme.ButtonNeutralBorder;
                textColor = OviaFluentTheme.TextMuted;
            }
            else if (isHovered)
            {
                backColor = palette.HoverBackColor;
            }

            if (Enabled && isPressed)
            {
                backColor = palette.DownBackColor;
            }

            RectangleF buttonRect = new RectangleF(0.5F, 0.5F, Math.Max(1F, Width - 1.5F), Math.Max(1F, Height - 1.5F));

            using (GraphicsPath path = CreateRoundRectangle(buttonRect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush brush = new SolidBrush(backColor))
            using (Pen pen = new Pen(borderColor, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            Rectangle textRect = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            );
        }

        private ButtonPalette GetPalette()
        {
            if (role == OVIA.Desktop.OviaButtonRole.Primary)
            {
                return new ButtonPalette(
                    OviaFluentTheme.ButtonPrimaryBack,
                    OviaFluentTheme.ButtonPrimaryBackHover,
                    OviaFluentTheme.AccentHover,
                    OviaFluentTheme.ButtonPrimaryBorder,
                    OviaFluentTheme.ButtonPrimaryText
                );
            }

            if (role == OVIA.Desktop.OviaButtonRole.Danger)
            {
                return new ButtonPalette(
                    OviaFluentTheme.ButtonDangerBack,
                    OviaFluentTheme.ButtonDangerBackHover,
                    OviaFluentTheme.DangerLight,
                    OviaFluentTheme.ButtonDangerBorder,
                    OviaFluentTheme.ButtonDangerText
                );
            }

            return new ButtonPalette(
                OviaFluentTheme.ButtonNeutralBack,
                OviaFluentTheme.ButtonNeutralBackHover,
                OviaFluentTheme.NeutralLight,
                OviaFluentTheme.ButtonNeutralBorder,
                OviaFluentTheme.ButtonNeutralText
            );
        }

        private struct ButtonPalette
        {
            public readonly Color BackColor;
            public readonly Color HoverBackColor;
            public readonly Color DownBackColor;
            public readonly Color BorderColor;
            public readonly Color TextColor;

            public ButtonPalette(Color backColor, Color hoverBackColor, Color downBackColor, Color borderColor, Color textColor)
            {
                BackColor = backColor;
                HoverBackColor = hoverBackColor;
                DownBackColor = downBackColor;
                BorderColor = borderColor;
                TextColor = textColor;
            }
        }

        private static GraphicsPath CreateRoundRectangle(RectangleF rectangle, int radius)
        {
            float diameter = Math.Max(1F, radius * 2F);
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

}
