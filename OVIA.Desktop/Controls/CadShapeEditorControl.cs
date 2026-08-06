using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public enum CadShapeEditorMode
    {
        Select,
        AddLine,
        AddCircle,
        AddAngle,
        AddText
    }

    public sealed class CadShapeEditorControl : UserControl
    {
        private const float DefaultFitZoom = 0.50F;
        private const float MinZoom = 0.18F;
        private const float MaxZoom = 8F;
        private const float EndpointSnapThreshold = 12F;
        private const double MaxManualAngleSweep = 270D;
        private const double MinTextScale = 0.25D;
        private const double MaxTextScale = 8D;
        private const int CadCurveObjectMinimumSegments = 3;
        private const double CadCurveObjectMinimumTurnDegrees = 5D;
        private const double CadCurveObjectMaximumJoinDegrees = 45D;
        private const double CadCurveObjectMaximumLengthRatio = 12D;

        private CadShapeEditDocument document;
        private CadShapeEditDocument originalDocument;
        private readonly Stack<CadShapeEditDocument> undoStack;
        private readonly Stack<CadShapeEditDocument> redoStack;
        private readonly HashSet<int> selectedIndices;
        private readonly Dictionary<int, CadShapeEditElement> dragStartElements;
        private readonly Dictionary<int, int> cadCurveObjectGroupByElementIndex;
        private readonly Dictionary<int, List<int>> cadCurveObjectMembers;
        private CadShapeEditorMode mode;
        private int selectedIndex;
        private bool isDragging;
        private int dragKind;
        private PointF dragStartWorld;
        private bool isPanning;
        private Point panStartScreen;
        private PointF panStartOffset;
        private PointF panOffset;
        private bool hasPendingLineStart;
        private PointF pendingLineStart;
        private bool hasPendingCircleCenter;
        private PointF pendingCircleCenter;
        private bool hasPendingAngleCenter;
        private bool hasPendingAngleStart;
        private PointF pendingAngleCenter;
        private PointF pendingAngleStart;
        private bool hasPendingAngleSweep;
        private double pendingAngleLastDegrees;
        private double pendingAngleSweep;
        private bool isMarqueeSelecting;
        private Point marqueeStartScreen;
        private Point marqueeCurrentScreen;
        private Point currentMouseScreen;
        private float zoom;
        private bool suppressHistory;
        private bool hasViewBounds;
        private double viewMinX;
        private double viewMinY;
        private double viewMaxX;
        private double viewMaxY;
        private TextBox inlineTextEditor;
        private int inlineTextElementIndex;
        private bool inlineEditClosing;
        private static Cursor rotationCursor;

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
            selectedIndices = new HashSet<int>();
            dragStartElements = new Dictionary<int, CadShapeEditElement>();
            cadCurveObjectGroupByElementIndex = new Dictionary<int, int>();
            cadCurveObjectMembers = new Dictionary<int, List<int>>();
            mode = CadShapeEditorMode.Select;
            selectedIndex = -1;
            zoom = DefaultFitZoom;
            panOffset = PointF.Empty;
            hasViewBounds = false;
            inlineTextElementIndex = -1;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(250, 251, 253);
            TabStop = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.StandardClick
                | ControlStyles.StandardDoubleClick,
                true
            );
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

        public int SelectedCount
        {
            get { return GetSelectedObjectCount(); }
        }

        public bool IsSingleCadCurveObjectSelected
        {
            get
            {
                int groupId;
                return TryGetSingleSelectedCadCurveObjectGroup(out groupId);
            }
        }

        public bool CanSplitSelectedLine
        {
            get
            {
                CadShapeEditElement selected = SelectedElement;
                return selectedIndices.Count == 1
                    && selected != null
                    && selected.Type == "LINE"
                    && !cadCurveObjectGroupByElementIndex.ContainsKey(selectedIndex);
            }
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

                CommitInlineTextEdit();
                mode = value;
                hasPendingLineStart = false;
                hasPendingCircleCenter = false;
                hasPendingAngleCenter = false;
                hasPendingAngleStart = false;
                hasPendingAngleSweep = false;
                pendingAngleSweep = 0D;
                isDragging = false;
                isMarqueeSelecting = false;
                Invalidate();
                OnModeChanged();
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
            CancelInlineTextEdit();
            document = source == null ? CadShapeEditDocument.CreateEmpty() : source.Clone();
            originalDocument = original == null ? document.Clone() : original.Clone();
            document.EnsureTextIds();
            originalDocument.EnsureTextIds();
            RebuildCadCurveObjectGroups();
            undoStack.Clear();
            redoStack.Clear();
            ClearSelection(false);
            zoom = DefaultFitZoom;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            hasPendingLineStart = false;
            hasPendingCircleCenter = false;
            hasPendingAngleCenter = false;
            hasPendingAngleStart = false;
            hasPendingAngleSweep = false;
            pendingAngleSweep = 0D;
            isMarqueeSelecting = false;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void FitToScreen()
        {
            CommitInlineTextEdit();
            zoom = DefaultFitZoom;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            Invalidate();
        }

        public void ZoomIn()
        {
            CommitInlineTextEdit();
            SetZoom(zoom * 1.2F, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void ZoomOut()
        {
            CommitInlineTextEdit();
            SetZoom(zoom / 1.2F, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void Undo()
        {
            CommitInlineTextEdit();

            if (undoStack.Count == 0)
            {
                return;
            }

            redoStack.Push(document.Clone());
            document = undoStack.Pop();
            document.EnsureTextIds();
            RebuildCadCurveObjectGroups();
            ClearSelection(false);
            hasPendingLineStart = false;
            hasPendingCircleCenter = false;
            hasPendingAngleCenter = false;
            hasPendingAngleStart = false;
            hasPendingAngleSweep = false;
            pendingAngleSweep = 0D;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void Redo()
        {
            CommitInlineTextEdit();

            if (redoStack.Count == 0)
            {
                return;
            }

            undoStack.Push(document.Clone());
            document = redoStack.Pop();
            document.EnsureTextIds();
            RebuildCadCurveObjectGroups();
            ClearSelection(false);
            hasPendingLineStart = false;
            hasPendingCircleCenter = false;
            hasPendingAngleCenter = false;
            hasPendingAngleStart = false;
            hasPendingAngleSweep = false;
            pendingAngleSweep = 0D;
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void RestoreOriginal()
        {
            CommitInlineTextEdit();

            if (originalDocument == null)
            {
                return;
            }

            PushUndo();
            document = originalDocument.Clone();
            document.EnsureTextIds();
            RebuildCadCurveObjectGroups();
            ClearSelection(false);
            hasPendingLineStart = false;
            hasPendingCircleCenter = false;
            hasPendingAngleCenter = false;
            hasPendingAngleStart = false;
            hasPendingAngleSweep = false;
            pendingAngleSweep = 0D;
            zoom = DefaultFitZoom;
            panOffset = PointF.Empty;
            ResetViewBoundsFromDocument();
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void DeleteSelected()
        {
            CommitInlineTextEdit();

            if (document == null || selectedIndices.Count == 0)
            {
                return;
            }

            List<int> indexes = GetSelectedIndexesDescending();
            PushUndo();
            int i;

            for (i = 0; i < indexes.Count; i++)
            {
                int index = indexes[i];
                if (index >= 0 && index < document.Elements.Count)
                {
                    document.Elements.RemoveAt(index);
                }
            }

            ClearSelection(false);
            document.EnsureTextIds();
            RebuildCadCurveObjectGroups();
            Invalidate();
            OnSelectionChanged();
            OnDocumentChanged();
        }

        public void SplitSelectedLine()
        {
            CommitInlineTextEdit();
            CadShapeEditElement selected = SelectedElement;

            if (!CanSplitSelectedLine || selected == null)
            {
                return;
            }

            double middleX = (selected.X1 + selected.X2) / 2D;
            double middleY = (selected.Y1 + selected.Y2) / 2D;

            if (Distance(
                new PointF((float)selected.X1, (float)selected.Y1),
                new PointF((float)selected.X2, (float)selected.Y2)) < 0.2F)
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
            RebuildCadCurveObjectGroups();
            SetSelectedIndex(selectedIndex + 1);
            Invalidate();
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
            CommitInlineTextEdit();
            List<int> indexes = GetSelectedIndexesAscending();
            bool changed = false;
            int i;

            for (i = 0; i < indexes.Count; i++)
            {
                CadShapeEditElement element = document.Elements[indexes[i]];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE"
                    && !cadCurveObjectGroupByElementIndex.ContainsKey(indexes[i])
                    && Math.Abs(element.Y2 - element.Y1) > 0.0001D)
                {
                    changed = true;
                    break;
                }

                if (element.Type == "TEXT" && Math.Abs(NormalizeSignedDegrees(element.Rotation)) > 0.001D)
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                return;
            }

            PushUndo();

            for (i = 0; i < indexes.Count; i++)
            {
                CadShapeEditElement element = document.Elements[indexes[i]];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE"
                    && !cadCurveObjectGroupByElementIndex.ContainsKey(indexes[i]))
                {
                    element.Y2 = element.Y1;
                }
                else if (element.Type == "TEXT")
                {
                    element.Rotation = 0D;
                    element.HasBounds = false;
                }
            }

            RebuildCadCurveObjectGroups();
            Invalidate();
            OnDocumentChanged();
        }

        public void AlignSelectedVertical()
        {
            CommitInlineTextEdit();
            List<int> indexes = GetSelectedIndexesAscending();
            bool changed = false;
            int i;

            for (i = 0; i < indexes.Count; i++)
            {
                CadShapeEditElement element = document.Elements[indexes[i]];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE"
                    && !cadCurveObjectGroupByElementIndex.ContainsKey(indexes[i])
                    && Math.Abs(element.X2 - element.X1) > 0.0001D)
                {
                    changed = true;
                    break;
                }

                if (element.Type == "TEXT" && Math.Abs(NormalizeSignedDegrees(element.Rotation) - 90D) > 0.001D)
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                return;
            }

            PushUndo();

            for (i = 0; i < indexes.Count; i++)
            {
                CadShapeEditElement element = document.Elements[indexes[i]];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE"
                    && !cadCurveObjectGroupByElementIndex.ContainsKey(indexes[i]))
                {
                    element.X2 = element.X1;
                }
                else if (element.Type == "TEXT")
                {
                    element.Rotation = 90D;
                    element.HasBounds = false;
                }
            }

            RebuildCadCurveObjectGroups();
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

                if (element != null
                    && element.Type == "TEXT"
                    && element.TextId.Equals(textId, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedIndex(i);
                    Mode = CadShapeEditorMode.Select;
                    return;
                }
            }
        }

        public void BeginSelectedTextEdit()
        {
            CadShapeEditElement selected = SelectedElement;
            if (selected == null || selected.Type != "TEXT")
            {
                return;
            }

            BeginInlineTextEdit(selectedIndex);
        }

        public void CommitInlineTextEdit()
        {
            EndInlineTextEdit(true);
        }

        public void CancelInlineTextEdit()
        {
            EndInlineTextEdit(false);
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
            DrawPendingCircle(g);
            DrawPendingAngle(g);
            DrawMarquee(g);
            DrawOverlay(g);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CommitInlineTextEdit();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            currentMouseScreen = e.Location;

            if (inlineTextEditor != null && !inlineTextEditor.Bounds.Contains(e.Location))
            {
                CommitInlineTextEdit();
            }

            if (e.Button == MouseButtons.Middle)
            {
                CommitInlineTextEdit();
                isPanning = true;
                panStartScreen = e.Location;
                panStartOffset = panOffset;
                Cursor = Cursors.Hand;
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                hasPendingLineStart = false;
                hasPendingCircleCenter = false;
                hasPendingAngleCenter = false;
                hasPendingAngleStart = false;
                hasPendingAngleSweep = false;
                pendingAngleSweep = 0D;

                if (mode == CadShapeEditorMode.AddLine
                    || mode == CadShapeEditorMode.AddCircle
                    || mode == CadShapeEditorMode.AddAngle)
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

            if (mode == CadShapeEditorMode.AddCircle)
            {
                HandleAddCircleClick(world);
                return;
            }

            if (mode == CadShapeEditorMode.AddAngle)
            {
                HandleAddAngleClick(world);
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

            if (hitIndex < 0)
            {
                ClearSelection(true);
                isMarqueeSelecting = true;
                marqueeStartScreen = e.Location;
                marqueeCurrentScreen = e.Location;
                Invalidate();
                return;
            }

            List<int> cadCurveObject;
            if (TryGetCadCurveObjectMembers(hitIndex, out cadCurveObject))
            {
                if (!AreAllIndicesSelected(cadCurveObject))
                {
                    SetSelectedCadCurveObject(hitIndex, cadCurveObject);
                }
                else
                {
                    SetPrimarySelectedIndex(hitIndex);
                }

                // CAD 곡선은 샘플 선분의 끝점이 아니라 원본 곡선 객체 전체를 이동합니다.
                hitPart = 3;
            }
            else
            {
                if (!selectedIndices.Contains(hitIndex))
                {
                    SetSelectedIndex(hitIndex);
                }
                else
                {
                    SetPrimarySelectedIndex(hitIndex);
                }

                if (hitPart != 3 && selectedIndices.Count > 1)
                {
                    SetSelectedIndex(hitIndex);
                }
            }

            isDragging = true;
            dragKind = hitPart;
            dragStartWorld = world;
            CaptureDragStartElements(hitPart == 3 && selectedIndices.Count > 1);
            PushUndo();
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
            dragStartElements.Clear();
            dragKind = 0;
            SetSelectedIndex(hitIndex);
            BeginInlineTextEdit(hitIndex);
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

            if (isMarqueeSelecting)
            {
                marqueeCurrentScreen = e.Location;
                Cursor = Cursors.Cross;
                Invalidate();
                return;
            }

            if (isDragging && SelectedElement != null && dragStartElements.Count > 0)
            {
                PointF world = ScreenToWorld(e.Location);
                double dx = world.X - dragStartWorld.X;
                double dy = world.Y - dragStartWorld.Y;
                ApplyDrag(dx, dy, world);
                Invalidate();
                OnDocumentChanged();
                return;
            }

            if (mode == CadShapeEditorMode.AddAngle && hasPendingAngleStart)
            {
                PointF currentWorld = SnapToExistingLineEndpoint(ScreenToWorld(e.Location), -1, null);
                UpdatePendingAngleSweep(currentWorld);
                Cursor = Cursors.Cross;
                Invalidate();
                return;
            }

            int hoverIndex;
            int hoverPart;
            HitTest(e.Location, out hoverIndex, out hoverPart);
            Cursor = GetCursorForHit(hoverIndex, hoverPart);
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
                if (isMarqueeSelecting)
                {
                    CompleteMarqueeSelection();
                }

                isMarqueeSelecting = false;
                isDragging = false;
                dragStartElements.Clear();
                dragKind = 0;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if ((ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }

            CommitInlineTextEdit();
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
            else if (e.Control && e.KeyCode == Keys.A && mode == CadShapeEditorMode.Select)
            {
                SelectAllElements();
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
                if (inlineTextEditor != null)
                {
                    CancelInlineTextEdit();
                }
                else
                {
                    hasPendingLineStart = false;
                    hasPendingCircleCenter = false;
                    hasPendingAngleCenter = false;
                    hasPendingAngleStart = false;
                    Mode = CadShapeEditorMode.Select;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter
                && (mode == CadShapeEditorMode.AddLine
                    || mode == CadShapeEditorMode.AddCircle
                    || mode == CadShapeEditorMode.AddAngle))
            {
                hasPendingLineStart = false;
                hasPendingCircleCenter = false;
                hasPendingAngleCenter = false;
                hasPendingAngleStart = false;
                hasPendingAngleSweep = false;
                pendingAngleSweep = 0D;
                Mode = CadShapeEditorMode.Select;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if ((e.KeyCode == Keys.F2 || e.KeyCode == Keys.Enter)
                && mode == CadShapeEditorMode.Select
                && SelectedElement != null
                && SelectedElement.Type == "TEXT")
            {
                BeginSelectedTextEdit();
                OnTextEditRequested();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void HandleAddLineClick(PointF world)
        {
            if (!hasPendingLineStart)
            {
                pendingLineStart = SnapToExistingLineEndpoint(world, -1, null);
                hasPendingLineStart = true;
                Invalidate();
                return;
            }

            PointF end = world;
            end = SnapToExistingLineEndpoint(end, -1, null);

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
            RebuildCadCurveObjectGroups();
            SetSelectedIndex(document.Elements.Count - 1);
            pendingLineStart = end;
            Invalidate();
            OnDocumentChanged();
        }

        private void HandleAddCircleClick(PointF world)
        {
            if (!hasPendingCircleCenter)
            {
                pendingCircleCenter = world;
                hasPendingCircleCenter = true;
                Invalidate();
                return;
            }

            double radius = Distance(pendingCircleCenter, world);

            if (radius < 0.1D)
            {
                return;
            }

            PushUndo();
            CadShapeEditElement circle = new CadShapeEditElement();
            circle.Type = "CIRCLE";
            circle.CX = pendingCircleCenter.X;
            circle.CY = pendingCircleCenter.Y;
            circle.Radius = radius;
            circle.StartAngle = 0D;
            circle.EndAngle = 360D;
            document.Elements.Add(circle);
            RebuildCadCurveObjectGroups();
            SetSelectedIndex(document.Elements.Count - 1);
            hasPendingCircleCenter = false;
            Mode = CadShapeEditorMode.Select;
            Invalidate();
            OnDocumentChanged();
        }

        private void HandleAddAngleClick(PointF world)
        {
            PointF snapped = SnapToExistingLineEndpoint(world, -1, null);

            if (!hasPendingAngleCenter)
            {
                pendingAngleCenter = snapped;
                hasPendingAngleCenter = true;
                hasPendingAngleStart = false;
                Invalidate();
                return;
            }

            if (!hasPendingAngleStart)
            {
                if (Distance(pendingAngleCenter, snapped) < 0.1F)
                {
                    return;
                }

                pendingAngleStart = snapped;
                hasPendingAngleStart = true;
                pendingAngleLastDegrees = GetArcAngleDegrees(pendingAngleCenter, pendingAngleStart);
                pendingAngleSweep = 0D;
                hasPendingAngleSweep = true;
                Invalidate();
                return;
            }

            double radius = Distance(pendingAngleCenter, pendingAngleStart);
            if (radius < 0.1D || Distance(pendingAngleCenter, snapped) < 0.1F)
            {
                return;
            }

            double startAngle = GetArcAngleDegrees(pendingAngleCenter, pendingAngleStart);
            UpdatePendingAngleSweep(snapped);
            double sweep = ClampManualAngleSweep(pendingAngleSweep);

            if (Math.Abs(sweep) < 1D)
            {
                return;
            }

            PushUndo();
            CadShapeEditElement angle = new CadShapeEditElement();
            angle.Type = "ARC";
            angle.CX = pendingAngleCenter.X;
            angle.CY = pendingAngleCenter.Y;
            angle.Radius = radius;
            angle.StartAngle = startAngle;
            angle.EndAngle = startAngle + sweep;
            document.Elements.Add(angle);
            RebuildCadCurveObjectGroups();
            SetSelectedIndex(document.Elements.Count - 1);
            hasPendingAngleCenter = false;
            hasPendingAngleStart = false;
            hasPendingAngleSweep = false;
            pendingAngleSweep = 0D;
            Mode = CadShapeEditorMode.Select;
            Invalidate();
            OnDocumentChanged();
        }

        private void UpdatePendingAngleSweep(PointF world)
        {
            if (!hasPendingAngleCenter || !hasPendingAngleStart)
            {
                return;
            }

            if (Distance(pendingAngleCenter, world) < 0.1F)
            {
                return;
            }

            double currentDegrees = GetArcAngleDegrees(pendingAngleCenter, world);

            if (!hasPendingAngleSweep)
            {
                pendingAngleLastDegrees = GetArcAngleDegrees(pendingAngleCenter, pendingAngleStart);
                pendingAngleSweep = 0D;
                hasPendingAngleSweep = true;
            }

            double delta = NormalizeSignedDegrees(currentDegrees - pendingAngleLastDegrees);
            pendingAngleSweep = ClampManualAngleSweep(pendingAngleSweep + delta);
            pendingAngleLastDegrees = currentDegrees;
        }

        private double ClampManualAngleSweep(double sweep)
        {
            if (sweep > MaxManualAngleSweep)
            {
                return MaxManualAngleSweep;
            }

            if (sweep < -MaxManualAngleSweep)
            {
                return -MaxManualAngleSweep;
            }

            return sweep;
        }

        private double ResolveEditableArcSweep(double rawDelta, double referenceSweep)
        {
            double positive = NormalizeDegrees(rawDelta);
            double negative = positive - 360D;
            bool positiveAllowed = positive <= MaxManualAngleSweep + 0.0001D;
            bool negativeAllowed = Math.Abs(negative) <= MaxManualAngleSweep + 0.0001D;

            if (positiveAllowed && negativeAllowed)
            {
                return Math.Abs(positive - referenceSweep) <= Math.Abs(negative - referenceSweep)
                    ? positive
                    : negative;
            }

            if (positiveAllowed)
            {
                return positive;
            }

            if (negativeAllowed)
            {
                return negative;
            }

            return referenceSweep >= 0D ? MaxManualAngleSweep : -MaxManualAngleSweep;
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
            RebuildCadCurveObjectGroups();
            SetSelectedIndex(document.Elements.Count - 1);
            Mode = CadShapeEditorMode.Select;
            Invalidate();
            OnDocumentChanged();

            BeginInvoke((MethodInvoker)delegate
            {
                BeginSelectedTextEdit();
                OnTextEditRequested();
            });
        }

        private PointF SnapToExistingLineEndpoint(PointF candidateWorld, int ignoredElementIndex, HashSet<int> additionalIgnored)
        {
            if (document == null || document.Elements == null)
            {
                return candidateWorld;
            }

            PointF candidateScreen = WorldToScreen(candidateWorld);
            float bestDistance = EndpointSnapThreshold;
            PointF bestWorld = candidateWorld;
            int i;

            for (i = 0; i < document.Elements.Count; i++)
            {
                if (i == ignoredElementIndex || (additionalIgnored != null && additionalIgnored.Contains(i)))
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

        private void ApplyDrag(double dx, double dy, PointF currentWorld)
        {
            if (dragStartElements.Count == 0)
            {
                return;
            }

            if (dragKind == 3 && dragStartElements.Count > 1)
            {
                double adjustedDx = dx;
                double adjustedDy = dy;

                // 곡선 샘플의 내부 꼭짓점은 CAD 편집용 끝점이 아니므로 이동 스냅 기준에서 제외합니다.
                if (!cadCurveObjectGroupByElementIndex.ContainsKey(selectedIndex))
                {
                    AdjustTranslationForEndpointSnap(ref adjustedDx, ref adjustedDy);
                }

                ApplyTranslationToDragElements(adjustedDx, adjustedDy);
                return;
            }

            CadShapeEditElement start;
            if (!dragStartElements.TryGetValue(selectedIndex, out start) || start == null)
            {
                return;
            }

            CadShapeEditElement selected = SelectedElement;
            if (selected == null)
            {
                return;
            }

            if (selected.Type == "LINE")
            {
                if (dragKind == 1)
                {
                    PointF snapped = new PointF(
                        (float)(start.X1 + dx),
                        (float)(start.Y1 + dy)
                    );
                    snapped = SnapToExistingLineEndpoint(snapped, selectedIndex, null);
                    selected.X1 = snapped.X;
                    selected.Y1 = snapped.Y;
                    selected.X2 = start.X2;
                    selected.Y2 = start.Y2;
                }
                else if (dragKind == 2)
                {
                    PointF snapped = new PointF(
                        (float)(start.X2 + dx),
                        (float)(start.Y2 + dy)
                    );
                    snapped = SnapToExistingLineEndpoint(snapped, selectedIndex, null);
                    selected.X1 = start.X1;
                    selected.Y1 = start.Y1;
                    selected.X2 = snapped.X;
                    selected.Y2 = snapped.Y;
                }
                else if (dragKind == 4)
                {
                    RotateLineEndpoint(selected, start, true, currentWorld);
                }
                else if (dragKind == 5)
                {
                    RotateLineEndpoint(selected, start, false, currentWorld);
                }
                else
                {
                    double adjustedDx = dx;
                    double adjustedDy = dy;
                    AdjustTranslationForEndpointSnap(ref adjustedDx, ref adjustedDy);
                    ApplyTranslationToDragElements(adjustedDx, adjustedDy);
                }
            }
            else if (selected.Type == "TEXT")
            {
                if (dragKind == 11)
                {
                    RotateTextElement(selected, start, currentWorld);
                }
                else if (dragKind == 12)
                {
                    ResizeTextElement(selected, start, currentWorld);
                }
                else
                {
                    selected.X1 = start.X1 + dx;
                    selected.Y1 = start.Y1 + dy;
                    selected.HasBounds = false;
                }
            }
            else if (selected.Type == "CIRCLE")
            {
                if (dragKind == 6)
                {
                    double radiusDx = currentWorld.X - start.CX;
                    double radiusDy = currentWorld.Y - start.CY;
                    selected.Radius = Math.Max(0.1D, Math.Sqrt(radiusDx * radiusDx + radiusDy * radiusDy));
                }
                else
                {
                    selected.CX = start.CX + dx;
                    selected.CY = start.CY + dy;
                }
            }
            else if (selected.Type == "ARC")
            {
                if (dragKind == 7)
                {
                    double radiusDx = currentWorld.X - start.CX;
                    double radiusDy = currentWorld.Y - start.CY;
                    selected.Radius = Math.Max(0.1D, Math.Sqrt(radiusDx * radiusDx + radiusDy * radiusDy));
                }
                else if (dragKind == 8)
                {
                    PointF center = new PointF((float)start.CX, (float)start.CY);
                    double newStart = GetArcAngleDegrees(center, currentWorld);
                    double referenceSweep = start.EndAngle - start.StartAngle;
                    double newSweep = ResolveEditableArcSweep(start.EndAngle - newStart, referenceSweep);
                    selected.StartAngle = newStart;
                    selected.EndAngle = newStart + newSweep;
                }
                else if (dragKind == 9)
                {
                    PointF center = new PointF((float)start.CX, (float)start.CY);
                    double newEnd = GetArcAngleDegrees(center, currentWorld);
                    double referenceSweep = start.EndAngle - start.StartAngle;
                    selected.StartAngle = start.StartAngle;
                    selected.EndAngle = start.StartAngle
                        + ResolveEditableArcSweep(newEnd - start.StartAngle, referenceSweep);
                }
                else if (dragKind == 10)
                {
                    RotateArcElement(selected, start, currentWorld);
                }
                else
                {
                    selected.CX = start.CX + dx;
                    selected.CY = start.CY + dy;
                }
            }
        }

        private void RotateLineEndpoint(CadShapeEditElement selected, CadShapeEditElement start, bool moveFirst, PointF currentWorld)
        {
            PointF fixedWorld = moveFirst
                ? new PointF((float)start.X2, (float)start.Y2)
                : new PointF((float)start.X1, (float)start.Y1);
            PointF originalMoving = moveFirst
                ? new PointF((float)start.X1, (float)start.Y1)
                : new PointF((float)start.X2, (float)start.Y2);
            double length = Distance(fixedWorld, originalMoving);

            if (length <= 0.0001D)
            {
                return;
            }

            double angle = Math.Atan2(currentWorld.Y - fixedWorld.Y, currentWorld.X - fixedWorld.X);

            PointF rotated = new PointF(
                (float)(fixedWorld.X + Math.Cos(angle) * length),
                (float)(fixedWorld.Y + Math.Sin(angle) * length)
            );

            if (moveFirst)
            {
                selected.X1 = rotated.X;
                selected.Y1 = rotated.Y;
                selected.X2 = start.X2;
                selected.Y2 = start.Y2;
            }
            else
            {
                selected.X1 = start.X1;
                selected.Y1 = start.Y1;
                selected.X2 = rotated.X;
                selected.Y2 = rotated.Y;
            }
        }

        private void RotateTextElement(CadShapeEditElement selected, CadShapeEditElement start, PointF currentWorld)
        {
            double dx = currentWorld.X - start.X1;
            double dy = currentWorld.Y - start.Y1;

            if (Math.Abs(dx) < 0.0001D && Math.Abs(dy) < 0.0001D)
            {
                return;
            }

            selected.Rotation = NormalizeSignedDegrees(Math.Atan2(dy, dx) * 180D / Math.PI + 90D);
            selected.HasBounds = false;
        }

        private void ResizeTextElement(CadShapeEditElement selected, CadShapeEditElement start, PointF currentWorld)
        {
            PointF centerScreen = WorldToScreen(new PointF((float)start.X1, (float)start.Y1));
            PointF startHandle = GetTextResizeHandle(start);
            PointF currentScreen = WorldToScreen(currentWorld);
            double baseDistance = Math.Max(Distance(centerScreen, startHandle), 1D);
            double currentDistance = Math.Max(Distance(centerScreen, currentScreen), 1D);
            double startScale = Math.Max(MinTextScale, Math.Min(MaxTextScale, start.TextScale));
            selected.TextScale = Math.Max(
                MinTextScale,
                Math.Min(MaxTextScale, startScale * currentDistance / baseDistance)
            );
            selected.HasBounds = false;
        }

        private void RotateArcElement(CadShapeEditElement selected, CadShapeEditElement start, PointF currentWorld)
        {
            PointF center = new PointF((float)start.CX, (float)start.CY);
            double middleAngle = start.StartAngle + (start.EndAngle - start.StartAngle) / 2D;
            double currentAngle = GetArcAngleDegrees(center, currentWorld);
            double delta = NormalizeSignedDegrees(currentAngle - middleAngle);
            selected.StartAngle = start.StartAngle + delta;
            selected.EndAngle = start.EndAngle + delta;
        }

        private void ApplyTranslationToDragElements(double dx, double dy)
        {
            foreach (KeyValuePair<int, CadShapeEditElement> pair in dragStartElements)
            {
                int index = pair.Key;
                CadShapeEditElement start = pair.Value;

                if (index < 0 || index >= document.Elements.Count || start == null)
                {
                    continue;
                }

                CadShapeEditElement target = document.Elements[index];
                if (target == null)
                {
                    continue;
                }

                if (target.Type == "LINE")
                {
                    target.X1 = start.X1 + dx;
                    target.Y1 = start.Y1 + dy;
                    target.X2 = start.X2 + dx;
                    target.Y2 = start.Y2 + dy;
                }
                else if (target.Type == "TEXT")
                {
                    target.X1 = start.X1 + dx;
                    target.Y1 = start.Y1 + dy;
                    target.HasBounds = false;
                }
                else if (target.Type == "ARC" || target.Type == "CIRCLE")
                {
                    target.CX = start.CX + dx;
                    target.CY = start.CY + dy;
                }
            }
        }

        private void AdjustTranslationForEndpointSnap(ref double dx, ref double dy)
        {
            CadShapeEditElement primaryStart;
            if (!dragStartElements.TryGetValue(selectedIndex, out primaryStart)
                || primaryStart == null
                || primaryStart.Type != "LINE")
            {
                return;
            }

            HashSet<int> ignored = new HashSet<int>();
            foreach (int index in dragStartElements.Keys)
            {
                ignored.Add(index);
            }

            PointF movedFirst = new PointF((float)(primaryStart.X1 + dx), (float)(primaryStart.Y1 + dy));
            PointF movedSecond = new PointF((float)(primaryStart.X2 + dx), (float)(primaryStart.Y2 + dy));
            PointF snappedFirst = SnapToExistingLineEndpoint(movedFirst, -1, ignored);
            PointF snappedSecond = SnapToExistingLineEndpoint(movedSecond, -1, ignored);
            bool firstSnapped = Distance(movedFirst, snappedFirst) > 0.0001F;
            bool secondSnapped = Distance(movedSecond, snappedSecond) > 0.0001F;
            float firstDistance = firstSnapped ? Distance(WorldToScreen(movedFirst), WorldToScreen(snappedFirst)) : Single.MaxValue;
            float secondDistance = secondSnapped ? Distance(WorldToScreen(movedSecond), WorldToScreen(snappedSecond)) : Single.MaxValue;

            if (firstSnapped && firstDistance < EndpointSnapThreshold && firstDistance <= secondDistance)
            {
                dx += snappedFirst.X - movedFirst.X;
                dy += snappedFirst.Y - movedFirst.Y;
            }
            else if (secondSnapped && secondDistance < EndpointSnapThreshold)
            {
                dx += snappedSecond.X - movedSecond.X;
                dy += snappedSecond.Y - movedSecond.Y;
            }
        }

        private void SetZoom(float newZoom, Point anchorScreen)
        {
            if (newZoom < MinZoom) newZoom = MinZoom;
            if (newZoom > MaxZoom) newZoom = MaxZoom;

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

            CadShapeEditElement primary = SelectedElement;
            if (primary != null && selectedIndices.Count == 1)
            {
                if (primary.Type == "LINE")
                {
                    PointF p1 = WorldToScreen(new PointF((float)primary.X1, (float)primary.Y1));
                    PointF p2 = WorldToScreen(new PointF((float)primary.X2, (float)primary.Y2));
                    PointF rotate1;
                    PointF rotate2;
                    GetLineRotationHandles(p1, p2, out rotate1, out rotate2);

                    if (Distance(rotate1, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 4;
                        return;
                    }

                    if (Distance(rotate2, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 5;
                        return;
                    }
                }
                else if (primary.Type == "CIRCLE")
                {
                    PointF radiusHandle = GetCircleRadiusHandle(primary);
                    if (Distance(radiusHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 6;
                        return;
                    }
                }
                else if (primary.Type == "ARC")
                {
                    PointF radiusHandle;
                    PointF startHandle;
                    PointF endHandle;
                    PointF rotationHandle;
                    GetArcEditHandles(primary, out radiusHandle, out startHandle, out endHandle, out rotationHandle);

                    if (Distance(radiusHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 7;
                        return;
                    }

                    if (Distance(startHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 8;
                        return;
                    }

                    if (Distance(endHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 9;
                        return;
                    }

                    if (Distance(rotationHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 10;
                        return;
                    }
                }
                else if (primary.Type == "TEXT")
                {
                    PointF textResizeHandle = GetTextResizeHandle(primary);
                    if (Distance(textResizeHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 12;
                        return;
                    }

                    PointF textRotationHandle = GetTextRotationHandle(primary);
                    if (Distance(textRotationHandle, screenPoint) <= 10F)
                    {
                        hitIndex = selectedIndex;
                        hitPart = 11;
                        return;
                    }
                }
            }

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
                        hitPart = cadCurveObjectGroupByElementIndex.ContainsKey(i) ? 3 : 1;
                        return;
                    }

                    if (Distance(p2, screenPoint) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = cadCurveObjectGroupByElementIndex.ContainsKey(i) ? 3 : 2;
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
                    if (IsPointInsideText(screenPoint, element))
                    {
                        hitIndex = i;
                        hitPart = 3;
                        return;
                    }
                }
                else if (element.Type == "CIRCLE")
                {
                    PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
                    float radius = (float)(Math.Abs(element.Radius) * GetTransform().Scale);
                    float distance = Distance(center, screenPoint);

                    if (Math.Abs(distance - radius) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 3;
                        return;
                    }
                }
                else if (element.Type == "ARC")
                {
                    if (DistancePointToArc(screenPoint, element) <= threshold)
                    {
                        hitIndex = i;
                        hitPart = 3;
                        return;
                    }
                }
            }
        }

        private Cursor GetCursorForHit(int hitIndex, int hitPart)
        {
            if (mode == CadShapeEditorMode.AddLine
                || mode == CadShapeEditorMode.AddCircle
                || mode == CadShapeEditorMode.AddAngle
                || mode == CadShapeEditorMode.AddText)
            {
                return Cursors.Cross;
            }

            if (hitIndex < 0)
            {
                return Cursors.Default;
            }

            if (hitPart == 4 || hitPart == 5 || hitPart == 10 || hitPart == 11)
            {
                return GetRotationCursor();
            }

            if (hitPart == 1 || hitPart == 2 || hitPart == 6 || hitPart == 7 || hitPart == 8 || hitPart == 9 || hitPart == 12)
            {
                return Cursors.Cross;
            }

            return Cursors.SizeAll;
        }

        private static Cursor GetRotationCursor()
        {
            if (rotationCursor != null)
            {
                return rotationCursor;
            }

            try
            {
                using (Bitmap bitmap = new Bitmap(32, 32))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Pen pen = new Pen(Color.FromArgb(24, 91, 177), 2.2F))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawArc(pen, 5F, 5F, 22F, 22F, 35F, 275F);
                    graphics.DrawLine(pen, 7F, 8F, 7F, 14F);
                    graphics.DrawLine(pen, 7F, 8F, 13F, 8F);

                    IntPtr iconHandle = bitmap.GetHicon();
                    IconInfo iconInfo;

                    if (GetIconInfo(iconHandle, out iconInfo))
                    {
                        iconInfo.IsIcon = false;
                        iconInfo.XHotspot = 16;
                        iconInfo.YHotspot = 16;
                        IntPtr cursorHandle = CreateIconIndirect(ref iconInfo);

                        if (iconInfo.ColorBitmap != IntPtr.Zero)
                        {
                            DeleteObject(iconInfo.ColorBitmap);
                        }

                        if (iconInfo.MaskBitmap != IntPtr.Zero)
                        {
                            DeleteObject(iconInfo.MaskBitmap);
                        }

                        DestroyIcon(iconHandle);

                        if (cursorHandle != IntPtr.Zero)
                        {
                            rotationCursor = new Cursor(cursorHandle);
                            return rotationCursor;
                        }
                    }
                    else
                    {
                        DestroyIcon(iconHandle);
                    }
                }
            }
            catch
            {
                // 커스텀 커서를 만들 수 없는 Windows 환경에서는 안전하게 손 모양 커서를 사용합니다.
            }

            return Cursors.Hand;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool IsIcon;
            public int XHotspot;
            public int YHotspot;
            public IntPtr MaskBitmap;
            public IntPtr ColorBitmap;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetIconInfo(IntPtr iconHandle, out IconInfo iconInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr iconHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr objectHandle);

        private void RebuildCadCurveObjectGroups()
        {
            cadCurveObjectGroupByElementIndex.Clear();
            cadCurveObjectMembers.Clear();

            if (document == null || document.Elements == null || document.Elements.Count < CadCurveObjectMinimumSegments)
            {
                return;
            }

            double connectionTolerance = GetCadCurveObjectConnectionTolerance();
            int nextGroupId = 1;
            int index = 0;

            while (index < document.Elements.Count)
            {
                CadShapeEditElement first = document.Elements[index];

                if (first == null || first.Type != "LINE")
                {
                    index++;
                    continue;
                }

                List<int> run = new List<int>();
                List<double> joinAngles = new List<double>();
                run.Add(index);
                int nextIndex = index + 1;

                while (nextIndex < document.Elements.Count)
                {
                    CadShapeEditElement previous = document.Elements[nextIndex - 1];
                    CadShapeEditElement next = document.Elements[nextIndex];

                    if (previous == null || next == null || previous.Type != "LINE" || next.Type != "LINE")
                    {
                        break;
                    }

                    double joinAngle;
                    if (!TryGetConnectedLineJoinAngle(previous, next, connectionTolerance, out joinAngle))
                    {
                        break;
                    }

                    double previousLength = GetLineElementLength(previous);
                    double nextLength = GetLineElementLength(next);
                    double shorter = Math.Min(previousLength, nextLength);
                    double longer = Math.Max(previousLength, nextLength);

                    if (shorter <= 0.000001D
                        || longer / shorter > CadCurveObjectMaximumLengthRatio
                        || joinAngle > CadCurveObjectMaximumJoinDegrees)
                    {
                        break;
                    }

                    run.Add(nextIndex);
                    joinAngles.Add(joinAngle);
                    nextIndex++;
                }

                RegisterCadCurveObjectRun(run, joinAngles, ref nextGroupId);
                index = Math.Max(nextIndex, index + 1);
            }
        }

        private void RegisterCadCurveObjectRun(List<int> run, List<double> joinAngles, ref int nextGroupId)
        {
            if (run == null
                || joinAngles == null
                || run.Count < CadCurveObjectMinimumSegments
                || joinAngles.Count != run.Count - 1)
            {
                return;
            }

            int firstMeaningfulJoin = -1;
            int lastMeaningfulJoin = -1;
            int i;

            for (i = 0; i < joinAngles.Count; i++)
            {
                if (joinAngles[i] >= 0.75D)
                {
                    if (firstMeaningfulJoin < 0)
                    {
                        firstMeaningfulJoin = i;
                    }

                    lastMeaningfulJoin = i;
                }
            }

            if (firstMeaningfulJoin < 0 || lastMeaningfulJoin < firstMeaningfulJoin)
            {
                return;
            }

            int firstSegmentPosition = Math.Max(0, firstMeaningfulJoin);
            int lastSegmentPosition = Math.Min(run.Count - 1, lastMeaningfulJoin + 1);
            int segmentCount = lastSegmentPosition - firstSegmentPosition + 1;

            if (segmentCount < CadCurveObjectMinimumSegments)
            {
                return;
            }

            double totalTurn = 0D;
            for (i = firstSegmentPosition; i < lastSegmentPosition; i++)
            {
                totalTurn += Math.Abs(joinAngles[i]);
            }

            if (totalTurn < CadCurveObjectMinimumTurnDegrees)
            {
                return;
            }

            List<int> members = new List<int>();
            for (i = firstSegmentPosition; i <= lastSegmentPosition; i++)
            {
                members.Add(run[i]);
            }

            int groupId = nextGroupId++;
            cadCurveObjectMembers[groupId] = members;

            for (i = 0; i < members.Count; i++)
            {
                cadCurveObjectGroupByElementIndex[members[i]] = groupId;
            }
        }

        private double GetCadCurveObjectConnectionTolerance()
        {
            double span = 100D;

            if (document != null)
            {
                span = Math.Max(Math.Abs(document.Width), Math.Abs(document.Height));

                double minX;
                double minY;
                double maxX;
                double maxY;
                if (document.TryGetBounds(out minX, out minY, out maxX, out maxY))
                {
                    span = Math.Max(span, Math.Max(Math.Abs(maxX - minX), Math.Abs(maxY - minY)));
                }
            }

            return Math.Max(0.0005D, Math.Min(0.05D, span * 0.0002D));
        }

        private bool TryGetConnectedLineJoinAngle(
            CadShapeEditElement first,
            CadShapeEditElement second,
            double tolerance,
            out double angleDegrees)
        {
            angleDegrees = 0D;

            if (first == null || second == null || first.Type != "LINE" || second.Type != "LINE")
            {
                return false;
            }

            PointF[] firstPoints = new PointF[]
            {
                new PointF((float)first.X1, (float)first.Y1),
                new PointF((float)first.X2, (float)first.Y2)
            };
            PointF[] secondPoints = new PointF[]
            {
                new PointF((float)second.X1, (float)second.Y1),
                new PointF((float)second.X2, (float)second.Y2)
            };

            double bestDistance = Double.MaxValue;
            int firstShared = -1;
            int secondShared = -1;
            int i;
            int j;

            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < 2; j++)
                {
                    double distance = Distance(firstPoints[i], secondPoints[j]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        firstShared = i;
                        secondShared = j;
                    }
                }
            }

            if (bestDistance > tolerance || firstShared < 0 || secondShared < 0)
            {
                return false;
            }

            PointF firstOther = firstPoints[1 - firstShared];
            PointF firstJoin = firstPoints[firstShared];
            PointF secondJoin = secondPoints[secondShared];
            PointF secondOther = secondPoints[1 - secondShared];
            double firstDx = firstJoin.X - firstOther.X;
            double firstDy = firstJoin.Y - firstOther.Y;
            double secondDx = secondOther.X - secondJoin.X;
            double secondDy = secondOther.Y - secondJoin.Y;
            double firstLength = Math.Sqrt(firstDx * firstDx + firstDy * firstDy);
            double secondLength = Math.Sqrt(secondDx * secondDx + secondDy * secondDy);

            if (firstLength <= 0.000001D || secondLength <= 0.000001D)
            {
                return false;
            }

            double dot = (firstDx * secondDx + firstDy * secondDy) / (firstLength * secondLength);
            dot = Math.Max(-1D, Math.Min(1D, dot));
            angleDegrees = Math.Acos(dot) * 180D / Math.PI;
            return true;
        }

        private double GetLineElementLength(CadShapeEditElement element)
        {
            if (element == null || element.Type != "LINE")
            {
                return 0D;
            }

            double dx = element.X2 - element.X1;
            double dy = element.Y2 - element.Y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private bool TryGetCadCurveObjectMembers(int elementIndex, out List<int> members)
        {
            members = null;
            int groupId;

            if (!cadCurveObjectGroupByElementIndex.TryGetValue(elementIndex, out groupId))
            {
                return false;
            }

            return cadCurveObjectMembers.TryGetValue(groupId, out members)
                && members != null
                && members.Count >= CadCurveObjectMinimumSegments;
        }

        private bool AreAllIndicesSelected(List<int> indexes)
        {
            if (indexes == null || indexes.Count == 0)
            {
                return false;
            }

            int i;
            for (i = 0; i < indexes.Count; i++)
            {
                if (!selectedIndices.Contains(indexes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void SetSelectedCadCurveObject(int primaryIndex, List<int> members)
        {
            selectedIndices.Clear();

            if (members != null)
            {
                int i;
                for (i = 0; i < members.Count; i++)
                {
                    int index = members[i];
                    if (index >= 0 && index < document.Elements.Count && document.Elements[index] != null)
                    {
                        selectedIndices.Add(index);
                    }
                }
            }

            selectedIndex = selectedIndices.Contains(primaryIndex)
                ? primaryIndex
                : (selectedIndices.Count > 0 ? GetSmallestSelectedIndex() : -1);
            Invalidate();
            OnSelectionChanged();
        }

        private int GetSelectedObjectCount()
        {
            if (selectedIndices.Count == 0)
            {
                return 0;
            }

            HashSet<int> countedGroups = new HashSet<int>();
            int count = 0;

            foreach (int index in selectedIndices)
            {
                int groupId;
                if (cadCurveObjectGroupByElementIndex.TryGetValue(index, out groupId))
                {
                    if (countedGroups.Add(groupId))
                    {
                        count++;
                    }
                }
                else
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetSingleSelectedCadCurveObjectGroup(out int groupId)
        {
            groupId = -1;

            if (selectedIndices.Count < CadCurveObjectMinimumSegments)
            {
                return false;
            }

            foreach (int index in selectedIndices)
            {
                int currentGroupId;
                if (!cadCurveObjectGroupByElementIndex.TryGetValue(index, out currentGroupId))
                {
                    return false;
                }

                if (groupId < 0)
                {
                    groupId = currentGroupId;
                }
                else if (groupId != currentGroupId)
                {
                    return false;
                }
            }

            List<int> members;
            return groupId >= 0
                && cadCurveObjectMembers.TryGetValue(groupId, out members)
                && members != null
                && members.Count == selectedIndices.Count
                && AreAllIndicesSelected(members);
        }

        private void SetSelectedIndex(int value)
        {
            if (value < -1 || value >= document.Elements.Count)
            {
                value = -1;
            }

            selectedIndices.Clear();
            selectedIndex = value;
            if (value >= 0)
            {
                selectedIndices.Add(value);
            }

            Invalidate();
            OnSelectionChanged();
        }

        private void SetPrimarySelectedIndex(int value)
        {
            if (value < 0 || value >= document.Elements.Count || !selectedIndices.Contains(value))
            {
                SetSelectedIndex(value);
                return;
            }

            if (selectedIndex == value)
            {
                return;
            }

            selectedIndex = value;
            Invalidate();
            OnSelectionChanged();
        }

        private void ClearSelection(bool notify)
        {
            bool changed = selectedIndex != -1 || selectedIndices.Count > 0;
            selectedIndex = -1;
            selectedIndices.Clear();

            if (changed)
            {
                Invalidate();
                if (notify)
                {
                    OnSelectionChanged();
                }
            }
        }

        private void SelectAllElements()
        {
            selectedIndices.Clear();
            int i;
            for (i = 0; i < document.Elements.Count; i++)
            {
                if (document.Elements[i] != null)
                {
                    selectedIndices.Add(i);
                }
            }

            selectedIndex = selectedIndices.Count > 0 ? GetSmallestSelectedIndex() : -1;
            Invalidate();
            OnSelectionChanged();
        }

        private void CaptureDragStartElements(bool useWholeSelection)
        {
            dragStartElements.Clear();

            if (useWholeSelection)
            {
                foreach (int index in selectedIndices)
                {
                    if (index >= 0 && index < document.Elements.Count && document.Elements[index] != null)
                    {
                        dragStartElements[index] = document.Elements[index].Clone();
                    }
                }
            }
            else if (selectedIndex >= 0 && selectedIndex < document.Elements.Count && document.Elements[selectedIndex] != null)
            {
                dragStartElements[selectedIndex] = document.Elements[selectedIndex].Clone();
            }
        }

        private void CompleteMarqueeSelection()
        {
            RectangleF selection = NormalizeRectangle(marqueeStartScreen, marqueeCurrentScreen);

            if (selection.Width < 4F && selection.Height < 4F)
            {
                ClearSelection(true);
                Invalidate();
                return;
            }

            List<int> matches = new List<int>();
            HashSet<int> handledGroups = new HashSet<int>();
            int i;

            for (i = 0; i < document.Elements.Count; i++)
            {
                CadShapeEditElement element = document.Elements[i];
                if (element == null)
                {
                    continue;
                }

                int groupId;
                if (cadCurveObjectGroupByElementIndex.TryGetValue(i, out groupId))
                {
                    if (!handledGroups.Add(groupId))
                    {
                        continue;
                    }

                    List<int> groupMembers;
                    if (!cadCurveObjectMembers.TryGetValue(groupId, out groupMembers) || groupMembers == null)
                    {
                        continue;
                    }

                    RectangleF groupBounds = GetElementGroupScreenBounds(groupMembers);
                    if (RectangleContains(selection, groupBounds))
                    {
                        int memberIndex;
                        for (memberIndex = 0; memberIndex < groupMembers.Count; memberIndex++)
                        {
                            matches.Add(groupMembers[memberIndex]);
                        }
                    }

                    continue;
                }

                RectangleF bounds = GetElementScreenBounds(element);
                if (RectangleContains(selection, bounds))
                {
                    matches.Add(i);
                }
            }

            selectedIndices.Clear();
            for (i = 0; i < matches.Count; i++)
            {
                selectedIndices.Add(matches[i]);
            }

            selectedIndex = matches.Count > 0 ? matches[0] : -1;
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
            using (SolidBrush background = new SolidBrush(Color.FromArgb(250, 251, 253)))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            EditorTransform transform = GetTransform();
            double visibleWorldWidth = Math.Max(ClientSize.Width / Math.Max(transform.Scale, 0.0001D), 100D);
            double visibleWorldHeight = Math.Max(ClientSize.Height / Math.Max(transform.Scale, 0.0001D), 100D);
            double step = GetGridStep(visibleWorldWidth);
            PointF topLeftWorld = ScreenToWorld(new Point(0, 0));
            PointF bottomRightWorld = ScreenToWorld(new Point(ClientSize.Width, ClientSize.Height));

            using (Pen minorPen = new Pen(Color.FromArgb(235, 238, 243), 1F))
            using (Pen axisPen = new Pen(Color.FromArgb(220, 225, 232), 1F))
            {
                double startX = Math.Floor(Math.Min(topLeftWorld.X, bottomRightWorld.X) / step) * step;
                double endX = Math.Max(topLeftWorld.X, bottomRightWorld.X) + step;
                int guard = 0;

                for (double x = startX; x <= endX && guard < 1000; x += step, guard++)
                {
                    PointF p1 = WorldToScreen(new PointF((float)x, (float)(Math.Min(topLeftWorld.Y, bottomRightWorld.Y) - visibleWorldHeight)));
                    PointF p2 = WorldToScreen(new PointF((float)x, (float)(Math.Max(topLeftWorld.Y, bottomRightWorld.Y) + visibleWorldHeight)));
                    g.DrawLine(Math.Abs(x) < step * 0.1D ? axisPen : minorPen, p1, p2);
                }

                double startY = Math.Floor(Math.Min(topLeftWorld.Y, bottomRightWorld.Y) / step) * step;
                double endY = Math.Max(topLeftWorld.Y, bottomRightWorld.Y) + step;
                guard = 0;

                for (double y = startY; y <= endY && guard < 1000; y += step, guard++)
                {
                    PointF p1 = WorldToScreen(new PointF((float)(Math.Min(topLeftWorld.X, bottomRightWorld.X) - visibleWorldWidth), (float)y));
                    PointF p2 = WorldToScreen(new PointF((float)(Math.Max(topLeftWorld.X, bottomRightWorld.X) + visibleWorldWidth), (float)y));
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
                bool selected = selectedIndices.Contains(i);
                bool primary = i == selectedIndex;

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

                        if (selected && cadCurveObjectGroupByElementIndex.ContainsKey(i))
                        {
                            using (Pen halo = new Pen(Color.FromArgb(72, 19, 104, 206), 6F))
                            {
                                halo.StartCap = LineCap.Round;
                                halo.EndCap = LineCap.Round;
                                g.DrawLine(halo, p1, p2);
                            }
                        }

                        g.DrawLine(pen, p1, p2);

                        if (selected && selectedIndices.Count == 1 && primary)
                        {
                            DrawHandle(g, p1, false);
                            DrawHandle(g, p2, false);
                            DrawLineRotationHandles(g, p1, p2);
                        }
                    }
                    else if (element.Type == "CIRCLE")
                    {
                        DrawArcOrCircle(g, pen, element, true);
                        if (selected)
                        {
                            DrawCircleSelection(g, element, primary && selectedIndices.Count == 1);
                        }
                    }
                    else if (element.Type == "ARC")
                    {
                        DrawArcOrCircle(g, pen, element, false);
                        if (selected)
                        {
                            DrawArcSelection(g, element, primary && selectedIndices.Count == 1);
                        }
                    }
                    else if (element.Type == "TEXT")
                    {
                        DrawTextElement(g, brush, element, selected);
                        if (selected && primary && selectedIndices.Count == 1)
                        {
                            DrawTextEditHandles(g, element);
                        }
                    }
                }
            }

            DrawSelectedCadCurveObjectBounds(g);
        }

        private void DrawSelectedCadCurveObjectBounds(Graphics g)
        {
            int groupId;
            if (!TryGetSingleSelectedCadCurveObjectGroup(out groupId))
            {
                return;
            }

            List<int> members;
            if (!cadCurveObjectMembers.TryGetValue(groupId, out members) || members == null)
            {
                return;
            }

            RectangleF bounds = GetElementGroupScreenBounds(members);
            if (bounds.IsEmpty)
            {
                return;
            }

            bounds.Inflate(3F, 3F);
            using (Pen border = new Pen(Color.FromArgb(19, 104, 206), 1F))
            {
                border.DashStyle = DashStyle.Dash;
                g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        private RectangleF GetElementGroupScreenBounds(List<int> members)
        {
            RectangleF result = RectangleF.Empty;
            bool found = false;

            if (members == null)
            {
                return result;
            }

            int i;
            for (i = 0; i < members.Count; i++)
            {
                int index = members[i];
                if (index < 0 || index >= document.Elements.Count || document.Elements[index] == null)
                {
                    continue;
                }

                RectangleF bounds = GetElementScreenBounds(document.Elements[index]);
                if (bounds.IsEmpty)
                {
                    continue;
                }

                result = found ? RectangleF.Union(result, bounds) : bounds;
                found = true;
            }

            return found ? result : RectangleF.Empty;
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

        private void DrawCircleSelection(Graphics g, CadShapeEditElement element, bool showResizeHandle)
        {
            EditorTransform transform = GetTransform();
            PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
            float radius = (float)(Math.Abs(element.Radius) * transform.Scale);
            RectangleF bounds = new RectangleF(center.X - radius - 4F, center.Y - radius - 4F, radius * 2F + 8F, radius * 2F + 8F);

            using (Pen halo = new Pen(Color.FromArgb(80, 19, 104, 206), 6F))
            using (Pen border = new Pen(Color.FromArgb(19, 104, 206), 1F))
            {
                halo.Alignment = PenAlignment.Center;
                g.DrawEllipse(halo, center.X - radius, center.Y - radius, radius * 2F, radius * 2F);
                border.DashStyle = DashStyle.Dash;
                g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }

            if (showResizeHandle)
            {
                PointF radiusHandle = GetCircleRadiusHandle(element);
                DrawHandle(g, radiusHandle, true);
            }
        }

        private void DrawArcSelection(Graphics g, CadShapeEditElement element, bool showHandles)
        {
            using (Pen halo = new Pen(Color.FromArgb(80, 19, 104, 206), 6F))
            {
                halo.StartCap = LineCap.Round;
                halo.EndCap = LineCap.Round;
                DrawArcOrCircle(g, halo, element, false);
            }

            if (!showHandles)
            {
                return;
            }

            PointF radiusHandle;
            PointF startHandle;
            PointF endHandle;
            PointF rotationHandle;
            GetArcEditHandles(element, out radiusHandle, out startHandle, out endHandle, out rotationHandle);

            PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
            using (Pen connector = new Pen(Color.FromArgb(120, 19, 104, 206), 1F))
            using (Pen ring = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                connector.DashStyle = DashStyle.Dot;
                g.DrawLine(connector, radiusHandle, rotationHandle);
                g.DrawEllipse(ring, rotationHandle.X - 5F, rotationHandle.Y - 5F, 10F, 10F);
                g.DrawLine(connector, center, startHandle);
                g.DrawLine(connector, center, endHandle);
            }

            DrawHandle(g, startHandle, false);
            DrawHandle(g, endHandle, false);
            DrawHandle(g, radiusHandle, true);
        }

        private void DrawTextEditHandles(Graphics g, CadShapeEditElement element)
        {
            RectangleF baseBounds = GetTextScreenBounds(element);
            PointF center = new PointF(baseBounds.X + baseBounds.Width / 2F, baseBounds.Y + baseBounds.Height / 2F);
            PointF localRotationAnchor = new PointF(center.X, baseBounds.Top);
            PointF rotationAnchor = RotatePoint(localRotationAnchor, center, (float)element.Rotation);
            PointF rotationHandle = GetTextRotationHandle(element);
            PointF localResizeAnchor = new PointF(baseBounds.Right, baseBounds.Bottom);
            PointF resizeAnchor = RotatePoint(localResizeAnchor, center, (float)element.Rotation);
            PointF resizeHandle = GetTextResizeHandle(element);

            using (Pen connector = new Pen(Color.FromArgb(120, 19, 104, 206), 1F))
            using (Pen ring = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                connector.DashStyle = DashStyle.Dot;
                g.DrawLine(connector, rotationAnchor, rotationHandle);
                g.DrawEllipse(ring, rotationHandle.X - 5F, rotationHandle.Y - 5F, 10F, 10F);
                g.DrawLine(connector, resizeAnchor, resizeHandle);
            }

            DrawHandle(g, resizeHandle, true);
        }

        private void DrawTextElement(Graphics g, Brush brush, CadShapeEditElement element, bool selected)
        {
            PointF center = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
            string text = element.Text == null ? "" : element.Text;
            float fontSize = GetTextFontSize(element);

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
            PointF snapped = currentWorld;
            snapped = SnapToExistingLineEndpoint(snapped, -1, null);
            PointF end = WorldToScreen(snapped);

            using (Pen pen = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, start, end);
            }

            DrawHandle(g, start, false);
        }

        private void DrawPendingCircle(Graphics g)
        {
            if (!hasPendingCircleCenter || mode != CadShapeEditorMode.AddCircle)
            {
                return;
            }

            PointF center = WorldToScreen(pendingCircleCenter);
            PointF currentWorld = ScreenToWorld(currentMouseScreen);
            double radiusWorld = Distance(pendingCircleCenter, currentWorld);
            float radius = (float)(radiusWorld * GetTransform().Scale);

            using (Pen pen = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2F, radius * 2F);
            }

            DrawHandle(g, center, false);
        }

        private void DrawPendingAngle(Graphics g)
        {
            if (!hasPendingAngleCenter || mode != CadShapeEditorMode.AddAngle)
            {
                return;
            }

            PointF centerScreen = WorldToScreen(pendingAngleCenter);
            PointF currentWorld = SnapToExistingLineEndpoint(ScreenToWorld(currentMouseScreen), -1, null);
            PointF currentScreen = WorldToScreen(currentWorld);

            using (Pen guidePen = new Pen(Color.FromArgb(120, 19, 104, 206), 1F))
            using (Pen arcPen = new Pen(Color.FromArgb(19, 104, 206), 1.7F))
            {
                guidePen.DashStyle = DashStyle.Dot;
                arcPen.DashStyle = DashStyle.Dash;
                DrawHandle(g, centerScreen, false);

                if (!hasPendingAngleStart)
                {
                    g.DrawLine(guidePen, centerScreen, currentScreen);
                    return;
                }

                PointF startScreen = WorldToScreen(pendingAngleStart);
                g.DrawLine(guidePen, centerScreen, startScreen);
                g.DrawLine(guidePen, centerScreen, currentScreen);

                double radius = Distance(pendingAngleCenter, pendingAngleStart);
                double startAngle = GetArcAngleDegrees(pendingAngleCenter, pendingAngleStart);
                double sweep = hasPendingAngleSweep
                    ? ClampManualAngleSweep(pendingAngleSweep)
                    : 0D;

                if (Math.Abs(sweep) >= 0.5D)
                {
                    CadShapeEditElement preview = new CadShapeEditElement();
                    preview.Type = "ARC";
                    preview.CX = pendingAngleCenter.X;
                    preview.CY = pendingAngleCenter.Y;
                    preview.Radius = radius;
                    preview.StartAngle = startAngle;
                    preview.EndAngle = startAngle + sweep;
                    DrawArcOrCircle(g, arcPen, preview, false);

                    PointF label = GetArcScreenPoint(preview, preview.StartAngle + sweep / 2D, 18F);
                    using (Font font = OviaFluentTheme.FontKorean(9F, FontStyle.Bold))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(19, 104, 206)))
                    {
                        string value = Math.Abs(sweep).ToString("0.#") + "°";
                        SizeF size = g.MeasureString(value, font);
                        g.DrawString(value, font, brush, label.X - size.Width / 2F, label.Y - size.Height / 2F);
                    }
                }

                DrawHandle(g, startScreen, false);
            }
        }

        private void DrawMarquee(Graphics g)
        {
            if (!isMarqueeSelecting)
            {
                return;
            }

            RectangleF rect = NormalizeRectangle(marqueeStartScreen, marqueeCurrentScreen);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(35, 19, 104, 206)))
            using (Pen border = new Pen(Color.FromArgb(19, 104, 206), 1F))
            {
                border.DashStyle = DashStyle.Dash;
                g.FillRectangle(fill, rect);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawOverlay(Graphics g)
        {
            string modeText;
            string guide;

            if (mode == CadShapeEditorMode.AddLine)
            {
                modeText = "연속 선 그리기";
                guide = "끝점은 기존 선 끝점에 자동 연결됩니다. Enter·Esc·우클릭으로 종료합니다.";
            }
            else if (mode == CadShapeEditorMode.AddCircle)
            {
                modeText = "원 추가";
                guide = "중심점을 클릭한 뒤 반지름 지점을 클릭합니다. 선택 후 십자 핸들로 크기를 조절합니다.";
            }
            else if (mode == CadShapeEditorMode.AddAngle)
            {
                modeText = "각도 추가";
                guide = "중심점과 시작 방향을 클릭한 뒤 마우스를 원하는 방향으로 돌려 최대 270°까지 만든 다음 끝 위치를 클릭합니다.";
            }
            else if (mode == CadShapeEditorMode.AddText)
            {
                modeText = "문자 추가";
                guide = "문자를 놓을 위치를 클릭하면 즉시 값을 입력할 수 있습니다. 선택 후 회전 핸들과 십자 크기 핸들로 방향·크기를 조절할 수 있습니다.";
            }
            else
            {
                int selectedObjectCount = GetSelectedObjectCount();
                modeText = IsSingleCadCurveObjectSelected
                    ? "곡선 객체 선택"
                    : (selectedObjectCount > 1
                        ? selectedObjectCount.ToString() + "개 선택"
                        : "선택·이동");
                guide = "빈 공간을 드래그하면 영역 안의 요소를 함께 선택합니다. 문자·치수는 더블클릭하여 수정합니다.";
            }

            using (Font titleFont = OviaFluentTheme.FontKorean(9F, FontStyle.Bold))
            using (Font guideFont = OviaFluentTheme.FontKorean(8F, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(55, 65, 81)))
            using (SolidBrush guideBrush = new SolidBrush(Color.FromArgb(100, 110, 125)))
            {
                g.DrawString(modeText + "  |  " + Math.Round(zoom * 100F).ToString("0") + "%", titleFont, titleBrush, 12F, 10F);
                g.DrawString(guide, guideFont, guideBrush, 12F, 32F);
            }
        }

        private void DrawHandle(Graphics g, PointF center, bool resizeHandle)
        {
            RectangleF rect = new RectangleF(center.X - 4.5F, center.Y - 4.5F, 9F, 9F);

            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                if (resizeHandle)
                {
                    g.FillEllipse(brush, rect);
                    g.DrawEllipse(pen, rect);
                    g.DrawLine(pen, center.X - 2.5F, center.Y, center.X + 2.5F, center.Y);
                    g.DrawLine(pen, center.X, center.Y - 2.5F, center.X, center.Y + 2.5F);
                }
                else
                {
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        private void DrawLineRotationHandles(Graphics g, PointF p1, PointF p2)
        {
            PointF rotate1;
            PointF rotate2;
            GetLineRotationHandles(p1, p2, out rotate1, out rotate2);

            using (Pen connector = new Pen(Color.FromArgb(120, 19, 104, 206), 1F))
            using (Pen ring = new Pen(Color.FromArgb(19, 104, 206), 1.5F))
            {
                connector.DashStyle = DashStyle.Dot;
                g.DrawLine(connector, p1, rotate1);
                g.DrawLine(connector, p2, rotate2);
                g.DrawEllipse(ring, rotate1.X - 5F, rotate1.Y - 5F, 10F, 10F);
                g.DrawEllipse(ring, rotate2.X - 5F, rotate2.Y - 5F, 10F, 10F);
            }
        }

        private void GetLineRotationHandles(PointF p1, PointF p2, out PointF rotate1, out PointF rotate2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.001F)
            {
                rotate1 = new PointF(p1.X - 18F, p1.Y);
                rotate2 = new PointF(p2.X + 18F, p2.Y);
                return;
            }

            float ux = dx / length;
            float uy = dy / length;
            rotate1 = new PointF(p1.X - ux * 20F, p1.Y - uy * 20F);
            rotate2 = new PointF(p2.X + ux * 20F, p2.Y + uy * 20F);
        }

        private PointF GetCircleRadiusHandle(CadShapeEditElement element)
        {
            PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
            float radius = (float)(Math.Abs(element.Radius) * GetTransform().Scale);
            return new PointF(center.X + radius, center.Y);
        }

        private void GetArcEditHandles(
            CadShapeEditElement element,
            out PointF radiusHandle,
            out PointF startHandle,
            out PointF endHandle,
            out PointF rotationHandle)
        {
            double sweep = GetArcSweep(element);
            double middleAngle = element.StartAngle + sweep / 2D;
            startHandle = GetArcScreenPoint(element, element.StartAngle, 0F);
            endHandle = GetArcScreenPoint(element, element.StartAngle + sweep, 0F);
            radiusHandle = GetArcScreenPoint(element, middleAngle, 0F);
            rotationHandle = GetArcScreenPoint(element, middleAngle, 24F);
        }

        private PointF GetTextRotationHandle(CadShapeEditElement element)
        {
            RectangleF baseBounds = GetTextScreenBounds(element);
            PointF center = new PointF(baseBounds.X + baseBounds.Width / 2F, baseBounds.Y + baseBounds.Height / 2F);
            PointF localHandle = new PointF(center.X, baseBounds.Top - 24F);
            return RotatePoint(localHandle, center, (float)element.Rotation);
        }

        private PointF GetTextResizeHandle(CadShapeEditElement element)
        {
            RectangleF baseBounds = GetTextScreenBounds(element);
            PointF center = new PointF(baseBounds.X + baseBounds.Width / 2F, baseBounds.Y + baseBounds.Height / 2F);
            PointF localHandle = new PointF(baseBounds.Right + 14F, baseBounds.Bottom + 10F);
            return RotatePoint(localHandle, center, (float)element.Rotation);
        }

        private PointF GetArcScreenPoint(CadShapeEditElement element, double angleDegrees, float extraRadiusPixels)
        {
            PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
            double radians = angleDegrees * Math.PI / 180D;
            float radius = (float)(Math.Abs(element.Radius) * GetTransform().Scale) + extraRadiusPixels;
            return new PointF(
                center.X + (float)Math.Cos(radians) * radius,
                center.Y - (float)Math.Sin(radians) * radius
            );
        }

        private List<PointF> GetArcScreenPoints(CadShapeEditElement element)
        {
            List<PointF> points = new List<PointF>();
            double sweep = GetArcSweep(element);
            int segments = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / 6D));
            int i;

            for (i = 0; i <= segments; i++)
            {
                double angle = element.StartAngle + sweep * i / segments;
                points.Add(GetArcScreenPoint(element, angle, 0F));
            }

            return points;
        }

        private float DistancePointToArc(Point point, CadShapeEditElement element)
        {
            List<PointF> points = GetArcScreenPoints(element);
            float best = Single.MaxValue;
            int i;

            for (i = 1; i < points.Count; i++)
            {
                float distance = DistancePointToSegment(point, points[i - 1], points[i]);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private double GetArcSweep(CadShapeEditElement element)
        {
            double sweep = element.EndAngle - element.StartAngle;
            if (Math.Abs(sweep) < 0.1D)
            {
                return 360D;
            }

            return sweep;
        }

        private double GetArcAngleDegrees(PointF center, PointF point)
        {
            double angle = Math.Atan2(center.Y - point.Y, point.X - center.X) * 180D / Math.PI;
            return NormalizeDegrees(angle);
        }

        private double NormalizeDegrees(double value)
        {
            double normalized = value % 360D;
            if (normalized < 0D)
            {
                normalized += 360D;
            }

            return normalized;
        }

        private double NormalizeSignedDegrees(double value)
        {
            double normalized = NormalizeDegrees(value);
            if (normalized > 180D)
            {
                normalized -= 360D;
            }

            return normalized;
        }

        private void BeginInlineTextEdit(int elementIndex)
        {
            if (elementIndex < 0 || elementIndex >= document.Elements.Count)
            {
                return;
            }

            CadShapeEditElement element = document.Elements[elementIndex];
            if (element == null || element.Type != "TEXT")
            {
                return;
            }

            if (inlineTextEditor != null)
            {
                if (inlineTextElementIndex == elementIndex)
                {
                    inlineTextEditor.Focus();
                    inlineTextEditor.SelectAll();
                    return;
                }

                CommitInlineTextEdit();
            }

            RectangleF bounds = GetTextScreenBounds(element);
            int width = Math.Max(110, (int)Math.Ceiling(bounds.Width + 30F));
            width = Math.Min(width, Math.Max(110, ClientSize.Width - 24));
            int height = Math.Max(30, (int)Math.Ceiling(Math.Min(bounds.Height + 12F, 48F)));
            int left = (int)Math.Round(bounds.X + bounds.Width / 2F - width / 2F);
            int top = (int)Math.Round(bounds.Y + bounds.Height / 2F - height / 2F);
            left = Math.Max(8, Math.Min(left, ClientSize.Width - width - 8));
            top = Math.Max(8, Math.Min(top, ClientSize.Height - height - 8));

            inlineTextElementIndex = elementIndex;
            inlineTextEditor = new TextBox();
            inlineTextEditor.Text = element.Text == null ? "" : element.Text;
            inlineTextEditor.Font = OviaFluentTheme.FontInput(Math.Max(10F, Math.Min(18F, GetTextFontSize(element))), FontStyle.Regular);
            inlineTextEditor.BorderStyle = BorderStyle.FixedSingle;
            inlineTextEditor.Location = new Point(left, top);
            inlineTextEditor.Size = new Size(width, height);
            inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            inlineTextEditor.Leave += InlineTextEditor_Leave;
            Controls.Add(inlineTextEditor);
            inlineTextEditor.BringToFront();
            inlineTextEditor.Focus();
            inlineTextEditor.SelectAll();
        }

        private void InlineTextEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitInlineTextEdit();
                Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CancelInlineTextEdit();
                Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void InlineTextEditor_Leave(object sender, EventArgs e)
        {
            if (!inlineEditClosing)
            {
                CommitInlineTextEdit();
            }
        }

        private void EndInlineTextEdit(bool commit)
        {
            if (inlineTextEditor == null || inlineEditClosing)
            {
                return;
            }

            inlineEditClosing = true;
            TextBox editorBox = inlineTextEditor;
            int elementIndex = inlineTextElementIndex;
            string value = editorBox.Text == null ? "" : editorBox.Text;
            inlineTextEditor = null;
            inlineTextElementIndex = -1;
            editorBox.KeyDown -= InlineTextEditor_KeyDown;
            editorBox.Leave -= InlineTextEditor_Leave;
            Controls.Remove(editorBox);
            editorBox.Dispose();
            inlineEditClosing = false;

            if (commit && elementIndex >= 0 && elementIndex < document.Elements.Count)
            {
                CadShapeEditElement element = document.Elements[elementIndex];
                if (element != null && element.Type == "TEXT" && !String.Equals(element.Text, value, StringComparison.Ordinal))
                {
                    PushUndo();
                    element.Text = value;
                    element.HasBounds = false;
                    SetSelectedIndex(elementIndex);
                    Invalidate();
                    OnDocumentChanged();
                }
            }
        }

        private bool IsPointInsideText(Point screenPoint, CadShapeEditElement element)
        {
            RectangleF unrotated = GetTextScreenBounds(element);
            PointF center = new PointF(unrotated.X + unrotated.Width / 2F, unrotated.Y + unrotated.Height / 2F);
            PointF local = RotatePoint(new PointF(screenPoint.X, screenPoint.Y), center, -(float)element.Rotation);
            return unrotated.Contains(local);
        }

        private RectangleF GetTextScreenBounds(CadShapeEditElement element)
        {
            PointF center = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
            string text = element.Text == null ? "" : element.Text;
            float fontSize = GetTextFontSize(element);

            using (Font font = OviaFluentTheme.FontKorean(fontSize, FontStyle.Regular, GraphicsUnit.Point))
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                SizeF size = graphics.MeasureString(text == "" ? " " : text, font);
                return new RectangleF(
                    center.X - size.Width / 2F - 6F,
                    center.Y - size.Height / 2F - 4F,
                    size.Width + 12F,
                    size.Height + 8F
                );
            }
        }

        private RectangleF GetElementScreenBounds(CadShapeEditElement element)
        {
            if (element.Type == "LINE")
            {
                PointF p1 = WorldToScreen(new PointF((float)element.X1, (float)element.Y1));
                PointF p2 = WorldToScreen(new PointF((float)element.X2, (float)element.Y2));
                float minX = Math.Min(p1.X, p2.X) - 4F;
                float minY = Math.Min(p1.Y, p2.Y) - 4F;
                float maxX = Math.Max(p1.X, p2.X) + 4F;
                float maxY = Math.Max(p1.Y, p2.Y) + 4F;
                return RectangleF.FromLTRB(minX, minY, maxX, maxY);
            }

            if (element.Type == "TEXT")
            {
                RectangleF baseBounds = GetTextScreenBounds(element);
                if (Math.Abs(element.Rotation) <= 0.01D)
                {
                    return baseBounds;
                }

                PointF center = new PointF(baseBounds.X + baseBounds.Width / 2F, baseBounds.Y + baseBounds.Height / 2F);
                PointF[] corners = new PointF[]
                {
                    RotatePoint(new PointF(baseBounds.Left, baseBounds.Top), center, (float)element.Rotation),
                    RotatePoint(new PointF(baseBounds.Right, baseBounds.Top), center, (float)element.Rotation),
                    RotatePoint(new PointF(baseBounds.Right, baseBounds.Bottom), center, (float)element.Rotation),
                    RotatePoint(new PointF(baseBounds.Left, baseBounds.Bottom), center, (float)element.Rotation)
                };
                return BoundsFromPoints(corners);
            }

            if (element.Type == "CIRCLE")
            {
                PointF center = WorldToScreen(new PointF((float)element.CX, (float)element.CY));
                float radius = (float)(Math.Abs(element.Radius) * GetTransform().Scale);
                return new RectangleF(center.X - radius - 4F, center.Y - radius - 4F, radius * 2F + 8F, radius * 2F + 8F);
            }

            if (element.Type == "ARC")
            {
                List<PointF> arcPoints = GetArcScreenPoints(element);
                RectangleF arcBounds = BoundsFromPoints(arcPoints.ToArray());
                return RectangleF.FromLTRB(
                    arcBounds.Left - 4F,
                    arcBounds.Top - 4F,
                    arcBounds.Right + 4F,
                    arcBounds.Bottom + 4F
                );
            }

            return RectangleF.Empty;
        }

        private float GetTextFontSize(CadShapeEditElement element)
        {
            double elementHeight = element == null ? 2.5D : Math.Max(element.Height, 0.1D);
            double heightFactor = Math.Sqrt(Math.Max(0.55D, Math.Min(3D, elementHeight / 2.5D)));
            double textScale = element == null
                ? 1D
                : Math.Max(MinTextScale, Math.Min(MaxTextScale, element.TextScale));
            float zoomRatio = zoom / DefaultFitZoom;
            float size = (float)(12F * zoomRatio * heightFactor * textScale);
            return Math.Max(5F, Math.Min(180F, size));
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
            float paddingLeft = 36F;
            float paddingRight = 36F;
            float paddingTop = 50F;
            float paddingBottom = 36F;
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

        private List<int> GetSelectedIndexesAscending()
        {
            List<int> indexes = new List<int>();
            foreach (int index in selectedIndices)
            {
                indexes.Add(index);
            }
            indexes.Sort();
            return indexes;
        }

        private List<int> GetSelectedIndexesDescending()
        {
            List<int> indexes = GetSelectedIndexesAscending();
            indexes.Reverse();
            return indexes;
        }

        private int GetSmallestSelectedIndex()
        {
            int smallest = Int32.MaxValue;
            foreach (int index in selectedIndices)
            {
                if (index < smallest)
                {
                    smallest = index;
                }
            }
            return smallest == Int32.MaxValue ? -1 : smallest;
        }

        private RectangleF NormalizeRectangle(Point first, Point second)
        {
            float left = Math.Min(first.X, second.X);
            float top = Math.Min(first.Y, second.Y);
            float right = Math.Max(first.X, second.X);
            float bottom = Math.Max(first.Y, second.Y);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private bool RectangleContains(RectangleF outer, RectangleF inner)
        {
            return inner.Width >= 0F
                && inner.Height >= 0F
                && outer.Left <= inner.Left
                && outer.Top <= inner.Top
                && outer.Right >= inner.Right
                && outer.Bottom >= inner.Bottom;
        }

        private PointF RotatePoint(PointF point, PointF center, float degrees)
        {
            double radians = degrees * Math.PI / 180D;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            return new PointF(
                (float)(center.X + dx * cos - dy * sin),
                (float)(center.Y + dx * sin + dy * cos)
            );
        }

        private RectangleF BoundsFromPoints(PointF[] points)
        {
            if (points == null || points.Length == 0)
            {
                return RectangleF.Empty;
            }

            float minX = points[0].X;
            float minY = points[0].Y;
            float maxX = points[0].X;
            float maxY = points[0].Y;
            int i;

            for (i = 1; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxX = Math.Max(maxX, points[i].X);
                maxY = Math.Max(maxY, points[i].Y);
            }

            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
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
