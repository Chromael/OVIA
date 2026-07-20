using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public enum CadShapeEditorMode
    {
        Select,
        AddLine,
        AddText
    }

    public sealed class CadShapeEditorControl : UserControl
    {
        private CadShapeEditDocument document;
        private CadShapeEditDocument originalDocument;
        private readonly Stack<CadShapeEditDocument> undoStack;
        private readonly Stack<CadShapeEditDocument> redoStack;
        private CadShapeEditorMode mode;
        private int selectedIndex;
        private bool snapEnabled;
        private bool isDragging;
        private int dragKind;
        private PointF dragStartWorld;
        private CadShapeEditElement dragStartElement;
        private bool isPanning;
        private Point panStartScreen;
        private PointF panStartOffset;
        private PointF panOffset;
        private bool hasPendingLineStart;
        private PointF pendingLineStart;
        private Point currentMouseScreen;
        private float zoom;
        private bool suppressHistory;
        private bool hasViewBounds;
        private double viewMinX;
        private double viewMinY;
        private double viewMaxX;
        private double viewMaxY;

        public event EventHandler SelectionChanged;
        public event EventHandler DocumentChanged;
        public event EventHandler ModeChanged;
        public event EventHandler TextEditRequested;

        public CadShapeEditorControl()
        {
            document = CadShapeEditDocument.CreateEmpty();
            originalDocument = document.Clone();
            undoStack = new Stack<CadShapeEditDocument>();
            redoStack = new Stack<CadShapeEditDocument>();
            mode = CadShapeEditorMode.Select;
            selectedIndex = -1;
            snapEnabled = true;
            zoom = 1F;
            panOffset = PointF.Empty;
            hasViewBounds = false;
            DoubleBuffered = true;
            BackColor = Color.White;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        }

        public CadShapeEditDocument Document
        {
            get { return document; }
        }

        public CadShapeEditElement SelectedElement
        {
            get
            {
                if (document == null || selectedIndex < 0 || selectedIndex >= document.Elements.Count)
                {
                    return null;
                }

                return document.Elements[selectedIndex];
            }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
        }

        public CadShapeEditorMode Mode
        {
            get { return mode; }
            set
            {
                if (mode == value)
                {
                    return;
                }

                mode = value;
                hasPendingLineStart = false;
                isDragging = false;
                Invalidate();
                OnModeChanged();
            }
        }

        public bool SnapEnabled
        {
            get { return snapEnabled; }
            set
            {
                snapEnabled = value;
                Invalidate();
            }
        }

        public bool CanUndo
        {
            get { return undoStack.Count > 0; }
        }

        public bool CanRedo
        {
            get { return redoStack.Count > 0; }
        }

        public float Zoom
        {
            get { return zoom; }
        }

        public void LoadDocument(CadShapeEditDocument source, CadShapeEditDocument original)
        {
            document = source == null ? CadShapeEditDocument.CreateEmpty() : source.Clone();
            originalDocument = original == null ? document.Clone() : original.Clone();
            document.EnsureTextIds();
            originalDocument.EnsureTextIds();
            undoStack.Clear();
            redoStack.Clear();
            selectedIndex = -1;
            zoom = 1F;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            hasPendingLineStart = false;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void FitToScreen()
        {
            zoom = 1F;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            Invalidate();
        }

        public void ZoomIn()
        {
            SetZoom(zoom * 1.2F, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void ZoomOut()
        {
            SetZoom(zoom / 1.2F, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            redoStack.Push(document.Clone());
            document = undoStack.Pop();
            selectedIndex = -1;
            hasPendingLineStart = false;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void Redo()
        {
            if (redoStack.Count == 0)
            {
                return;
            }

            undoStack.Push(document.Clone());
            document = redoStack.Pop();
            selectedIndex = -1;
            hasPendingLineStart = false;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void RestoreOriginal()
        {
            if (originalDocument == null)
            {
                return;
            }

            PushUndo();
            document = originalDocument.Clone();
            selectedIndex = -1;
            hasPendingLineStart = false;
            zoom = 1F;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void DeleteSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= document.Elements.Count)
            {
                return;
            }

            PushUndo();
            document.Elements.RemoveAt(selectedIndex);
            selectedIndex = -1;
            document.EnsureTextIds();
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void SplitSelectedLine()
        {
            CadShapeEditElement selected = SelectedElement;

            if (selected == null || selected.Type != "LINE")
            {
                return;
            }

            double middleX = (selected.X1 + selected.X2) / 2D;
            double middleY = (selected.Y1 + selected.Y2) / 2D;

            if (Distance(new PointF((float)selected.X1, (float)selected.Y1), new PointF((float)selected.X2, (float)selected.Y2)) < 0.2F)
            {
                return;
            }

            PushUndo();
            CadShapeEditElement second = selected.Clone();
            second.X1 = middleX;
            second.Y1 = middleY;
            selected.X2 = middleX;
            selected.Y2 = middleY;
            document.Elements.Insert(selectedIndex + 1, second);
            selectedIndex++;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void SetSelectedText(string value)
        {
            CadShapeEditElement selected = SelectedElement;

            if (selected == null || selected.Type != "TEXT")
            {
                return;
            }

            string safe = value == null ? "" : value;

            if (String.Equals(selected.Text, safe, StringComparison.Ordinal))
            {
                return;
            }

            PushUndo();
            selected.Text = safe;
            selected.HasBounds = false;
            Invalidate();
            OnDocumentChanged();
        }

        public void SetTextValue(string textId, string value)
        {
            if (textId == null || textId.Trim() == "")
            {
                return;
            }

            List<CadShapeEditElement> texts = document.GetTextElements();
            int i;

            for (i = 0; i < texts.Count; i++)
            {
                if (texts[i].TextId.Equals(textId, StringComparison.OrdinalIgnoreCase))
                {
                    string safe = value == null ? "" : value;

                    if (!String.Equals(texts[i].Text, safe, StringComparison.Ordinal))
                    {
                        PushUndo();
                        texts[i].Text = safe;
                        texts[i].HasBounds = false;
                        Invalidate();
                        OnDocumentChanged();
                    }

                    return;
                }
            }
        }

        public void SetSelectedRotation(double degrees)
        {
            CadShapeEditElement selected = SelectedElement;

            if (selected == null || selected.Type != "TEXT")
            {
                return;
            }

            if (Math.Abs(selected.Rotation - degrees) <= 0.001D)
            {
                return;
            }

            PushUndo();
            selected.Rotation = degrees;
            selected.HasBounds = false;
            Invalidate();
            OnDocumentChanged();
        }

        public void AlignSelectedHorizontal()
        {
            CadShapeEditElement selected = SelectedElement;

            if (selected == null || selected.Type != "LINE")
            {
                return;
            }

            PushUndo();
            selected.Y2 = selected.Y1;
            Invalidate();
            OnDocumentChanged();
        }

        public void AlignSelectedVertical()
        {
            CadShapeEditElement selected = SelectedElement;

            if (selected == null || selected.Type != "LINE")
            {
                return;
            }

            PushUndo();
            selected.X2 = selected.X1;
            Invalidate();
            OnDocumentChanged();
        }

        public void SelectTextElement(string textId)
        {
            if (textId == null || textId.Trim() == "")
            {
                return;
            }

            int i;

            for (i = 0; i < document.Elements.Count; i++)
            {
                CadShapeEditElement element = document.Elements[i];

                if (element != null && element.Type == "TEXT" && element.TextId.Equals(textId, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedIndex(i);
                    Mode = CadShapeEditorMode.Select;
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            DrawBackgroundGrid(g);
            DrawElements(g);
            DrawPendingLine(g);
            DrawOverlay(g);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            currentMouseScreen = e.Location;

            if (e.Button == MouseButtons.Middle)
            {
                isPanning = true;
                panStartScreen = e.Location;
                panStartOffset = panOffset;
                Cursor = Cursors.Hand;
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                hasPendingLineStart = false;

                if (mode == CadShapeEditorMode.AddLine)
                {
                    Mode = CadShapeEditorMode.Select;
                }
                else
                {
                    Invalidate();
                }

                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            PointF world = ScreenToWorld(e.Location);

            if (mode == CadShapeEditorMode.AddLine)
            {
                HandleAddLineClick(world);
                return;
            }

            if (mode == CadShapeEditorMode.AddText)
            {
                AddTextAt(world);
                return;
            }

            int hitIndex;
            int hitPart;
            HitTest(e.Location, out hitIndex, out hitPart);
            SetSelectedIndex(hitIndex);

            if (hitIndex >= 0)
            {
                isDragging = true;
                dragKind = hitPart;
                dragStartWorld = world;
                dragStartElement = document.Elements[hitIndex].Clone();
                PushUndo();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button != MouseButtons.Left || mode != CadShapeEditorMode.Select)
            {
                return;
            }

            int hitIndex;
            int hitPart;
            HitTest(e.Location, out hitIndex, out hitPart);

            if (hitIndex < 0 || hitIndex >= document.Elements.Count)
            {
                return;
            }

            CadShapeEditElement hit = document.Elements[hitIndex];

            if (hit == null || hit.Type != "TEXT")
            {
                return;
            }

            isDragging = false;
            dragStartElement = null;
            dragKind = 0;
            SetSelectedIndex(hitIndex);
            OnTextEditRequested();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            currentMouseScreen = e.Location;

            if (isPanning)
            {
                panOffset = new PointF(
                    panStartOffset.X + e.X - panStartScreen.X,
                    panStartOffset.Y + e.Y - panStartScreen.Y
                );
                Invalidate();
                return;
            }

            if (isDragging && SelectedElement != null && dragStartElement != null)
            {
                PointF world = ScreenToWorld(e.Location);
                double dx = world.X - dragStartWorld.X;
                double dy = world.Y - dragStartWorld.Y;
                CadShapeEditElement selected = SelectedElement;

                if (selected.Type == "LINE")
                {
                    if (dragKind == 1)
                    {
                        PointF snapped = ApplySnap(new PointF((float)(dragStartElement.X1 + dx), (float)(dragStartElement.Y1 + dy)), new PointF((float)dragStartElement.X2, (float)dragStartElement.Y2));
                        snapped = SnapToExistingLineEndpoint(snapped, selectedIndex);
                        selected.X1 = snapped.X;
                        selected.Y1 = snapped.Y;
                        selected.X2 = dragStartElement.X2;
                        selected.Y2 = dragStartElement.Y2;
                    }
                    else if (dragKind == 2)
                    {
                        PointF snapped = ApplySnap(new PointF((float)(dragStartElement.X2 + dx), (float)(dragStartElement.Y2 + dy)), new PointF((float)dragStartElement.X1, (float)dragStartElement.Y1));
                        snapped = SnapToExistingLineEndpoint(snapped, selectedIndex);
                        selected.X1 = dragStartElement.X1;
                        selected.Y1 = dragStartElement.Y1;
                        selected.X2 = snapped.X;
                        selected.Y2 = snapped.Y;
                    }
                    else
                    {
                        selected.X1 = dragStartElement.X1 + dx;
                        selected.Y1 = dragStartElement.Y1 + dy;
                        selected.X2 = dragStartElement.X2 + dx;
                        selected.Y2 = dragStartElement.Y2 + dy;
                    }
                }
                else if (selected.Type == "TEXT")
                {
                    selected.X1 = dragStartElement.X1 + dx;
                    selected.Y1 = dragStartElement.Y1 + dy;
                    selected.HasBounds = false;
                }
                else if (selected.Type == "ARC" || selected.Type == "CIRCLE")
                {
                    selected.CX = dragStartElement.CX + dx;
                    selected.CY = dragStartElement.CY + dy;
                }

                Invalidate();
                OnDocumentChanged();
                return;
            }

            int hoverIndex;
            int hoverPart;
            HitTest(e.Location, out hoverIndex, out hoverPart);
            Cursor = hoverIndex >= 0 ? Cursors.SizeAll : Cursors.Cross;

            if (mode == CadShapeEditorMode.Select && hoverIndex < 0)
            {
                Cursor = Cursors.Default;
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Middle)
            {
                isPanning = false;
                Cursor = Cursors.Default;
            }

            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                dragStartElement = null;
                dragKind = 0;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float factor = e.Delta > 0 ? 1.15F : 1F / 1.15F;
            SetZoom(zoom * factor, e.Location);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Y)
            {
                Redo();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                hasPendingLineStart = false;
                Mode = CadShapeEditorMode.Select;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && mode == CadShapeEditorMode.AddLine)
            {
                hasPendingLineStart = false;
                Mode = CadShapeEditorMode.Select;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if ((e.KeyCode == Keys.F2 || e.KeyCode == Keys.Enter)
                && mode == CadShapeEditorMode.Select
                && SelectedElement != null
                && SelectedElement.Type == "TEXT")
            {
                OnTextEditRequested();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void HandleAddLineClick(PointF world)
        {
            if (!hasPendingLineStart)
            {
                pendingLineStart = SnapToExistingLineEndpoint(world, -1);
                hasPendingLineStart = true;
                Invalidate();
                return;
            }

            PointF end = ApplySnap(world, pendingLineStart);
            end = SnapToExistingLineEndpoint(end, -1);

            if (Distance(pendingLineStart, end) < 0.1F)
            {
                return;
            }

            PushUndo();
            CadShapeEditElement line = new CadShapeEditElement();
            line.Type = "LINE";
            line.X1 = pendingLineStart.X;
            line.Y1 = pendingLineStart.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            document.Elements.Add(line);
            selectedIndex = document.Elements.Count - 1;
            pendingLineStart = end;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        private void AddTextAt(PointF world)
        {
            PushUndo();
            CadShapeEditElement text = new CadShapeEditElement();
            text.Type = "TEXT";
            text.Text = "값";
            text.X1 = world.X;
            text.Y1 = world.Y;
            text.Height = 3D;
            text.Rotation = 0D;
            document.Elements.Add(text);
            document.EnsureTextIds();
            selectedIndex = document.Elements.Count - 1;
            Mode = CadShapeEditorMode.Select;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        private PointF ApplySnap(PointF movingPoint, PointF fixedPoint)
        {
            if (!snapEnabled)
            {
                return movingPoint;
            }

            double dx = movingPoint.X - fixedPoint.X;
            double dy = movingPoint.Y - fixedPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= 0.0001D)
            {
                return movingPoint;
            }

            double angle = Math.Atan2(dy, dx);
            double snapStep = Math.PI / 12D;
            double snappedAngle = Math.Round(angle / snapStep) * snapStep;
            return new PointF(
                (float)(fixedPoint.X + Math.Cos(snappedAngle) * length),
                (float)(fixedPoint.Y + Math.Sin(snappedAngle) * length)
            );
        }

        private PointF SnapToExistingLineEndpoint(PointF candidateWorld, int ignoredElementIndex)
        {
            if (!snapEnabled || document == null || document.Elements == null)
            {
                return candidateWorld;
            }

            PointF candidateScreen = WorldToScreen(candidateWorld);
            float bestDistance = 10F;
            PointF bestWorld = candidateWorld;
            int i;

            for (i = 0; i < document.Elements.Count; i++)
            {
                if (i == ignoredElementIndex)
                {
                    continue;
                }

                CadShapeEditElement element = document.Elements[i];

                if (element == null || element.Type != "LINE")
                {
                    continue;
                }

                PointF firstWorld = new PointF((float)element.X1, (float)element.Y1);
                PointF secondWorld = new PointF((float)element.X2, (float)element.Y2);
                float firstDistance = Distance(candidateScreen, WorldToScreen(firstWorld));
                float secondDistance = Distance(candidateScreen, WorldToScreen(secondWorld));

                if (firstDistance < bestDistance)
                {
                    bestDistance = firstDistance;
                    bestWorld = firstWorld;
                }

                if (secondDistance < bestDistance)
                {
                    bestDistance = secondDistance;
                    bestWorld = secondWorld;
                }
            }

            return bestWorld;
        }

        private void SetZoom(float newZoom, Point anchorScreen)
        {
            if (newZoom < 0.25F) newZoom = 0.25F;
            if (newZoom > 8F) newZoom = 8F;

            PointF anchorWorld = ScreenToWorld(anchorScreen);
            zoom = newZoom;
            PointF anchorAfter = WorldToScreen(anchorWorld);
            panOffset = new PointF(
                panOffset.X + anchorScreen.X - anchorAfter.X,
                panOffset.Y + anchorScreen.Y - anchorAfter.Y
            );
            Invalidate();
        }

        private void HitTest(Point screenPoint, out int hitIndex, out int hitPart)
        {
            hitIndex = -1;
            hitPart = 0;
            float threshold = 8F;
            int i;

            for (i = document.Elements.Count - 1; i >= 0; i--)
            {
                CadShapeEditElement element = document.Elements[i];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE")
                {
                    PointF p1 = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
                    PointF p2 = WorldToScreen(new PointF((float)element.X2, (float)element.Y2));

                    if (Distance(p1, screenPoint) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 1;
                        return;
                    }

                    if (Distance(p2, screenPoint) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 2;
                        return;
                    }

                    if (DistancePointToSegment(screenPoint, p1, p2) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 3;
                        return;
                    }
                }
                else if (element.Type == "TEXT")
                {
                    PointF center = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));

                    using (Font hitFont = OviaFluentTheme.FontKorean(9F, FontStyle.Regular))
                    {
                        Size size = TextRenderer.MeasureText(element.Text == null ? "" : element.Text, hitFont);
                        RectangleF rect = new RectangleF(center.X - size.Width / 2F - 5F, center.Y - size.Height / 2F - 4F, size.Width + 10F, size.Height + 8F);

                        if (rect.Contains(screenPoint))
                        {
                            hitIndex = i;
                            hitPart = 3;
                            return;
                        }
                    }
                }
                else if (element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
                    float radius = (float)(element.Radius * GetTransform().Scale);
                    float distance = Distance(center, screenPoint);

                    if (Math.Abs(distance - radius) <= threshold || distance <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 3;
                        return;
                    }
                }
            }
        }

        private void SetSelectedIndex(int value)
        {
            if (value < -1 || value >= document.Elements.Count)
            {
                value = -1;
            }

            if (selectedIndex == value)
            {
                return;
            }

            selectedIndex = value;
            Invalidate();
            OnSelectionChanged();
        }

        private void PushUndo()
        {
            if (suppressHistory)
            {
                return;
            }

            undoStack.Push(document.Clone());

            while (undoStack.Count > 60)
            {
                CadShapeEditDocument[] items = undoStack.ToArray();
                undoStack.Clear();
                int i;

                for (i = Math.Min(items.Length - 1, 58); i >= 0; i--)
                {
                    undoStack.Push(items[i]);
                }
            }

            redoStack.Clear();
        }

        private void DrawBackgroundGrid(Graphics g)
        {
            g.Clear(Color.FromArgb(250, 251, 253));
            EditorTransform transform = GetTransform();
            double worldWidth = Math.Max(transform.MaxX - transform.MinX, 100D);
            double step = GetGridStep(worldWidth);

            using (Pen minorPen = new Pen(Color.FromArgb(235, 238, 243), 1F))
            using (Pen axisPen = new Pen(Color.FromArgb(220, 225, 232), 1F))
            {
                double startX = Math.Floor((transform.MinX - 1000D) / step) * step;
                double endX = transform.MaxX + 1000D;
                int guard = 0;

                for (double x = startX; x <= endX && guard < 500; x += step, guard++)
                {
                    PointF p1 = WorldToScreen(new PointF((float)x, (float)(transform.MinY - 1000D)));
                    PointF p2 = WorldToScreen(new PointF((float)x, (float)(transform.MaxY + 1000D)));
                    g.DrawLine(Math.Abs(x) < step * 0.1D ? axisPen : minorPen, p1, p2);
                }

                double startY = Math.Floor((transform.MinY - 1000D) / step) * step;
                double endY = transform.MaxY + 1000D;
                guard = 0;

                for (double y = startY; y <= endY && guard < 500; y += step, guard++)
                {
                    PointF p1 = WorldToScreen(new PointF((float)(transform.MinX - 1000D), (float)y));
                    PointF p2 = WorldToScreen(new PointF((float)(transform.MaxX + 1000D), (float)y));
                    g.DrawLine(Math.Abs(y) < step * 0.1D ? axisPen : minorPen, p1, p2);
                }
            }
        }

        private void DrawElements(Graphics g)
        {
            int i;

            for (i = 0; i < document.Elements.Count; i++)
            {
                CadShapeEditElement element = document.Elements[i];
                bool selected = i == selectedIndex;

                if (element == null)
                {
                    continue;
                }

                Color lineColor = selected ? Color.FromArgb(19, 104, 206) : Color.FromArgb(12, 17, 28);
                float width = selected ? 2.3F : 1.55F;

                using (Pen pen = new Pen(lineColor, width))
                using (SolidBrush brush = new SolidBrush(lineColor))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    if (element.Type == "LINE")
                    {
                        PointF p1 = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
                        PointF p2 = WorldToScreen(new PointF((float)element.X2, (float)element.Y2));
                        g.DrawLine(pen, p1, p2);

                        if (selected)
                        {
                            DrawHandle(g, p1);
                            DrawHandle(g, p2);
                        }
                    }
                    else if (element.Type == "CIRCLE")
                    {
                        DrawArcOrCircle(g, pen, element, true);
                    }
                    else if (element.Type == "ARC")
                    {
                        DrawArcOrCircle(g, pen, element, false);
                    }
                    else if (element.Type == "TEXT")
                    {
                        DrawTextElement(g, brush, element, selected);
                    }
                }
            }
        }

        private void DrawArcOrCircle(Graphics g, Pen pen, CadShapeEditElement element, bool circle)
        {
            EditorTransform transform = GetTransform();
            PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
            float radius = (float)(Math.Abs(element.Radius) * transform.Scale);
            RectangleF bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2F, radius * 2F);

            if (circle)
            {
                g.DrawEllipse(pen, bounds);
            }
            else
            {
                float start = (float)(-element.StartAngle);
                float sweep = (float)(-(element.EndAngle - element.StartAngle));

                if (Math.Abs(sweep) < 0.1F)
                {
                    sweep = 360F;
                }

                g.DrawArc(pen, bounds, start, sweep);
            }
        }

        private void DrawTextElement(Graphics g, Brush brush, CadShapeEditElement element, bool selected)
        {
            PointF center = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
            string text = element.Text == null ? "" : element.Text;
            float fontSize = Math.Max(8F, Math.Min(14F, 8F + (zoom - 1F) * 1.3F));

            using (Font font = OviaFluentTheme.FontKorean(fontSize, FontStyle.Regular, GraphicsUnit.Point))
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(center.X, center.Y);
                g.RotateTransform((float)element.Rotation);
                SizeF size = g.MeasureString(text, font);

                if (selected)
                {
                    using (SolidBrush selectionBrush = new SolidBrush(Color.FromArgb(40, 19, 104, 206)))
                    using (Pen selectionPen = new Pen(Color.FromArgb(19, 104, 206), 1F))
                    {
                        RectangleF box = new RectangleF(-size.Width / 2F - 5F, -size.Height / 2F - 3F, size.Width + 10F, size.Height + 6F);
                        g.FillRectangle(selectionBrush, box);
                        g.DrawRectangle(selectionPen, box.X, box.Y, box.Width, box.Height);
                    }
                }

                g.DrawString(text, font, brush, -size.Width / 2F, -size.Height / 2F);
                g.Restore(state);
            }
        }

        private void DrawPendingLine(Graphics g)
        {
            if (!hasPendingLineStart || mode != CadShapeEditorMode.AddLine)
            {
                return;
            }

            PointF start = WorldToScreen(pendingLineStart);
            PointF currentWorld = ScreenToWorld(currentMouseScreen);
            PointF snapped = ApplySnap(currentWorld, pendingLineStart);
            PointF end = WorldToScreen(snapped);

            using (Pen pen = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, start, end);
            }

            DrawHandle(g, start);
        }

        private void DrawOverlay(Graphics g)
        {
            string modeText = mode == CadShapeEditorMode.Select
                ? "선택·이동"
                : mode == CadShapeEditorMode.AddLine ? "연속 선 그리기" : "문자 추가";
            string guide = mode == CadShapeEditorMode.AddLine
                ? "클릭하여 꺾임점을 이어서 그립니다. Enter·Esc·우클릭으로 종료합니다."
                : mode == CadShapeEditorMode.AddText
                    ? "문자를 놓을 위치를 클릭한 뒤 우측 속성에서 값을 수정합니다."
                    : "요소를 선택해 이동하거나 끝점을 끌어 수정합니다. 마우스 휠 확대·축소, 가운데 버튼 이동.";

            using (Font titleFont = OviaFluentTheme.FontKorean(9F, FontStyle.Bold))
            using (Font guideFont = OviaFluentTheme.FontKorean(8F, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(55, 65, 81)))
            using (SolidBrush guideBrush = new SolidBrush(Color.FromArgb(100, 110, 125)))
            {
                g.DrawString(modeText + "  |  " + Math.Round(zoom * 100F).ToString("0") + "%", titleFont, titleBrush, 12F, 10F);
                g.DrawString(guide, guideFont, guideBrush, 12F, 32F);
            }
        }

        private void DrawHandle(Graphics g, PointF center)
        {
            RectangleF rect = new RectangleF(center.X - 4F, center.Y - 4F, 8F, 8F);

            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                g.FillRectangle(brush, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private PointF WorldToScreen(PointF world)
        {
            EditorTransform transform = GetTransform();
            return new PointF(
                transform.OffsetX + (float)((world.X - transform.MinX) * transform.Scale),
                transform.OffsetY + (float)((world.Y - transform.MinY) * transform.Scale)
            );
        }

        private PointF ScreenToWorld(Point screen)
        {
            EditorTransform transform = GetTransform();
            return new PointF(
                (float)(transform.MinX + (screen.X - transform.OffsetX) / Math.Max(transform.Scale, 0.0001D)),
                (float)(transform.MinY + (screen.Y - transform.OffsetY) / Math.Max(transform.Scale, 0.0001D))
            );
        }

        private EditorTransform GetTransform()
        {
            if (!hasViewBounds)
            {
                ResetViewBoundsFromDocument();
            }

            double minX = viewMinX;
            double minY = viewMinY;
            double maxX = viewMaxX;
            double maxY = viewMaxY;
            double width = Math.Max(maxX - minX, 10D);
            double height = Math.Max(maxY - minY, 10D);
            float paddingLeft = 70F;
            float paddingRight = 50F;
            float paddingTop = 70F;
            float paddingBottom = 50F;
            double availableWidth = Math.Max(ClientSize.Width - paddingLeft - paddingRight, 10F);
            double availableHeight = Math.Max(ClientSize.Height - paddingTop - paddingBottom, 10F);
            double fitScale = Math.Min(availableWidth / width, availableHeight / height);
            double scale = Math.Max(fitScale * zoom, 0.0001D);
            double contentWidth = width * scale;
            double contentHeight = height * scale;

            EditorTransform transform = new EditorTransform();
            transform.MinX = minX;
            transform.MinY = minY;
            transform.MaxX = maxX;
            transform.MaxY = maxY;
            transform.Scale = scale;
            transform.OffsetX = paddingLeft + (float)((availableWidth - contentWidth) / 2D) + panOffset.X;
            transform.OffsetY = paddingTop + (float)((availableHeight - contentHeight) / 2D) + panOffset.Y;
            return transform;
        }

        private void ResetViewBoundsFromDocument()
        {
            double minX;
            double minY;
            double maxX;
            double maxY;

            if (document == null)
            {
                minX = 0D;
                minY = 0D;
                maxX = 160D;
                maxY = 80D;
            }
            else
            {
                document.TryGetBounds(out minX, out minY, out maxX, out maxY);
            }

            double width = Math.Max(maxX - minX, 20D);
            double height = Math.Max(maxY - minY, 20D);
            double marginX = Math.Max(width * 0.10D, 6D);
            double marginY = Math.Max(height * 0.14D, 6D);
            viewMinX = minX - marginX;
            viewMinY = minY - marginY;
            viewMaxX = maxX + marginX;
            viewMaxY = maxY + marginY;
            hasViewBounds = true;
        }

        private double GetGridStep(double worldWidth)
        {
            double rough = Math.Max(worldWidth / 16D, 1D);
            double exponent = Math.Pow(10D, Math.Floor(Math.Log10(rough)));
            double normalized = rough / exponent;

            if (normalized <= 1D) return exponent;
            if (normalized <= 2D) return exponent * 2D;
            if (normalized <= 5D) return exponent * 5D;
            return exponent * 10D;
        }

        private float Distance(PointF a, Point b)
        {
            return Distance(a, new PointF(b.X, b.Y));
        }

        private float Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private float DistancePointToSegment(Point point, PointF a, PointF b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double denominator = dx * dx + dy * dy;

            if (denominator <= 0.00001D)
            {
                return Distance(a, point);
            }

            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / denominator;
            if (t < 0D) t = 0D;
            if (t > 1D) t = 1D;
            PointF projected = new PointF((float)(a.X + dx * t), (float)(a.Y + dy * t));
            return Distance(projected, point);
        }

        private void OnSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnDocumentChanged()
        {
            EventHandler handler = DocumentChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnModeChanged()
        {
            EventHandler handler = ModeChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnTextEditRequested()
        {
            EventHandler handler = TextEditRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private sealed class EditorTransform
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
            public double Scale;
            public float OffsetX;
            public float OffsetY;
        }
    }
}
