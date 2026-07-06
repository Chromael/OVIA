using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop.Controls
{
    public static class OviaGridContextMenuFactory
    {
        public static ContextMenuStrip CreateMenu(params ToolStripItem[] items)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            menu.BackColor = Color.White;
            menu.ForeColor = Color.Black;
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(2, 4, 2, 4);
            menu.RenderMode = ToolStripRenderMode.System;

            int i;
            for (i = 0; i < items.Length; i++)
            {
                if (items[i] != null)
                {
                    menu.Items.Add(items[i]);
                }
            }

            return menu;
        }

        public static ToolStripMenuItem CreateItem(string text, EventHandler clickHandler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.AutoSize = false;
            item.Width = 128;
            item.Height = 28;
            item.Padding = new Padding(10, 0, 10, 0);
            item.TextAlign = ContentAlignment.MiddleLeft;

            if (clickHandler != null)
            {
                item.Click += clickHandler;
            }

            return item;
        }

        public static ToolStripSeparator CreateSeparator()
        {
            return new ToolStripSeparator();
        }

        public static void SelectFullRowOnRightClick(DataGridView grid, DataGridViewCellMouseEventArgs e)
        {
            if (grid == null || e == null || e.Button != MouseButtons.Right)
            {
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.RowIndex >= grid.Rows.Count || !grid.Columns[e.ColumnIndex].Visible)
            {
                return;
            }

            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        }
    }
}
