using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal static class OviaContentLayoutHelper
    {
        public const int WorkspaceMenuBottom = 48;
        public const int FixedAreaGap = 12;
        public const int FixedAreaMaxHeight = 50;
        public const int LeftMargin = 25;
        public const int RightMargin = 25;

        public static void LayoutContentFrame(Form owner, Panel fixedPanel, Control contentFrame, bool hasFixedArea, int preferredFixedHeight)
        {
            if (owner == null || contentFrame == null)
            {
                return;
            }

            int width = Math.Max(1, owner.ClientSize.Width);
            int height = Math.Max(1, owner.ClientSize.Height);
            int contentTop = WorkspaceMenuBottom;

            if (hasFixedArea && fixedPanel != null)
            {
                int fixedHeight = Math.Max(1, Math.Min(FixedAreaMaxHeight, preferredFixedHeight <= 0 ? OviaFluentTheme.ButtonHeight : preferredFixedHeight));
                int fixedTop = WorkspaceMenuBottom + FixedAreaGap;

                fixedPanel.SuspendLayout();
                fixedPanel.Location = new Point(0, fixedTop);
                fixedPanel.Size = new Size(width, fixedHeight);
                fixedPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                fixedPanel.Margin = Padding.Empty;
                fixedPanel.Padding = Padding.Empty;
                fixedPanel.Visible = true;
                fixedPanel.ResumeLayout(false);
                fixedPanel.BringToFront();

                contentTop = fixedTop + fixedHeight + FixedAreaGap;
            }
            else if (fixedPanel != null)
            {
                fixedPanel.Visible = false;
                fixedPanel.Size = new Size(width, 0);
            }

            if (contentTop >= height)
            {
                contentTop = Math.Max(WorkspaceMenuBottom, height - 1);
            }

            contentFrame.SuspendLayout();
            contentFrame.Location = new Point(0, contentTop);
            contentFrame.Size = new Size(width, Math.Max(1, height - contentTop));
            contentFrame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            contentFrame.Margin = Padding.Empty;
            contentFrame.Padding = Padding.Empty;
            contentFrame.ResumeLayout(false);
        }

        public static void ConfigureGridContentFrame(Panel frame, DataGridView grid, bool resetScrollToTop)
        {
            if (frame == null || grid == null)
            {
                return;
            }

            frame.SuspendLayout();
            if (resetScrollToTop)
            {
                ResetAutoScroll(frame);
            }

            frame.AutoScroll = true;
            frame.AutoScrollMargin = Size.Empty;
            frame.Padding = Padding.Empty;
            frame.Margin = Padding.Empty;
            frame.BackColor = OviaFluentTheme.AppBackground;

            grid.SuspendLayout();
            grid.Dock = DockStyle.None;
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            grid.Location = Point.Empty;
            grid.Margin = Padding.Empty;
            grid.ScrollBars = ScrollBars.None;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            int frameWidth = Math.Max(1, frame.ClientSize.Width);
            int frameHeight = Math.Max(1, frame.ClientSize.Height);
            int rowsHeight = GetGridRowsHeight(grid);
            bool needsVertical = rowsHeight > frameHeight;
            int availableWidth = Math.Max(1, frameWidth - (needsVertical ? SystemInformation.VerticalScrollBarWidth : 0));
            int gridWidth = FitGridColumns(grid, availableWidth);
            bool needsHorizontal = gridWidth > availableWidth;
            int availableHeight = Math.Max(1, frameHeight - (needsHorizontal ? SystemInformation.HorizontalScrollBarHeight : 0));
            int gridHeight = Math.Max(availableHeight, rowsHeight);

            grid.Size = new Size(Math.Max(1, gridWidth), Math.Max(1, gridHeight));
            grid.ResumeLayout(false);

            frame.AutoScrollMinSize = new Size(grid.Width, grid.Height);
            if (resetScrollToTop)
            {
                ResetAutoScroll(frame);
            }
            frame.ResumeLayout(false);
        }

        public static void ConfigureScrollableContentFrame(Panel frame, int requiredHeight, bool resetScrollToTop)
        {
            if (frame == null)
            {
                return;
            }

            frame.SuspendLayout();
            if (resetScrollToTop)
            {
                ResetAutoScroll(frame);
            }

            frame.AutoScroll = true;
            frame.AutoScrollMargin = Size.Empty;
            frame.Padding = Padding.Empty;
            frame.Margin = Padding.Empty;
            frame.HorizontalScroll.Enabled = false;
            frame.HorizontalScroll.Visible = false;
            frame.AutoScrollMinSize = new Size(0, Math.Max(1, requiredHeight));

            if (resetScrollToTop)
            {
                ResetAutoScroll(frame);
            }
            frame.ResumeLayout(false);
        }

        public static void HideRemovedStatus(Label label, Form owner)
        {
            if (label == null || owner == null)
            {
                return;
            }

            label.Visible = false;
            label.Location = new Point(0, Math.Max(1, owner.ClientSize.Height));
            label.Size = new Size(Math.Max(1, owner.ClientSize.Width), 0);
            label.Margin = Padding.Empty;
            label.Padding = Padding.Empty;
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        }

        public static void ResetAutoScroll(ScrollableControl control)
        {
            if (control == null)
            {
                return;
            }

            try
            {
                control.AutoScrollPosition = Point.Empty;
            }
            catch
            {
            }
        }

        private static int GetGridRowsHeight(DataGridView grid)
        {
            int height = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
            try
            {
                height += grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible);
            }
            catch
            {
                int i;
                for (i = 0; i < grid.Rows.Count; i++)
                {
                    if (grid.Rows[i].Visible)
                    {
                        height += grid.Rows[i].Height;
                    }
                }
            }

            return Math.Max(1, height + 2);
        }

        private static int FitGridColumns(DataGridView grid, int availableWidth)
        {
            int totalBaseWidth = 0;
            int visibleCount = 0;
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                if (column == null || !column.Visible)
                {
                    continue;
                }

                totalBaseWidth += GetColumnBaseWidth(column);
                visibleCount++;
            }

            if (visibleCount == 0 || totalBaseWidth <= 0)
            {
                return Math.Max(1, availableWidth);
            }

            int targetWidth = Math.Max(availableWidth, totalBaseWidth);
            int extraWidth = Math.Max(0, targetWidth - totalBaseWidth);
            int remainingExtra = extraWidth;
            int actualWidth = 0;
            DataGridViewColumn lastVisibleColumn = null;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                if (column == null || !column.Visible)
                {
                    continue;
                }

                int baseWidth = GetColumnBaseWidth(column);
                int addWidth = 0;
                if (extraWidth > 0)
                {
                    addWidth = (int)Math.Floor((double)extraWidth * (double)baseWidth / (double)totalBaseWidth);
                    remainingExtra -= addWidth;
                }

                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = Math.Max(column.MinimumWidth, baseWidth + addWidth);
                actualWidth += column.Width;
                lastVisibleColumn = column;
            }

            if (extraWidth > 0 && remainingExtra > 0 && lastVisibleColumn != null)
            {
                lastVisibleColumn.Width += remainingExtra;
                actualWidth += remainingExtra;
            }

            return Math.Max(1, actualWidth + 2);
        }

        private static int GetColumnBaseWidth(DataGridViewColumn column)
        {
            object tag = column.Tag;
            if (tag is int)
            {
                return Math.Max(column.MinimumWidth, (int)tag);
            }

            int width = Math.Max(column.MinimumWidth, column.Width);
            column.Tag = width;
            return width;
        }
    }
}
