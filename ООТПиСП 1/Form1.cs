namespace ООТПиСП_1
{
    using System.Drawing.Drawing2D;
    using System.Drawing;
    using System.Windows.Forms;
    using System.ComponentModel;
    public partial class Form1 : Form
    {
        private List<Shape> shapes = new List<Shape>();
        private Shape? selectedShape;
        private bool dragging = false;
        private PointF dragOffset;
        private float zoom = 1.0f;
        private PointF panOffset = new PointF(0, 0);
        private bool panning = false;
        private Point panStart;
        private Panel propertiesPanel;
        private Label selectedShapeNameLabel;
        private ComboBox addTypeComboBox;
        private Button addButton;
        private Panel creationPanel;
        private Panel canvasPanel;
        private bool showGrid = false;
        private TableLayoutPanel sizeInputPanel;
        private List<TextBox> sizeTextBoxes = new List<TextBox>();
        private bool showAxes = false;
        private enum ResizeHandle
        {
            None,
            TopLeft, TopMiddle, TopRight,
            MiddleLeft, MiddleRight,
            BottomLeft, BottomMiddle, BottomRight
        }
        private ResizeHandle activeResizeHandle = ResizeHandle.None;
        private bool resizing = false;
        private RectangleF originalBounds;
        private PointF resizeOriginalAnchor;
        private PointF resizeStartWorld;
        private bool rotatingWithThumb = false;
        private PointF rotationHandleScreenCenter = PointF.Empty;
        private float rotationHandleScreenRadius = 8f;
        private bool showAnchorPoint = false;
        private PointF lastMouseWorld = PointF.Empty;
        private Point lastMouseScreen = Point.Empty;
        private bool mouseInsideCanvas = false;
        private System.Windows.Forms.Timer cursorTimer;
        private float resizeStartWidth = 0f;
        private float resizeStartHeight = 0f;
        private RectangleF resizeStartLocalBounds;
        private GroupBox propertiesSizeGroupBox;
        private TableLayoutPanel propertiesSizeInputPanel;
        private List<TextBox> propertiesSizeTextBoxes = new List<TextBox>();
        private bool updatingPropertiesSizes = false;
        private GroupBox propertiesColorGroupBox;
        private GroupBox propertiesTransformGroupBox;
        private NumericUpDown propRotationUpDown;
        private NumericUpDown propAUpDown;
        private NumericUpDown propRUpDown;
        private NumericUpDown propGUpDown;
        private NumericUpDown propBUpDown;
        private GroupBox propertiesBoundsGroupBox;
        private Label boundsTopLeftLabel;
        private Label boundsBottomRightLabel;
        private GroupBox propertiesEdgesGroupBox;
        private FlowLayoutPanel propertiesEdgesPanel;
        public Form1()
        {
            InitializeComponent();
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            this.DoubleBuffered = true;
            this.UpdateStyles();
            InitCanvas();
            InitPropertiesPanel();
            InitCreationPanel();
            InitShapes();
            this.KeyDown += Form1_KeyDown;
        }
        public class DiamondShape : Shape
        {
            [Category("Размеры")]
            public float Top { get; set; }
            [Category("Размеры")]
            public float Right { get; set; }
            [Category("Размеры")]
            public float Bottom { get; set; }
            [Category("Размеры")]
            public float Left { get; set; }
            public DiamondShape(PointF anchor, float top, float right, float bottom, float left) : base(anchor)
            {
                Top = top; Right = right; Bottom = bottom; Left = left;
                for (int i = 0; i < 4; i++) Edges.Add(new Edge());
            }
            public DiamondShape(PointF anchor, List<float> sides) : this(anchor,
                sides.Count > 0 ? sides[0] : 50f,
                sides.Count > 1 ? sides[1] : 50f,
                sides.Count > 2 ? sides[2] : 50f,
                sides.Count > 3 ? sides[3] : 50f) { }
            private PointF[] GetPoints()
            {
                // local coordinates centered at Anchor
                var ptsLocal = new[] {
                    new PointF(0f, -Top),
                    new PointF(Right, 0f),
                    new PointF(0f, Bottom),
                    new PointF(-Left, 0f)
                };
                return ptsLocal.Select(p => LocalToWorld(p)).ToArray();
            }
            public override void Draw(Graphics g)
            {
                var pts = GetPoints();
                using (var brush = new SolidBrush(FillColor)) g.FillPolygon(brush, pts);
                EdgeRenderer.DrawPolygonEdges(g, pts, Edges);
            }
            public override bool ContainsPoint(PointF p)
            {
                var pts = GetPoints();
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(pts);
                    return path.IsVisible(p);
                }
            }
            public override RectangleF GetBounds()
            {
                var pts = GetPoints();
                float minX = pts.Min(p => p.X);
                float minY = pts.Min(p => p.Y);
                float maxX = pts.Max(p => p.X);
                float maxY = pts.Max(p => p.Y);
                return new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
            public override void Resize(RectangleF newBounds)
            {
                // scale distances proportionally to new bounds while keeping anchor as center
                float halfW = newBounds.Width / 2f;
                float halfH = newBounds.Height / 2f;
                // map old bounds to new to compute scale
                var old = GetBounds();
                float sx = old.Width > 0 ? newBounds.Width / old.Width : 1f;
                float sy = old.Height > 0 ? newBounds.Height / old.Height : 1f;
                Top *= sy; Bottom *= sy; Left *= sx; Right *= sx;
                Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
            }
            public override string[] GetSideLabels() => new[] { "Верх (top):", "Право (right):", "Низ (bottom):", "Лево (left):" };
            public override float[] GetCurrentSides() => new float[] { Top, Right, Bottom, Left };
            public override void UpdateFromSides(float[] newSides)
            {
                if (newSides.Length >= 4)
                {
                    Top = Math.Max(newSides[0], 1f);
                    Right = Math.Max(newSides[1], 1f);
                    Bottom = Math.Max(newSides[2], 1f);
                    Left = Math.Max(newSides[3], 1f);
                }
            }
        }

        private bool AreAllEdgesUniform(List<Edge> edges)
        {
            if (edges.Count == 0) return false;
            var firstColor = edges[0].Color.ToArgb();
            var firstWidth = edges[0].Width;
            foreach (var e in edges)
            {
                if (e.Color.ToArgb() != firstColor || Math.Abs(e.Width - firstWidth) > 0.01f)
                    return false;
            }
            return true;
        }

        private void DrawResizeHandles(Graphics g, Shape shape)
        {
            if (shape == null) return;
            RectangleF bounds = shape.GetBounds();
            float handleSize = 8f / zoom;
            PointF[] handlePositions;
            // For triangle, place handles at bounds corners and midpoints of sides
            if (shape is TriangleShape)
            {
                var tl = new PointF(bounds.Left, bounds.Top);
                var tr = new PointF(bounds.Right, bounds.Top);
                var bl = new PointF(bounds.Left, bounds.Bottom);
                var br = new PointF(bounds.Right, bounds.Bottom);
                var tm = new PointF((tl.X + tr.X) / 2f, (tl.Y + tr.Y) / 2f);
                var bm = new PointF((bl.X + br.X) / 2f, (bl.Y + br.Y) / 2f);
                var ml = new PointF((tl.X + bl.X) / 2f, (tl.Y + bl.Y) / 2f);
                var mr = new PointF((tr.X + br.X) / 2f, (tr.Y + br.Y) / 2f);
                // Order must match ResizeHandle enum: TL, TM, TR, ML, MR, BL, BM, BR
                handlePositions = new[] { tl, tm, tr, ml, mr, bl, bm, br };
            }
            else
            {
                float halfW = bounds.Width / 2f;
                float halfH = bounds.Height / 2f;
                PointF[] localPositions = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(-halfW, 0),
                    new PointF(halfW, 0),
                    new PointF(-halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(halfW, halfH)
                };
                handlePositions = localPositions.Select(p => shape.LocalToWorld(p)).ToArray();
            }
            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.Black, 1))
            {
                foreach (var pos in handlePositions)
                {
                    RectangleF rect = new RectangleF(
                        pos.X - handleSize / 2,
                        pos.Y - handleSize / 2,
                        handleSize, handleSize);
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }
        private ResizeHandle GetResizeHandleAtPoint(Shape shape, PointF worldPoint)
        {
            if (shape == null) return ResizeHandle.None;
            RectangleF bounds = shape.GetBounds();
            float handleSize = 10f / zoom;
            PointF[] worldPositions;
            if (shape is TriangleShape)
            {
                var tl = new PointF(bounds.Left, bounds.Top);
                var tr = new PointF(bounds.Right, bounds.Top);
                var bl = new PointF(bounds.Left, bounds.Bottom);
                var br = new PointF(bounds.Right, bounds.Bottom);
                var tm = new PointF((tl.X + tr.X) / 2f, (tl.Y + tr.Y) / 2f);
                var bm = new PointF((bl.X + br.X) / 2f, (bl.Y + br.Y) / 2f);
                var ml = new PointF((tl.X + bl.X) / 2f, (tl.Y + bl.Y) / 2f);
                var mr = new PointF((tr.X + br.X) / 2f, (tr.Y + br.Y) / 2f);
                worldPositions = new[] { tl, tm, tr, ml, mr, bl, bm, br };
            }
            else
            {
                float halfW = bounds.Width / 2f;
                float halfH = bounds.Height / 2f;
                PointF[] localPositions = new PointF[]
                {
                    new PointF(-halfW, -halfH),
                    new PointF(0, -halfH),
                    new PointF(halfW, -halfH),
                    new PointF(-halfW, 0),
                    new PointF(halfW, 0),
                    new PointF(-halfW, halfH),
                    new PointF(0, halfH),
                    new PointF(halfW, halfH)
                };
                worldPositions = localPositions.Select(p => shape.LocalToWorld(p)).ToArray();
            }
            for (int i = 0; i < worldPositions.Length; i++)
            {
                if (IsPointNearHandle(worldPoint, worldPositions[i], handleSize))
                    return (ResizeHandle)(i + 1);
            }
            return ResizeHandle.None;
        }
        private bool IsPointNearHandle(PointF p, PointF handle, float threshold)
        {
            return Math.Abs(p.X - handle.X) <= threshold && Math.Abs(p.Y - handle.Y) <= threshold;
        }
        private RectangleF ComputeNewBounds(RectangleF original, ResizeHandle handle, PointF currentWorld)
        {
            float left = original.Left;
            float top = original.Top;
            float right = original.Right;
            float bottom = original.Bottom;
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                    left = currentWorld.X;
                    top = currentWorld.Y;
                    break;
                case ResizeHandle.TopMiddle:
                    top = currentWorld.Y;
                    break;
                case ResizeHandle.TopRight:
                    right = currentWorld.X;
                    top = currentWorld.Y;
                    break;
                case ResizeHandle.MiddleLeft:
                    left = currentWorld.X;
                    break;
                case ResizeHandle.MiddleRight:
                    right = currentWorld.X;
                    break;
                case ResizeHandle.BottomLeft:
                    left = currentWorld.X;
                    bottom = currentWorld.Y;
                    break;
                case ResizeHandle.BottomMiddle:
                    bottom = currentWorld.Y;
                    break;
                case ResizeHandle.BottomRight:
                    right = currentWorld.X;
                    bottom = currentWorld.Y;
                    break;
            }
            float minSize = 10f;
            if (right - left < minSize)
            {
                if (handle == ResizeHandle.TopLeft || handle == ResizeHandle.MiddleLeft || handle == ResizeHandle.BottomLeft)
                    left = right - minSize;
                else
                    right = left + minSize;
            }
            if (bottom - top < minSize)
            {
                if (handle == ResizeHandle.TopLeft || handle == ResizeHandle.TopMiddle || handle == ResizeHandle.TopRight)
                    top = bottom - minSize;
                else
                    bottom = top + minSize;
            }
            return new RectangleF(left, top, right - left, bottom - top);
        }
        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && canvasPanel.Focused)
            {
                if (selectedShape != null)
                {
                    shapes.Remove(selectedShape);
                    selectedShape = null;
                    showAnchorPoint = false;
                    UpdateSelectedShapeName();
                    UpdatePropertiesSizeInputs();
                    canvasPanel?.Invalidate();
                }
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.R || e.KeyCode == Keys.K)
            {
                if (propertiesPanel != null)
                {
                    propertiesPanel.Visible = !propertiesPanel.Visible;
                    canvasPanel?.Invalidate();
                    canvasPanel?.Focus();
                }
                e.Handled = true;
            }
            if (e.KeyCode == Keys.C || e.KeyCode == Keys.K)
            {
                if (creationPanel != null)
                {
                    creationPanel.Visible = !creationPanel.Visible;
                    canvasPanel?.Invalidate();
                    canvasPanel?.Focus();
                }
                e.Handled = true;
            }
            if (e.KeyCode == Keys.A)
            {
                showAxes = !showAxes;
                canvasPanel?.Invalidate();
                e.Handled = true;
            }
            if (e.KeyCode == Keys.G)
            {
                showGrid = !showGrid;
                canvasPanel?.Invalidate();
                e.Handled = true;
            }
        }
        private void InitCanvas()
        {
            canvasPanel = new DoubleBufferedPanel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.WhiteSmoke
            };
            canvasPanel.Paint += CanvasPanel_Paint;
            canvasPanel.MouseDown += Form1_MouseDown;
            canvasPanel.MouseMove += Form1_MouseMove;
            canvasPanel.MouseEnter += (s, e) => { mouseInsideCanvas = true; };
            canvasPanel.MouseLeave += (s, e) => { mouseInsideCanvas = false; canvasPanel?.Invalidate(); };
            canvasPanel.MouseUp += Form1_MouseUp;
            canvasPanel.MouseWheel += CanvasPanel_MouseWheel;
            canvasPanel.TabStop = true;
            this.Controls.Add(canvasPanel);

            // start polling cursor to update coordinates continuously
            cursorTimer = new System.Windows.Forms.Timer();
            cursorTimer.Interval = 40; // ~25 Hz
            cursorTimer.Tick += (s, e) =>
            {
                if (canvasPanel == null || !canvasPanel.IsHandleCreated) return;
                var screenPos = Cursor.Position;
                var clientPos = canvasPanel.PointToClient(screenPos);
                bool inside = clientPos.X >= 0 && clientPos.Y >= 0 && clientPos.X < canvasPanel.ClientSize.Width && clientPos.Y < canvasPanel.ClientSize.Height;
                mouseInsideCanvas = inside;
                // update last positions
                if (inside)
                {
                    lastMouseScreen = clientPos;
                    try { lastMouseWorld = ScreenToWorld(clientPos); }
                    catch { }
                }
                // always invalidate overlay so text updates (e.g., zoom changes)
                canvasPanel.Invalidate();
            };
            cursorTimer.Start();
        }
        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                this.DoubleBuffered = true;
                this.UpdateStyles();
            }
        }
        private void InitPropertiesPanel()
        {
            propertiesPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 460,
                BackColor = SystemColors.Control,
                Padding = new Padding(8),
                AutoScroll = true
            };
            selectedShapeNameLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Arial", 10, FontStyle.Bold),
                Text = "Нет выделения"
            };
            propertiesColorGroupBox = new GroupBox
            {
                Text = "Цвет заливки (RGBA)",
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                AutoSize = true,
                Visible = true
            };
            var colorPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            propAUpDown = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 60, Value = 255 };
            propRUpDown = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 60, Value = 200 };
            propGUpDown = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 60, Value = 200 };
            propBUpDown = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 60, Value = 200 };
            propAUpDown.ValueChanged += PropColor_ValueChanged;
            propRUpDown.ValueChanged += PropColor_ValueChanged;
            propGUpDown.ValueChanged += PropColor_ValueChanged;
            propBUpDown.ValueChanged += PropColor_ValueChanged;
            colorPanel.Controls.Add(new Label { Text = "A:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            colorPanel.Controls.Add(propAUpDown, 1, 0);
            colorPanel.Controls.Add(new Label { Text = "R:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
            colorPanel.Controls.Add(propRUpDown, 1, 1);
            colorPanel.Controls.Add(new Label { Text = "G:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 2);
            colorPanel.Controls.Add(propGUpDown, 1, 2);
            colorPanel.Controls.Add(new Label { Text = "B:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 3);
            colorPanel.Controls.Add(propBUpDown, 1, 3);
            propertiesColorGroupBox.Controls.Add(colorPanel);
            propertiesTransformGroupBox = new GroupBox
            {
                Text = "Трансформация",
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                AutoSize = true,
                Visible = true
            };
            var transformPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            transformPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            transformPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            propRotationUpDown = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 1, Increment = 1M, Width = 80, Value = 0 };
            propRotationUpDown.ValueChanged += PropRotation_ValueChanged;
            transformPanel.Controls.Add(new Label { Text = "Угол (°):", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            transformPanel.Controls.Add(propRotationUpDown, 1, 0);
            propertiesTransformGroupBox.Controls.Add(transformPanel);
            propertiesEdgesGroupBox = new GroupBox
            {
                Text = "Грани",
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                AutoSize = true,
                Visible = true
            };
            propertiesEdgesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            propertiesEdgesGroupBox.Controls.Add(propertiesEdgesPanel);
            propertiesSizeGroupBox = new GroupBox
            {
                Text = "Размеры по сторонам",
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                AutoSize = true,
                Visible = false
            };
            propertiesSizeInputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 5),
                ColumnCount = 2,
                RowCount = 0
            };
            propertiesSizeInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            propertiesSizeInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            propertiesSizeGroupBox.Controls.Add(propertiesSizeInputPanel);
            propertiesBoundsGroupBox = new GroupBox
            {
                Text = "Границы (виртуальные)",
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                AutoSize = true,
                Visible = true
            };
            boundsTopLeftLabel = new Label { Text = "TL: ", Dock = DockStyle.Top, Height = 20 };
            boundsBottomRightLabel = new Label { Text = "BR: ", Dock = DockStyle.Top, Height = 20 };
            propertiesBoundsGroupBox.Controls.Add(boundsBottomRightLabel);
            propertiesBoundsGroupBox.Controls.Add(boundsTopLeftLabel);
            propertiesPanel.Controls.Add(propertiesSizeGroupBox);
            propertiesPanel.Controls.Add(propertiesEdgesGroupBox);
            propertiesPanel.Controls.Add(propertiesColorGroupBox);
            propertiesPanel.Controls.Add(propertiesTransformGroupBox);
            propertiesPanel.Controls.Add(propertiesBoundsGroupBox);
            propertiesPanel.Controls.Add(selectedShapeNameLabel);
            this.Controls.Add(propertiesPanel);
            propertiesPanel.BringToFront();
            propertiesPanel.Visible = false;
        }
        private void InitCreationPanel()
        {
            creationPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = propertiesPanel.Width,
                BackColor = SystemColors.Control,
                Padding = new Padding(8),
                Visible = false
            };
            var headerLabel = new Label
            {
                Text = "Создание новой фигуры",
                Font = new Font("Arial", 10, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            addTypeComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 3, 6)
            };
            addTypeComboBox.Items.AddRange(new object[] { "Прямоугольник", "Круг", "Треугольник", "Шестиугольник", "Трапеция", "Ромб" });
            addTypeComboBox.SelectedIndex = 0;
            addTypeComboBox.SelectedIndexChanged += (s, e) => UpdateSizeInputs();
            addButton = new Button
            {
                Text = "Создать",
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 6, 0, 6)
            };
            addButton.Click += AddButton_Click;
            topPanel.Controls.Add(addTypeComboBox, 0, 0);
            topPanel.Controls.Add(addButton, 1, 0);
            var sizeGroupBox = new GroupBox
            {
                Text = "Параметры",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                AutoSize = true
            };
            sizeInputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 5),
                ColumnCount = 2,
                RowCount = 0
            };
            sizeInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            sizeInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sizeGroupBox.Controls.Add(sizeInputPanel);
            creationPanel.Controls.Add(sizeGroupBox);
            creationPanel.Controls.Add(topPanel);
            creationPanel.Controls.Add(headerLabel);
            this.Controls.Add(creationPanel);
            creationPanel.BringToFront();
            UpdateSizeInputs();
        }
        private void UpdateSizeInputs()
        {
            sizeInputPanel.Controls.Clear();
            sizeTextBoxes.Clear();
            string type = addTypeComboBox.SelectedItem as string;
            string[] labels = null;
            string[] defaults = null;
            switch (type)
            {
                case "Прямоугольник":
                    labels = new[] { "Сторона 1 (низ):", "Сторона 2 (право):", "Сторона 3 (верх):", "Сторона 4 (лево):" };
                    defaults = new[] { "140", "90", "140", "90" };
                    break;
                case "Круг":
                    labels = new[] { "Радиус X:", "Радиус Y:" };
                    defaults = new[] { "60", "60" };
                    break;
                case "Треугольник":
                    labels = new[] { "Сторона a:", "Сторона b:", "Сторона c:" };
                    defaults = new[] { "120", "90", "90" };
                    break;
                case "Шестиугольник":
                    labels = new[] { "Сторона 1:", "Сторона 2:", "Сторона 3:", "Сторона 4:", "Сторона 5:", "Сторона 6:" };
                    defaults = new[] { "60", "60", "60", "60", "60", "60" };
                    break;
                case "Трапеция":
                    labels = new[] { "Нижнее основание:", "Верхнее основание:", "Левая сторона:", "Правая сторона:" };
                    defaults = new[] { "140", "80", "100", "100" };
                    break;
                case "Ромб":
                    labels = new[] { "Верх (top):", "Право (right):", "Низ (bottom):", "Лево (left):" };
                    defaults = new[] { "50", "40", "50", "40" };
                    break;
            }
            if (labels != null)
            {
                sizeInputPanel.RowCount = labels.Length;
                for (int i = 0; i < labels.Length; i++)
                {
                    var label = new Label
                    {
                        Text = labels[i],
                        TextAlign = ContentAlignment.MiddleRight,
                        Anchor = AnchorStyles.Right,
                        Height = 25,
                        Margin = new Padding(3, 3, 5, 3)
                    };
                    var textBox = new TextBox
                    {
                        Text = defaults[i],
                        Width = 150,
                        Anchor = AnchorStyles.Left,
                        Margin = new Padding(3, 3, 3, 3)
                    };
                    sizeInputPanel.Controls.Add(label, 0, i);
                    sizeInputPanel.Controls.Add(textBox, 1, i);
                    sizeTextBoxes.Add(textBox);
                }
            }
        }
        private float[] GetSizeValues()
        {
            List<float> values = new List<float>();
            foreach (var tb in sizeTextBoxes)
            {
                if (float.TryParse(tb.Text, out float val))
                    values.Add(val);
                else
                    values.Add(0);
            }
            return values.ToArray();
        }
        private void UpdatePropertiesSizeInputs()
        {
            updatingPropertiesSizes = true;
            propertiesSizeInputPanel.Controls.Clear();
            propertiesSizeTextBoxes.Clear();
            if (selectedShape == null || selectedShape.GetSideLabels().Length == 0)
            {
                propertiesSizeGroupBox.Visible = false;
                updatingPropertiesSizes = false;
                return;
            }
            propertiesSizeGroupBox.Visible = true;
            string[] labels = selectedShape.GetSideLabels();
            float[] currents = selectedShape.GetCurrentSides();
            propertiesSizeInputPanel.RowCount = labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var label = new Label
                {
                    Text = labels[i],
                    TextAlign = ContentAlignment.MiddleRight,
                    Anchor = AnchorStyles.Right,
                    Height = 25,
                    Margin = new Padding(3, 3, 5, 3)
                };
                var textBox = new TextBox
                {
                    Text = i < currents.Length ? currents[i].ToString("0.##") : "100",
                    Width = 150,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 3, 3, 3)
                };
                propertiesSizeInputPanel.Controls.Add(label, 0, i);
                propertiesSizeInputPanel.Controls.Add(textBox, 1, i);
                propertiesSizeTextBoxes.Add(textBox);
                textBox.TextChanged += PropertiesSizeTextBox_TextChanged;
            }
            updatingPropertiesSizes = false;
            UpdatePropertiesColorInputs();
            UpdatePropertiesEdgesInputs();
            if (selectedShape != null)
            {
                propRotationUpDown.Value = (decimal)selectedShape.Rotation;
            }
            if (selectedShape != null)
            {
                var b = selectedShape.GetBounds();
                boundsTopLeftLabel.Text = $"TL: ({b.Left:0.##}, {b.Top:0.##})";
                boundsBottomRightLabel.Text = $"BR: ({b.Right:0.##}, {b.Bottom:0.##})";
                propertiesBoundsGroupBox.Visible = true;
            }
            else
            {
                propertiesBoundsGroupBox.Visible = false;
            }
        }
        private void UpdatePropertiesColorInputs()
        {
            if (selectedShape == null)
            {
                propertiesColorGroupBox.Visible = false;
                return;
            }
            propertiesColorGroupBox.Visible = true;
            var c = selectedShape.FillColor;
            propAUpDown.Value = c.A;
            propRUpDown.Value = c.R;
            propGUpDown.Value = c.G;
            propBUpDown.Value = c.B;
        }
        private void PropColor_ValueChanged(object? sender, EventArgs e)
        {
            if (selectedShape == null) return;
            int a = (int)propAUpDown.Value;
            int r = (int)propRUpDown.Value;
            int g = (int)propGUpDown.Value;
            int b = (int)propBUpDown.Value;
            selectedShape.FillColor = Color.FromArgb(a, r, g, b);
            canvasPanel?.Invalidate();
        }
        private void PropRotation_ValueChanged(object? sender, EventArgs e)
        {
            if (selectedShape == null) return;
            selectedShape.Rotation = (float)propRotationUpDown.Value;
            canvasPanel?.Invalidate();
        }
        private void UpdatePropertiesEdgesInputs()
        {
            propertiesEdgesPanel.Controls.Clear();
            if (selectedShape == null)
            {
                propertiesEdgesGroupBox.Visible = false;
                return;
            }
            propertiesEdgesGroupBox.Visible = true;
            for (int i = 0; i < selectedShape.Edges.Count; i++)
            {
                var edge = selectedShape.Edges[i];
                var panel = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0, 2, 0, 2)
                };
                var lbl = new Label
                {
                    Text = $"Грань {i + 1}:",
                    Width = 60,
                    TextAlign = ContentAlignment.MiddleRight
                };
                var widthNum = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = 20,
                    DecimalPlaces = 1,
                    Increment = 0.5M,
                    Value = (decimal)edge.Width,
                    Width = 70
                };
                var btn = new Button
                {
                    Text = "Цвет",
                    Width = 70,
                    Height = 23
                };
                widthNum.ValueChanged += (s, e) =>
                {
                    edge.Width = (float)widthNum.Value;
                    canvasPanel?.Invalidate();
                };
                btn.Click += (s, e) =>
                {
                    using (var cd = new ColorDialog())
                    {
                        cd.Color = edge.Color;
                        if (cd.ShowDialog() == DialogResult.OK)
                        {
                            edge.Color = cd.Color;
                            canvasPanel?.Invalidate();
                        }
                    }
                };
                panel.Controls.Add(lbl);
                panel.Controls.Add(widthNum);
                panel.Controls.Add(btn);
                propertiesEdgesPanel.Controls.Add(panel);
            }
        }
        private void PropertiesSizeTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (updatingPropertiesSizes || selectedShape == null) return;
            float[] values = GetPropertiesSizeValues();
            try
            {
                selectedShape.UpdateFromSides(values);
                canvasPanel?.Invalidate();
            }
            catch
            {
            }
        }
        private float[] GetPropertiesSizeValues()
        {
            List<float> values = new List<float>();
            foreach (var tb in propertiesSizeTextBoxes)
            {
                if (float.TryParse(tb.Text, out float val))
                    values.Add(val);
                else
                    values.Add(0);
            }
            return values.ToArray();
        }
        private void InitShapes()
        {
            shapes.Clear();
            float stdWidth = 140f;
            float stdHeight = 120f;
            float gap = 40f;
            var rect = new RectangleShape(PointF.Empty, stdWidth, stdHeight);
            var circle = new CircleShape(PointF.Empty, stdWidth / 2f, stdHeight / 2f);
            var triangle = new TriangleShape(PointF.Empty, stdWidth, stdHeight * 0.9f);
            // flip triangle vertically so it's 'inverted'
            // apply a 180-degree rotation and invert local verts Y
            triangle.Rotation += 180f;
            var hex = new HexagonShape(PointF.Empty, stdWidth / 2f);
            var trap = new TrapezoidShape(PointF.Empty, stdWidth, stdHeight * 0.8f);
            var diamond = new DiamondShape(PointF.Empty, stdHeight/2f, stdWidth/2f, stdHeight/2f, stdWidth/2f);
            var shapeList = new List<Shape> { rect, circle, triangle, hex, trap, diamond };
            var dims = new List<(float w, float h)>();
            foreach (var s in shapeList)
            {
                var b = s.GetBounds();
                dims.Add((b.Width <= 0 ? stdWidth : b.Width, b.Height <= 0 ? stdHeight : b.Height));
            }
            float centerX = 0f;
            float centerY = 0f;
            float totalWidth = dims.Sum(d => d.w) + gap * (shapeList.Count - 1);
            float startX = centerX - totalWidth / 2f;
            float x = startX;
            for (int i = 0; i < shapeList.Count; i++)
            {
                var s = shapeList[i];
                var w = dims[i].w;
                var h = dims[i].h;
                if (s is RectangleShape rr) rr.Anchor = new PointF(x, centerY - h / 2f);
                else if (s is CircleShape cc) cc.Anchor = new PointF(x + w / 2f, centerY);
                else if (s is TriangleShape tt) tt.Anchor = new PointF(x, centerY + h / 2f);
                else if (s is HexagonShape hh) hh.Anchor = new PointF(x + w / 2f, centerY);
                else if (s is TrapezoidShape trr) trr.Anchor = new PointF(x, centerY + h / 2f);
                else if (s is DiamondShape dd) dd.Anchor = new PointF(x + w / 2f, centerY);
                else s.Anchor = new PointF(x + w / 2f, centerY);
                shapes.Add(s);
                x += w + gap;
            }
            canvasPanel?.Invalidate();
        }
        private void UpdateSelectedShapeName()
        {
            if (selectedShapeNameLabel == null) return;
            if (selectedShape != null)
            {
                string typeName = selectedShape.GetType().Name.Replace("Shape", "");
                string rusName = typeName switch
                {
                    "Rectangle" => "Прямоугольник",
                    "Circle" => "Круг",
                    "Triangle" => "Треугольник",
                    "Hexagon" => "Шестиугольник",
                    "Trapezoid" => "Трапеция",
                    "Diamond" => "Ромб",
                    _ => typeName
                };
                selectedShapeNameLabel.Text = $"{rusName} {selectedShape.Id}";
            }
            else
            {
                selectedShapeNameLabel.Text = "Нет выделения";
            }
        }
        private void AddButton_Click(object? sender, EventArgs e)
        {
            Shape? s = null;
            var type = addTypeComboBox.SelectedItem as string;
            var center = new PointF(0, 0);
            float[] values = GetSizeValues();
            List<float> sides = new List<float>(values);
            try
            {
                switch (type)
                {
                    case "Прямоугольник":
                        if (sides.Count >= 4)
                            s = new RectangleShape(center, sides);
                        else
                            s = new RectangleShape(center, 140, 90);
                        break;
                    case "Круг":
                        if (sides.Count >= 2)
                            s = new CircleShape(center, sides[0], sides[1]);
                        else if (sides.Count >= 1)
                            s = new CircleShape(center, sides[0]);
                        else
                            s = new CircleShape(center, 60);
                        break;
                    case "Треугольник":
                        if (sides.Count >= 3)
                            s = new TriangleShape(center, sides);
                        else
                            s = new TriangleShape(center, 120, 90);
                        break;
                    case "Шестиугольник":
                        if (sides.Count >= 6)
                            s = new HexagonShape(center, sides);
                        else
                            s = new HexagonShape(center, 60);
                        break;
                    case "Трапеция":
                        if (sides.Count >= 4)
                            s = new TrapezoidShape(center, sides);
                        else
                            s = new TrapezoidShape(center, 140, 80);
                        break;
                    case "Ромб":
                        if (sides.Count >= 4)
                            s = new DiamondShape(center, sides[0], sides[1], sides[2], sides[3]);
                        else
                            s = new DiamondShape(center, 50, 40, 50, 40);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания фигуры: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (s != null)
            {
                shapes.Add(s);
                selectedShape = s;
                UpdateSelectedShapeName();
                UpdatePropertiesSizeInputs();
                canvasPanel?.Invalidate();
            }
        }
        private RectangleF GetVisibleWorldBounds()
        {
            PointF topLeft = ScreenToWorld(new Point(0, 0));
            PointF bottomRight = ScreenToWorld(new Point(canvasPanel.ClientSize.Width, canvasPanel.ClientSize.Height));
            return new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }
        private float GetGridStep(float visibleSize, float zoom)
        {
            float targetPixels = 50f;
            float targetWorld = targetPixels / zoom;
            double exponent = Math.Floor(Math.Log10(targetWorld));
            double baseStep = Math.Pow(10, exponent);
            double[] multipliers = { 1, 2, 5 };
            double bestStep = baseStep * multipliers[0];
            double bestDiff = Math.Abs(targetWorld - bestStep);
            foreach (var m in multipliers)
            {
                double step = baseStep * m;
                double diff = Math.Abs(targetWorld - step);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestStep = step;
                }
            }
            return (float)bestStep;
        }
        private void DrawTickLabels(Graphics g)
        {
            if (!showAxes) return;
            var visible = GetVisibleWorldBounds();
            float stepX = GetLabelStep(visible.Width, zoom);
            float stepY = GetLabelStep(visible.Height, zoom);
            var state = g.Save();
            g.ResetTransform();
            using (Font font = new Font("Arial", 10f))
            using (SolidBrush brush = new SolidBrush(Color.DarkGray))
            {
                float startX = (float)Math.Floor(visible.Left / stepX) * stepX;
                for (float x = startX; x <= visible.Right; x += stepX)
                {
                    if (Math.Abs(x) < 1e-6) continue;
                    PointF screen = WorldToScreen(new PointF(x, 0));
                    string text = FormatCoordinate(x);
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, screen.X - size.Width / 2, screen.Y + 8);
                }
                float startY = (float)Math.Floor(visible.Top / stepY) * stepY;
                for (float y = startY; y <= visible.Bottom; y += stepY)
                {
                    if (Math.Abs(y) < 1e-6) continue;
                    PointF screen = WorldToScreen(new PointF(0, y));
                    string text = FormatCoordinate(y);
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, screen.X - size.Width - 8, screen.Y - size.Height / 2);
                }
            }
            g.Restore(state);
        }
        private float GetLabelStep(float visibleSize, float zoom)
        {
            const float targetPixels = 120f;
            float targetWorld = targetPixels / zoom;
            if (targetWorld <= 0) return 1f;
            double exponent = Math.Floor(Math.Log10(targetWorld));
            double baseStep = Math.Pow(10, exponent);
            double[] multipliers = { 1.0, 2.0, 5.0 };
            double bestStep = baseStep * multipliers[0];
            double bestDiff = Math.Abs(targetWorld - bestStep);
            foreach (var m in multipliers)
            {
                double step = baseStep * m;
                double diff = Math.Abs(targetWorld - step);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestStep = step;
                }
            }
            float screenSpacing = (float)bestStep * zoom;
            if (screenSpacing < 100f)
            {
                bestStep *= 2;
            }
            return (float)bestStep;
        }
        private string FormatCoordinate(float value)
        {
            if (Math.Abs(value - Math.Round(value)) < 1e-6)
                return value.ToString("0");
            else
                return value.ToString("0.##");
        }
        private void CanvasPanel_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = canvasPanel.ClientSize.Width / 2f;
            float cy = canvasPanel.ClientSize.Height / 2f;
            e.Graphics.ResetTransform();
            e.Graphics.TranslateTransform(cx + panOffset.X, cy + panOffset.Y);
            e.Graphics.ScaleTransform(zoom, zoom);
            if (showGrid)
            {
                var visible = GetVisibleWorldBounds();
                float stepX = GetGridStep(visible.Width, zoom);
                float stepY = GetGridStep(visible.Height, zoom);
                using (Pen gridPen = new Pen(Color.FromArgb(220, 220, 220), 1f / zoom))
                {
                    float startX = (float)Math.Floor(visible.Left / stepX) * stepX;
                    for (float x = startX; x <= visible.Right; x += stepX)
                    {
                        e.Graphics.DrawLine(gridPen, x, visible.Top, x, visible.Bottom);
                    }
                    float startY = (float)Math.Floor(visible.Top / stepY) * stepY;
                    for (float y = startY; y <= visible.Bottom; y += stepY)
                    {
                        e.Graphics.DrawLine(gridPen, visible.Left, y, visible.Right, y);
                    }
                }
            }
            if (showAxes)
            {
                var visible = GetVisibleWorldBounds();
                using (Pen axisPen = new Pen(Color.DarkGray, 2f / zoom))
                {
                    e.Graphics.DrawLine(axisPen, visible.Left, 0, visible.Right, 0);
                    e.Graphics.DrawLine(axisPen, 0, visible.Top, 0, visible.Bottom);
                }
                float arrowLen = 12f / zoom;
                float arrowWid = 6f / zoom;
                using (Pen arrowPen = new Pen(Color.DarkGray, 2f / zoom))
                {
                    PointF xEnd = new PointF(visible.Right, 0);
                    e.Graphics.DrawLine(arrowPen, xEnd.X - arrowLen, xEnd.Y - arrowWid, xEnd.X, xEnd.Y);
                    e.Graphics.DrawLine(arrowPen, xEnd.X - arrowLen, xEnd.Y + arrowWid, xEnd.X, xEnd.Y);
                    PointF yEnd = new PointF(0, visible.Bottom);
                    e.Graphics.DrawLine(arrowPen, yEnd.X - arrowWid, yEnd.Y - arrowLen, yEnd.X, yEnd.Y);
                    e.Graphics.DrawLine(arrowPen, yEnd.X + arrowWid, yEnd.Y - arrowLen, yEnd.X, yEnd.Y);
                }
                float stepX = GetLabelStep(visible.Width, zoom);
                float stepY = GetLabelStep(visible.Height, zoom);
                float tickLen = 4f / zoom;
                using (Pen tickPen = new Pen(Color.DarkGray, 1.5f / zoom))
                {
                    float startX = (float)Math.Floor(visible.Left / stepX) * stepX;
                    for (float x = startX; x <= visible.Right; x += stepX)
                    {
                        if (Math.Abs(x) < 1e-6) continue;
                        e.Graphics.DrawLine(tickPen, x, -tickLen, x, tickLen);
                    }
                    float startY = (float)Math.Floor(visible.Top / stepY) * stepY;
                    for (float y = startY; y <= visible.Bottom; y += stepY)
                    {
                        if (Math.Abs(y) < 1e-6) continue;
                        e.Graphics.DrawLine(tickPen, -tickLen, y, tickLen, y);
                    }
                }
                PointF screenX = WorldToScreen(new PointF(visible.Right - 0.5f, 0));
                PointF screenY = WorldToScreen(new PointF(0, visible.Bottom - 0.5f));
                var state = e.Graphics.Save();
                e.Graphics.ResetTransform();
                using (Font font = new Font("Arial", 12f))
                using (SolidBrush brush = new SolidBrush(Color.DarkGray))
                {
                    e.Graphics.DrawString("X", font, brush, screenX.X - 24, screenX.Y - 25);
                    e.Graphics.DrawString("Y", font, brush, screenY.X - 30, screenY.Y - 35);
                }
                e.Graphics.Restore(state);
                DrawTickLabels(e.Graphics);
            }
            if (selectedShape != null)
            {
                try
                {
                    var b = selectedShape.GetBounds();
                    using (var boundPen = new Pen(Color.FromArgb(120, Color.DarkBlue), 2f / zoom))
                    using (var brush = new SolidBrush(Color.FromArgb(20, Color.LightBlue)))
                    {
                        e.Graphics.FillRectangle(brush, b.X, b.Y, b.Width, b.Height);
                        boundPen.DashStyle = DashStyle.Solid;
                        e.Graphics.DrawRectangle(boundPen, b.X, b.Y, b.Width, b.Height);
                    }
                }
                catch { }
            }
            foreach (var s in shapes)
                s.Draw(e.Graphics);
            if (selectedShape != null)
            {
                DrawResizeHandles(e.Graphics, selectedShape);
                try
                {
                    var b = selectedShape.GetBounds();
                    float halfH = b.Height / 2f;
                    var handleLocal = new PointF(0f, -halfH - 15f);
                    var handleWorld = selectedShape.LocalToWorld(handleLocal);
                    rotationHandleScreenCenter = WorldToScreen(handleWorld);
                    var state = e.Graphics.Save();
                    e.Graphics.ResetTransform();
                    using (var fill = new SolidBrush(Color.White))
                    using (var pen = new Pen(Color.Black, 1))
                    {
                        float r = rotationHandleScreenRadius;
                        e.Graphics.FillEllipse(fill, rotationHandleScreenCenter.X - r, rotationHandleScreenCenter.Y - r, r * 2, r * 2);
                        e.Graphics.DrawEllipse(pen, rotationHandleScreenCenter.X - r, rotationHandleScreenCenter.Y - r, r * 2, r * 2);
                    }
                    e.Graphics.Restore(state);
                }
                catch { }
            }
            if (selectedShape != null && showAnchorPoint)
            {
                try
                {
                    var state2 = e.Graphics.Save();
                    e.Graphics.ResetTransform();
                    float r = 5f;
                    var s = selectedShape;
                    var screen = WorldToScreen(s.Anchor);
                    var fillColor = Color.Red;
                    using (var brush = new SolidBrush(fillColor))
                    using (var pen = new Pen(Color.DarkBlue, 1))
                    {
                        e.Graphics.FillEllipse(brush, screen.X - r, screen.Y - r, r * 2, r * 2);
                        e.Graphics.DrawEllipse(pen, screen.X - r, screen.Y - r, r * 2, r * 2);
                    }
                    e.Graphics.Restore(state2);
                }
                catch { }
            }
            // draw overlay: zoom percent and cursor coordinates at top center
            try
            {
                var stateOverlay = e.Graphics.Save();
                e.Graphics.ResetTransform();
                string zoomText = $"Зум: {(int)(zoom * 100)}%";
                string cursorText = mouseInsideCanvas ? $"  |  Курсор: ({lastMouseWorld.X:0.##}, {lastMouseWorld.Y:0.##})" : "";
                string text = zoomText + cursorText;
                using (Font font = new Font("Arial", 10f, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(Color.DimGray))
                {
                    SizeF size = e.Graphics.MeasureString(text, font);
                    float x = (canvasPanel.ClientSize.Width - size.Width) / 2f;
                    float y = 4f;
                    e.Graphics.DrawString(text, font, brush, x, y);
                }
                e.Graphics.Restore(stateOverlay);
            }
            catch { }
        }
        private PointF WorldToScreen(PointF world)
        {
            float cx = canvasPanel.ClientSize.Width / 2f;
            float cy = canvasPanel.ClientSize.Height / 2f;
            return new PointF(cx + panOffset.X + world.X * zoom, cy + panOffset.Y + world.Y * zoom);
        }
        private void Form1_MouseDown(object? sender, MouseEventArgs e)
        {
            try
            {
                canvasPanel?.Focus();
                var world = ScreenToWorld(e.Location);
                if (e.Button == MouseButtons.Right)
                {
                    panning = true;
                    panStart = e.Location;
                    return;
                }
                if (e.Button == MouseButtons.Left)
                {
                    if (!rotationHandleScreenCenter.IsEmpty)
                    {
                        var dx = e.Location.X - rotationHandleScreenCenter.X;
                        var dy = e.Location.Y - rotationHandleScreenCenter.Y;
                        if (dx * dx + dy * dy <= (rotationHandleScreenRadius + 4) * (rotationHandleScreenRadius + 4))
                        {
                            rotatingWithThumb = true;
                            return;
                        }
                    }
                    if (selectedShape != null)
                    {
                        activeResizeHandle = GetResizeHandleAtPoint(selectedShape, world);
                        if (activeResizeHandle != ResizeHandle.None)
                        {
                            resizing = true;
                            originalBounds = selectedShape.GetBounds();
                            resizeOriginalAnchor = selectedShape.Anchor;
                            resizeStartWorld = world;
                            resizeStartLocalBounds = selectedShape.GetLocalBounds();
                            if (selectedShape is RectangleShape rs)
                            {
                                resizeStartWidth = rs.Width;
                                resizeStartHeight = rs.Height;
                            }
                            return;
                        }
                    }
                    for (int i = shapes.Count - 1; i >= 0; i--)
                    {
                        bool contains = false;
                        try
                        {
                            contains = shapes[i].ContainsPoint(world);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Ошибка в ContainsPoint для фигуры index={i}, type={shapes[i].GetType().Name}: {ex.Message}\n{ex.StackTrace}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            continue;
                        }
                        if (contains)
                        {
                            selectedShape = shapes[i];
                            shapes.RemoveAt(i);
                            shapes.Add(selectedShape);
                            dragging = true;
                            var anchor = selectedShape.Anchor;
                            dragOffset = new PointF(world.X - anchor.X, world.Y - anchor.Y);
                            if (propertiesPanel != null) propertiesPanel.Visible = true;
                            UpdateSelectedShapeName();
                            UpdatePropertiesSizeInputs();
                            showAnchorPoint = true;
                            canvasPanel.Invalidate();
                            return;
                        }
                    }
                    selectedShape = null;
                    if (propertiesPanel != null) propertiesPanel.Visible = false;
                    showAnchorPoint = false;
                    UpdateSelectedShapeName();
                    UpdatePropertiesSizeInputs();
                    canvasPanel.Invalidate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обработке нажатия мыши: {ex.Message}\n{ex.StackTrace}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void Form1_MouseMove(object? sender, MouseEventArgs e)
        {
            // update last mouse screen/world positions
            lastMouseScreen = e.Location;
            lastMouseWorld = ScreenToWorld(e.Location);

            if (panning)
            {
                var dx = e.Location.X - panStart.X;
                var dy = e.Location.Y - panStart.Y;
                panOffset = new PointF(panOffset.X + dx, panOffset.Y + dy);
                panStart = e.Location;
                canvasPanel.Invalidate();
                return;
            }
            if (rotatingWithThumb && selectedShape != null)
            {
                var anchorScreen = WorldToScreen(selectedShape.Anchor);
                float dx = e.Location.X - anchorScreen.X;
                float dy = e.Location.Y - anchorScreen.Y;
                double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                float rot = (float)ang + 90f;
                if (rot > 180f) rot -= 360f;
                if (rot < -180f) rot += 360f;
                selectedShape.Rotation = rot;
                if (propRotationUpDown != null)
                    propRotationUpDown.Value = Math.Max(propRotationUpDown.Minimum, Math.Min(propRotationUpDown.Maximum, (decimal)rot));
                canvasPanel.Invalidate();
                return;
            }
            var world = ScreenToWorld(e.Location);
            if (resizing && selectedShape != null)
            {
                var currentLocal = selectedShape.WorldToLocal(world);
                RectangleF newLocalBounds = ComputeNewBounds(resizeStartLocalBounds, activeResizeHandle, currentLocal);
                var newLocalCenter = new PointF(newLocalBounds.Left + newLocalBounds.Width / 2f, newLocalBounds.Top + newLocalBounds.Height / 2f);
                var newAnchorWorld = selectedShape.LocalToWorld(newLocalCenter);
                selectedShape.ResizeLocal(newLocalBounds, newAnchorWorld);
                canvasPanel.Invalidate();
                return;
            }
            if (dragging && selectedShape != null)
            {
                selectedShape.Anchor = new PointF(world.X - dragOffset.X, world.Y - dragOffset.Y);
                canvasPanel.Invalidate();
            }
        }
        private void Form1_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) panning = false;
            dragging = false;
            rotatingWithThumb = false;
            if (resizing)
            {
                resizing = false;
                activeResizeHandle = ResizeHandle.None;
                UpdatePropertiesSizeInputs();
                canvasPanel.Invalidate();
            }
        }
        private void CanvasPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            var mouse = e.Location;
            var worldBefore = ScreenToWorld(mouse);
            float delta = e.Delta > 0 ? 1.1f : 1 / 1.1f;
            zoom *= delta;
            zoom = Math.Min(Math.Max(zoom, 0.1f), 10f);
            float cx = canvasPanel.ClientSize.Width / 2f;
            float cy = canvasPanel.ClientSize.Height / 2f;
            panOffset = new PointF(mouse.X - cx - worldBefore.X * zoom, mouse.Y - cy - worldBefore.Y * zoom);
            canvasPanel.Invalidate();
        }
        private PointF ScreenToWorld(Point screen)
        {
            float cx = canvasPanel.ClientSize.Width / 2f;
            float cy = canvasPanel.ClientSize.Height / 2f;
            return new PointF(
                (screen.X - (cx + panOffset.X)) / zoom,
                (screen.Y - (cy + panOffset.Y)) / zoom
            );
        }
    }
    public abstract class Shape
    {
        private static int _nextId = 1;
        [Browsable(false)]
        public int Id { get; }
        [Browsable(false)]
        public PointF Anchor { get; set; }
        [Category("Положение")]
        public float AnchorX { get => Anchor.X; set => Anchor = new PointF(value, Anchor.Y); }
        [Category("Положение")]
        public float AnchorY { get => Anchor.Y; set => Anchor = new PointF(Anchor.X, value); }
        [Category("Положение")]
        public float Rotation { get; set; } = 0f;
        [Browsable(false)]
        public Color FillColor { get; set; } = Color.LightGray;
        [Browsable(false)]
        public List<Edge> Edges { get; } = new List<Edge>();
        protected Shape(PointF anchor)
        {
            Anchor = anchor;
            Id = _nextId++;
        }
        public abstract void Draw(Graphics g);
        public abstract bool ContainsPoint(PointF p);
        public virtual RectangleF GetBounds()
        {
            return new RectangleF(Anchor.X, Anchor.Y, 1, 1);
        }
        public virtual RectangleF GetLocalBounds()
        {
            return new RectangleF(-0.5f, -0.5f, 1f, 1f);
        }
        public PointF WorldToLocal(PointF p)
        {
            float dx = p.X - Anchor.X;
            float dy = p.Y - Anchor.Y;
            double ang = -Rotation * Math.PI / 180.0;
            double cos = Math.Cos(ang);
            double sin = Math.Sin(ang);
            float nx = (float)(dx * cos - dy * sin);
            float ny = (float)(dx * sin + dy * cos);
            return new PointF(nx, ny);
        }
        public PointF LocalToWorld(PointF p)
        {
            double ang = Rotation * Math.PI / 180.0;
            double cos = Math.Cos(ang);
            double sin = Math.Sin(ang);
            float nx = (float)(p.X * cos - p.Y * sin) + Anchor.X;
            float ny = (float)(p.X * sin + p.Y * cos) + Anchor.Y;
            return new PointF(nx, ny);
        }
        public virtual void Resize(RectangleF newBounds)
        {
            Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
        }
        public virtual void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            Resize(newLocalBounds); // fallback to old implementation if not overridden
        }
        public virtual string[] GetSideLabels() => Array.Empty<string>();
        public virtual float[] GetCurrentSides() => Array.Empty<float>();
        public virtual void UpdateFromSides(float[] newSides) { }
    }
    public class Edge
    {
        public float Width { get; set; } = 2f;
        public Color Color { get; set; } = Color.Black;
    }
    public class CircleShape : Shape
    {
        [Category("Размеры")]
        public float RadiusX { get; set; }
        [Category("Размеры")]
        public float RadiusY { get; set; }
        public CircleShape(PointF anchor, float radius) : base(anchor)
        {
            RadiusX = RadiusY = radius;
            Edges.Add(new Edge());
        }
        public CircleShape(PointF anchor, float rx, float ry) : base(anchor)
        {
            RadiusX = rx;
            RadiusY = ry;
            Edges.Add(new Edge());
        }
        public override void Draw(Graphics g)
        {
            var rect = new RectangleF(Anchor.X - RadiusX, Anchor.Y - RadiusY, RadiusX * 2, RadiusY * 2);
            using (var brush = new SolidBrush(FillColor)) g.FillEllipse(brush, rect);
            var edge = Edges[0];
            using (var pen = new Pen(edge.Color, edge.Width)) g.DrawEllipse(pen, rect);
        }
        public override bool ContainsPoint(PointF p)
        {
            var local = WorldToLocal(p);
            if (RadiusX <= 0 || RadiusY <= 0) return false;
            return (local.X * local.X) / (RadiusX * RadiusX) + (local.Y * local.Y) / (RadiusY * RadiusY) <= 1.0f;
        }
        public override RectangleF GetBounds()
        {
            return new RectangleF(Anchor.X - RadiusX, Anchor.Y - RadiusY, RadiusX * 2, RadiusY * 2);
        }
        public override RectangleF GetLocalBounds()
        {
            return new RectangleF(-RadiusX, -RadiusY, RadiusX * 2, RadiusY * 2);
        }
        public override void Resize(RectangleF newBounds)
        {
            RadiusX = Math.Max(newBounds.Width / 2f, 1f);
            RadiusY = Math.Max(newBounds.Height / 2f, 1f);
            Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
        }
        public override void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            RadiusX = Math.Max(newLocalBounds.Width / 2f, 1f);
            RadiusY = Math.Max(newLocalBounds.Height / 2f, 1f);
            Anchor = newAnchorWorld;
        }
        public override string[] GetSideLabels() => new[] { "Радиус X:", "Радиус Y:" };
        public override float[] GetCurrentSides() => new[] { RadiusX, RadiusY };
        public override void UpdateFromSides(float[] newSides)
        {
            if (newSides.Length >= 1) RadiusX = Math.Max(newSides[0], 1f);
            if (newSides.Length >= 2) RadiusY = Math.Max(newSides[1], 1f);
        }
    }
    public class RectangleShape : Shape
    {
        [Category("Размеры")]
        public float Width { get; set; }
        [Category("Размеры")]
        public float Height { get; set; }
        public RectangleShape(PointF anchor, float width, float height) : base(anchor)
        {
            Width = width;
            Height = height;
            for (int i = 0; i < 4; i++) Edges.Add(new Edge());
        }
        public RectangleShape(PointF anchor, List<float> sides) : base(anchor)
        {
            if (sides.Count < 4)
                throw new ArgumentException("Для прямоугольника нужно 4 стороны");
            const float eps = 0.001f;
            if (Math.Abs(sides[0] - sides[2]) > eps || Math.Abs(sides[1] - sides[3]) > eps)
                throw new ArgumentException("Противоположные стороны прямоугольника должны быть равны");
            Width = sides[0];
            Height = sides[1];
            for (int i = 0; i < 4; i++) Edges.Add(new Edge());
        }
        public override void Draw(Graphics g)
        {
            var halfW = Width / 2f;
            var halfH = Height / 2f;
            var pts = new PointF[] {
                new PointF(-halfW, -halfH),
                new PointF(halfW, -halfH),
                new PointF(halfW, halfH),
                new PointF(-halfW, halfH)
            };
            for (int i = 0; i < pts.Length; i++) pts[i] = LocalToWorld(pts[i]);
            using (var brush = new SolidBrush(FillColor)) g.FillPolygon(brush, pts);
            EdgeRenderer.DrawPolygonEdges(g, pts, Edges);
        }
        public override bool ContainsPoint(PointF p)
        {
            var local = WorldToLocal(p);
            return local.X >= -Width / 2 && local.X <= Width / 2 && local.Y >= -Height / 2 && local.Y <= Height / 2;
        }
        public override RectangleF GetBounds()
        {
            var halfW = Width / 2f;
            var halfH = Height / 2f;
            var pts = new PointF[] {
                LocalToWorld(new PointF(-halfW, -halfH)),
                LocalToWorld(new PointF(halfW, -halfH)),
                LocalToWorld(new PointF(halfW, halfH)),
                LocalToWorld(new PointF(-halfW, halfH))
            };
            float minX = pts.Min(t => t.X);
            float minY = pts.Min(t => t.Y);
            float maxX = pts.Max(t => t.X);
            float maxY = pts.Max(t => t.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override RectangleF GetLocalBounds()
        {
            return new RectangleF(-Width / 2f, -Height / 2f, Width, Height);
        }
        public override void Resize(RectangleF newBounds)
        {
            Width = newBounds.Width;
            Height = newBounds.Height;
            Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
        }
        public override void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            Width = Math.Max(newLocalBounds.Width, 10f);
            Height = Math.Max(newLocalBounds.Height, 10f);
            Anchor = newAnchorWorld;
        }
        public override string[] GetSideLabels() => new[] { "Сторона 1 (низ):", "Сторона 2 (право):", "Сторона 3 (верх):", "Сторона 4 (лево):" };
        public override float[] GetCurrentSides() => new[] { Width, Height, Width, Height };
        public override void UpdateFromSides(float[] newSides)
        {
            if (newSides.Length >= 2)
            {
                Width = Math.Max(newSides[0], 10f);
                Height = Math.Max(newSides[1], 10f);
            }
        }
    }
    public class TriangleShape : Shape
    {
        [Category("Размеры")]
        public float A { get; set; }
        [Category("Размеры")]
        public float B { get; set; }
        [Category("Размеры")]
        public float C { get; set; }
        private PointF[] localVerts = new PointF[3];
        public TriangleShape(PointF anchor, float b, float h) : base(anchor)
        {
            // fallback: create isosceles based on base and height
            A = b; B = h; C = h;
            RebuildFromSides(A, B, C);
            for (int i = 0; i < 3; i++) Edges.Add(new Edge());
        }
        public TriangleShape(PointF anchor, List<float> sides) : base(anchor)
        {
            if (sides.Count < 3)
                throw new ArgumentException("Для треугольника нужно 3 стороны");
            float a = sides[0];
            float b = sides[1];
            float c = sides[2];
            if (a + b <= c || a + c <= b || b + c <= a)
                throw new ArgumentException("Треугольник с такими сторонами не существует");
            A = a; B = b; C = c;
            RebuildFromSides(A, B, C);
            for (int i = 0; i < 3; i++) Edges.Add(new Edge());
        }
        private void RebuildFromSides(float a, float b, float c)
        {
            float x2 = (a * a + b * b - c * c) / (2 * a);
            float y2sq = b * b - x2 * x2;
            float y2 = y2sq > 0 ? (float)Math.Sqrt(y2sq) : 0f;
            var v0 = new PointF(0f, 0f);
            var v1 = new PointF(a, 0f);
            var v2 = new PointF(x2, y2);
            float cx = (v0.X + v1.X + v2.X) / 3f;
            float cy = (v0.Y + v1.Y + v2.Y) / 3f;
            localVerts[0] = new PointF(v0.X - cx, v0.Y - cy);
            localVerts[1] = new PointF(v1.X - cx, v1.Y - cy);
            localVerts[2] = new PointF(v2.X - cx, v2.Y - cy);
        }
        private PointF[] GetPoints()
        {
            return localVerts.Select(p => LocalToWorld(p)).ToArray();
        }
        private float Distance(PointF p1, PointF p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        public override void Draw(Graphics g)
        {
            var pts = GetPoints();
            using (var brush = new SolidBrush(FillColor)) g.FillPolygon(brush, pts);
            EdgeRenderer.DrawPolygonEdges(g, pts, Edges);
        }
        public override bool ContainsPoint(PointF p)
        {
            var pts = GetPoints();
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                return path.IsVisible(p);
            }
        }
        public override RectangleF GetBounds()
        {
            var pts = GetPoints();
            float minX = pts.Min(pt => pt.X);
            float minY = pts.Min(pt => pt.Y);
            float maxX = pts.Max(pt => pt.X);
            float maxY = pts.Max(pt => pt.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override RectangleF GetLocalBounds()
        {
            float minX = localVerts.Min(pt => pt.X);
            float minY = localVerts.Min(pt => pt.Y);
            float maxX = localVerts.Max(pt => pt.X);
            float maxY = localVerts.Max(pt => pt.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override void Resize(RectangleF newBounds)
        {
            var old = GetBounds();
            float sx = old.Width > 0 ? newBounds.Width / old.Width : 1f;
            float sy = old.Height > 0 ? newBounds.Height / old.Height : 1f;
            for (int i = 0; i < localVerts.Length; i++)
                localVerts[i] = new PointF(localVerts[i].X * sx, localVerts[i].Y * sy);
            Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
        }
        public override void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            var old = GetLocalBounds();
            float sx = old.Width > 0 ? newLocalBounds.Width / old.Width : 1f;
            float sy = old.Height > 0 ? newLocalBounds.Height / old.Height : 1f;
            for (int i = 0; i < localVerts.Length; i++)
            {
                float nx = (localVerts[i].X - old.X) * sx + newLocalBounds.X;
                float ny = (localVerts[i].Y - old.Y) * sy + newLocalBounds.Y;
                localVerts[i] = new PointF(nx, ny);
            }
            float cx = localVerts.Average(p => p.X);
            float cy = localVerts.Average(p => p.Y);
            for (int i = 0; i < localVerts.Length; i++)
                localVerts[i] = new PointF(localVerts[i].X - cx, localVerts[i].Y - cy);
            Anchor = newAnchorWorld;
        }
        public override string[] GetSideLabels() => new[] { "Сторона a:", "Сторона b:", "Сторона c:" };
        public override float[] GetCurrentSides()
        {
            var pts = GetPoints();
            float a = Distance(pts[0], pts[1]);
            float b = Distance(pts[1], pts[2]);
            float c = Distance(pts[2], pts[0]);
            return new[] { a, b, c };
        }
        public override void UpdateFromSides(float[] newSides)
        {
            if (newSides.Length >= 3)
            {
                float a = newSides[0];
                float b = newSides[1];
                float c = newSides[2];
                if (a <= 0 || b <= 0 || c <= 0 || a + b <= c || a + c <= b || b + c <= a) return;
                A = a; B = b; C = c;
                RebuildFromSides(A, B, C);
            }
        }
    }
    public class HexagonShape : Shape
    {
        [Category("Размеры")]
        public float[] Radii { get; set; } = new float[6] { 50,50,50,50,50,50 };
        public HexagonShape(PointF anchor, float radius) : base(anchor)
        {
            for (int i = 0; i < 6; i++) Radii[i] = radius;
            for (int i = 0; i < 6; i++) Edges.Add(new Edge());
        }
        public HexagonShape(PointF anchor, List<float> sides) : base(anchor)
        {
            for (int i = 0; i < 6; i++)
            {
                Radii[i] = (i < sides.Count && sides[i] > 0) ? sides[i] : (sides.Count > 0 ? sides[0] : 50f);
            }
            for (int i = 0; i < 6; i++) Edges.Add(new Edge());
        }
        private PointF[] GetPoints()
        {
            var pts = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                var ang = (float)(Math.PI / 3 * i);
                var lx = Radii[i] * (float)Math.Cos(ang);
                var ly = Radii[i] * (float)Math.Sin(ang);
                pts[i] = LocalToWorld(new PointF(lx, ly));
            }
            return pts;
        }
        public override void Draw(Graphics g)
        {
            var pts = GetPoints();
            using (var brush = new SolidBrush(FillColor)) g.FillPolygon(brush, pts);
            EdgeRenderer.DrawPolygonEdges(g, pts, Edges);
        }
        public override bool ContainsPoint(PointF p)
        {
            var pts = GetPoints();
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                return path.IsVisible(p);
            }
        }
        public override RectangleF GetBounds()
        {
            var pts = GetPoints();
            float minX = pts.Min(pt => pt.X);
            float minY = pts.Min(pt => pt.Y);
            float maxX = pts.Max(pt => pt.X);
            float maxY = pts.Max(pt => pt.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override RectangleF GetLocalBounds()
        {
            float maxR = Radii.Max();
            return new RectangleF(-maxR, -maxR, maxR * 2, maxR * 2);
        }
        public override void Resize(RectangleF newBounds)
        {
            var old = GetBounds();
            float sx = old.Width > 0 ? newBounds.Width / old.Width : 1f;
            float sy = old.Height > 0 ? newBounds.Height / old.Height : 1f;
            for (int i = 0; i < 6; i++) Radii[i] = Math.Max(Radii[i] * Math.Max(sx, sy), 1f);
            Anchor = new PointF(newBounds.X + newBounds.Width / 2, newBounds.Y + newBounds.Height / 2);
        }
        public override void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            var old = GetLocalBounds();
            float sx = old.Width > 0 ? newLocalBounds.Width / old.Width : 1f;
            float sy = old.Height > 0 ? newLocalBounds.Height / old.Height : 1f;
            for (int i = 0; i < 6; i++) Radii[i] = Math.Max(Radii[i] * Math.Max(sx, sy), 1f);
            Anchor = newAnchorWorld;
        }
        public override string[] GetSideLabels() => new[] { "Радиус 1:", "Радиус 2:", "Радиус 3:", "Радиус 4:", "Радиус 5:", "Радиус 6:" };
        public override float[] GetCurrentSides()
        {
            return (float[])Radii.Clone();
        }
        public override void UpdateFromSides(float[] newSides)
        {
            for (int i = 0; i < 6; i++)
            {
                if (i < newSides.Length && newSides[i] > 0)
                    Radii[i] = newSides[i];
            }
        }
    }
    public class TrapezoidShape : Shape
    {
        [Category("Размеры")]
        public float BottomWidth { get; set; }
        [Category("Размеры")]
        public float TopWidth { get; set; }
        [Category("Размеры")]
        public float LeftSide { get; set; }
        [Category("Размеры")]
        public float RightSide { get; set; }
        [Category("Размеры")]
        public float Height { get; set; }
        public TrapezoidShape(PointF anchor, float bottomWidth, float topWidth) : base(anchor)
        {
            BottomWidth = bottomWidth;
            TopWidth = topWidth;
            LeftSide = RightSide = Math.Max((bottomWidth - topWidth) / 2f, 50f);
            Height = (float)Math.Sqrt(Math.Max(LeftSide * LeftSide - ((bottomWidth - topWidth) / 2f) * ((bottomWidth - topWidth) / 2f), 1f));
            for (int i = 0; i < 4; i++) Edges.Add(new Edge());
        }
        public TrapezoidShape(PointF anchor, List<float> sides) : base(anchor)
        {
            if (sides.Count < 4)
                throw new ArgumentException("Для трапеции нужно 4 стороны");
            float bottom = sides[0];
            float top = sides[1];
            float left = sides[2];
            float right = sides[3];
            BottomWidth = bottom; TopWidth = top; LeftSide = left; RightSide = right;
            float diff = (bottom - top) / 2f;
            float h1 = left > Math.Abs(diff) ? (float)Math.Sqrt(Math.Max(left * left - diff * diff, 0f)) : 0f;
            float h2 = right > Math.Abs(diff) ? (float)Math.Sqrt(Math.Max(right * right - diff * diff, 0f)) : 0f;
            Height = Math.Max(1f, (h1 + h2) / 2f);
            for (int i = 0; i < 4; i++) Edges.Add(new Edge());
        }
        private PointF[] GetPoints()
        {
            float halfBottom = BottomWidth / 2f;
            float halfTop = TopWidth / 2f;
            float leftOffset = (float)Math.Sqrt(Math.Max(LeftSide * LeftSide - Height * Height, 0f));
            float rightOffset = (float)Math.Sqrt(Math.Max(RightSide * RightSide - Height * Height, 0f));
            var p0 = new PointF(-halfBottom, Height / 2f);
            var p1 = new PointF(halfBottom, Height / 2f);
            var p2 = new PointF(halfTop - rightOffset, -Height / 2f);
            var p3 = new PointF(-halfTop + leftOffset, -Height / 2f);
            return new[] { LocalToWorld(p0), LocalToWorld(p1), LocalToWorld(p2), LocalToWorld(p3) };
        }
        public override void Draw(Graphics g)
        {
            var pts = GetPoints();
            using (var brush = new SolidBrush(FillColor)) g.FillPolygon(brush, pts);
            EdgeRenderer.DrawPolygonEdges(g, pts, Edges);
        }
        public override bool ContainsPoint(PointF p)
        {
            var pts = GetPoints();
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                return path.IsVisible(p);
            }
        }
        public override RectangleF GetBounds()
        {
            var pts = GetPoints();
            float minX = pts.Min(pt => pt.X);
            float minY = pts.Min(pt => pt.Y);
            float maxX = pts.Max(pt => pt.X);
            float maxY = pts.Max(pt => pt.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override RectangleF GetLocalBounds()
        {
            float halfBottom = BottomWidth / 2f;
            float halfTop = TopWidth / 2f;
            float leftOffset = (float)Math.Sqrt(Math.Max(LeftSide * LeftSide - Height * Height, 0f));
            float rightOffset = (float)Math.Sqrt(Math.Max(RightSide * RightSide - Height * Height, 0f));
            var p0 = new PointF(-halfBottom, Height / 2f);
            var p1 = new PointF(halfBottom, Height / 2f);
            var p2 = new PointF(halfTop - rightOffset, -Height / 2f);
            var p3 = new PointF(-halfTop + leftOffset, -Height / 2f);
            var pts = new[] { p0, p1, p2, p3 };
            float minX = pts.Min(pt => pt.X);
            float minY = pts.Min(pt => pt.Y);
            float maxX = pts.Max(pt => pt.X);
            float maxY = pts.Max(pt => pt.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        public override void Resize(RectangleF newBounds)
        {
            var oldBounds = GetBounds();
            float scaleX = oldBounds.Width > 0 ? newBounds.Width / oldBounds.Width : 1f;
            float scaleY = oldBounds.Height > 0 ? newBounds.Height / oldBounds.Height : 1f;
            float newAnchorX = newBounds.X + (Anchor.X - oldBounds.X) * scaleX;
            float newAnchorY = newBounds.Y + (Anchor.Y - oldBounds.Y) * scaleY;
            Anchor = new PointF(newAnchorX, newAnchorY);
            BottomWidth *= scaleX;
            TopWidth *= scaleX;
            LeftSide *= Math.Max(scaleX, scaleY);
            RightSide *= Math.Max(scaleX, scaleY);
            Height *= scaleY;
        }
        public override void ResizeLocal(RectangleF newLocalBounds, PointF newAnchorWorld)
        {
            var oldBounds = GetLocalBounds();
            float scaleX = oldBounds.Width > 0 ? newLocalBounds.Width / oldBounds.Width : 1f;
            float scaleY = oldBounds.Height > 0 ? newLocalBounds.Height / oldBounds.Height : 1f;
            Anchor = newAnchorWorld;
            BottomWidth *= scaleX;
            TopWidth *= scaleX;
            LeftSide *= Math.Max(scaleX, scaleY);
            RightSide *= Math.Max(scaleX, scaleY);
            Height *= scaleY;
        }
        public override string[] GetSideLabels() => new[] { "Нижнее основание:", "Верхнее основание:", "Левая сторона:", "Правая сторона:" };
        public override float[] GetCurrentSides()
        {
            return new[] { BottomWidth, TopWidth, LeftSide, RightSide };
        }
        public override void UpdateFromSides(float[] newSides)
        {
            if (newSides.Length >= 4)
            {
                float bottom = newSides[0];
                float top = newSides[1];
                float left = newSides[2];
                float right = newSides[3];
                if (bottom <= 0 || top <= 0 || left <= 0 || right <= 0) return;
                BottomWidth = bottom; TopWidth = top; LeftSide = left; RightSide = right;
                float diff = (bottom - top) / 2f;
                float h1 = left > Math.Abs(diff) ? (float)Math.Sqrt(Math.Max(left * left - diff * diff, 0f)) : 0f;
                float h2 = right > Math.Abs(diff) ? (float)Math.Sqrt(Math.Max(right * right - diff * diff, 0f)) : 0f;
                Height = Math.Max(1f, (h1 + h2) / 2f);
            }
        }
    }

    public static class EdgeRenderer
    {
        public static void DrawPolygonEdges(Graphics g, PointF[] pts, List<Edge> edges)
        {
            if (pts == null || pts.Length < 2) return;
            int n = pts.Length;
            if (edges == null) return;
            PointF[] dir = new PointF[n];
            PointF[] normal = new PointF[n];
            float[] halfWidth = new float[n];
            for (int i = 0; i < n; i++)
            {
                int ei = i % Math.Max(1, edges.Count);
                halfWidth[i] = Math.Max(0f, edges[ei].Width) / 2f;
                var a = pts[i];
                var b = pts[(i + 1) % n];
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 1e-6f)
                {
                    dir[i] = new PointF(0, 0);
                    normal[i] = new PointF(0, 0);
                }
                else
                {
                    dir[i] = new PointF(dx / len, dy / len);
                    normal[i] = new PointF(-dir[i].Y, dir[i].X);
                }
            }

            PointF[] leftMiter = new PointF[n];
            PointF[] rightMiter = new PointF[n];
            for (int k = 0; k < n; k++)
            {
                int prev = (k - 1 + n) % n;
                int cur = k;
                PointF a1 = new PointF(pts[prev].X + normal[prev].X * halfWidth[prev], pts[prev].Y + normal[prev].Y * halfWidth[prev]);
                PointF a2 = new PointF(pts[(prev + 1) % n].X + normal[prev].X * halfWidth[prev], pts[(prev + 1) % n].Y + normal[prev].Y * halfWidth[prev]);
                PointF b1 = new PointF(pts[cur].X + normal[cur].X * halfWidth[cur], pts[cur].Y + normal[cur].Y * halfWidth[cur]);
                PointF b2 = new PointF(pts[(cur + 1) % n].X + normal[cur].X * halfWidth[cur], pts[(cur + 1) % n].Y + normal[cur].Y * halfWidth[cur]);
                var li = IntersectLines(a1, a2, b1, b2);
                if (li.HasValue)
                    leftMiter[k] = li.Value;
                else
                    leftMiter[k] = new PointF(pts[k].X + (normal[prev].X * halfWidth[prev] + normal[cur].X * halfWidth[cur]) / 2f,
                                               pts[k].Y + (normal[prev].Y * halfWidth[prev] + normal[cur].Y * halfWidth[cur]) / 2f);

                PointF ra1 = new PointF(pts[prev].X - normal[prev].X * halfWidth[prev], pts[prev].Y - normal[prev].Y * halfWidth[prev]);
                PointF ra2 = new PointF(pts[(prev + 1) % n].X - normal[prev].X * halfWidth[prev], pts[(prev + 1) % n].Y - normal[prev].Y * halfWidth[prev]);
                PointF rb1 = new PointF(pts[cur].X - normal[cur].X * halfWidth[cur], pts[cur].Y - normal[cur].Y * halfWidth[cur]);
                PointF rb2 = new PointF(pts[(cur + 1) % n].X - normal[cur].X * halfWidth[cur], pts[(cur + 1) % n].Y - normal[cur].Y * halfWidth[cur]);
                var ri = IntersectLines(ra1, ra2, rb1, rb2);
                if (ri.HasValue)
                    rightMiter[k] = ri.Value;
                else
                    rightMiter[k] = new PointF(pts[k].X - (normal[prev].X * halfWidth[prev] + normal[cur].X * halfWidth[cur]) / 2f,
                                                pts[k].Y - (normal[prev].Y * halfWidth[prev] + normal[cur].Y * halfWidth[cur]) / 2f);
            }

            var oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var poly = new PointF[] { leftMiter[i], leftMiter[j], rightMiter[j], rightMiter[i] };
                int edgeIndex = i % Math.Max(1, edges.Count);
                using (var brush = new SolidBrush(edges[edgeIndex].Color))
                {
                    try { g.FillPolygon(brush, poly); }
                    catch { }
                }
            }
            g.SmoothingMode = oldSmoothing;
        }

        private static PointF? IntersectLines(PointF p1, PointF p2, PointF p3, PointF p4)
        {
            float x1 = p1.X, y1 = p1.Y;
            float x2 = p2.X, y2 = p2.Y;
            float x3 = p3.X, y3 = p3.Y;
            float x4 = p4.X, y4 = p4.Y;
            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-6f) return null;
            float px = ( (x1*y2 - y1*x2) * (x3 - x4) - (x1 - x2) * (x3*y4 - y3*x4) ) / denom;
            float py = ( (x1*y2 - y1*x2) * (y3 - y4) - (y1 - y2) * (x3*y4 - y3*x4) ) / denom;
            return new PointF(px, py);
        }
    }
}