using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using DIAVision.UI.Base;
using DIAVision.UI.Unified.Models.RunModeEditor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CSV;
using DIAVision.UI.Unified.DiaControl;

namespace MyLiveChartNamespace
{
    public partial class MyLiveChart : DiaControlPlugin
    {
        public static readonly DirectProperty<MyLiveChart, object> DataInputProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, object>(nameof(DataInput), o => o.DataInput, (o, v) => o.DataInput = v);

        public static readonly DirectProperty<MyLiveChart, string> ChartTitleProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, string>(nameof(ChartTitle), o => o.ChartTitle, (o, v) => o.ChartTitle = v);

        public static readonly DirectProperty<MyLiveChart, IBrush> LineColorProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, IBrush>(nameof(LineColor), o => o.LineColor, (o, v) => o.LineColor = v);

        public static readonly DirectProperty<MyLiveChart, double> YRangeMaxProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, double>(nameof(YRangeMax), o => o.YRangeMax, (o, v) => o.YRangeMax = v);

        public static readonly DirectProperty<MyLiveChart, string> YUnitProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, string>(nameof(YUnit), o => o.YUnit, (o, v) => o.YUnit = v);

        public static readonly DirectProperty<MyLiveChart, double> UpperLimitProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, double>(nameof(UpperLimit), o => o.UpperLimit, (o, v) => o.UpperLimit = v);

        public static readonly DirectProperty<MyLiveChart, double> LowerLimitProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, double>(nameof(LowerLimit), o => o.LowerLimit, (o, v) => o.LowerLimit = v);

        public static readonly DirectProperty<MyLiveChart, double> StandardValueProperty =
            AvaloniaProperty.RegisterDirect<MyLiveChart, double>(nameof(StandardValue), o => o.StandardValue, (o, v) => o.StandardValue = v);

        private object dataInput = 0.0;
        private string chartTitle = "数据折线图";
        private IBrush lineColor = Brushes.Cyan;
        private double yRangeMax = 3000.0;
        private string yUnit = "mm";

        private double upperLimit = 2500.0;
        private double lowerLimit = 500.0;
        private double standardValue = 1500.0;

        private List<double> _valueBuffer = new List<double>();
        private List<DateTime> _timeBuffer = new List<DateTime>();

        private int _lastInputListCount = 0;

        private Polyline? _polyline;
        private Canvas? _canvas;
        private Canvas? _xAxisCanvas;
        private TextBlock? _totalCountLabel;
        private Line? _avgLine;
        private TextBlock? _avgLabel;

        private Line? _upperLine, _lowerLine, _stdLine;
        private TextBlock? _upperLabel, _lowerLabel, _stdLabel;

        // ======= 核心状态控制系统 =======
        private double _xStep = 25.0; // 图表动态缩放间距（每个点在 X 轴的物理距离，初始25）
        private const double MinXStep = 2.0;   // 无限缩小的极限（数据密恐极限保护）
        private const double MaxXStep = 100.0; // 放大的极限跨度
        private const double ZoomSpeed = 1.2;  // 每次鼠标滚动一格放缩的倍率
        
        private double _panX = 0;        // 【灵魂参数】：当前摄影机（视口）的全局 X 轴平移补偿。一切拖放缩放都只改变它。
        private bool _isDragging = false;// 判断用户当前是否按下了鼠标左键
        private bool _isFollowing = true;// 判断当前数据产生时，画面是否要自动跟踪到最新数据（80%吸附）
        private Point _lastMousePos;     // 记录上一帧鼠标的拖拽位置，用于计算位移距离

        private string _lastRawInput = ""; // 记录上一次接收的输入，避免重复渲染

        /// <summary>
        /// 构造函数：初始化组件并绑定窗口尺寸变化事件，确保在调整窗口大小时重新绘制图表。
        /// </summary>
        public MyLiveChart()
        {
            InitializeComponent();
            GeneratePropertyEditorInfos();
            this.SizeChanged += (s, e) => RedrawChart();
        }

        /// <summary>
        /// 加载 XAML UI 组件。
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// 当控件被附加到可视化树时调用。在此处获取 XAML 中定义的子控件引用，并设置 Canvas 透明背景以支持原生拖拽事件。
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _polyline = this.FindControl<Polyline>("MainPolyline");
            _canvas = this.FindControl<Canvas>("GraphCanvas");
            _xAxisCanvas = this.FindControl<Canvas>("XAxisCanvas");
            _totalCountLabel = this.FindControl<TextBlock>("TotalCountLabel");
            _avgLine = this.FindControl<Line>("AverageLine");
            _avgLabel = this.FindControl<TextBlock>("AverageLabel");

            _upperLine = this.FindControl<Line>("UpperLimitLine");
            _lowerLine = this.FindControl<Line>("LowerLimitLine");
            _stdLine = this.FindControl<Line>("StandardLine");

            _upperLabel = this.FindControl<TextBlock>("UpperLimitLabel");
            _lowerLabel = this.FindControl<TextBlock>("LowerLimitLabel");
            _stdLabel = this.FindControl<TextBlock>("StandardLabel");
            
            if (_canvas != null) _canvas.Background = Brushes.Transparent; // Make canvas hit-testable!
        }

        /// <summary>
        /// [框架要求] 声明此插件可在 DIAVision 属性面板中暴露配置的各个属性信息。
        /// </summary>
        private void GeneratePropertyEditorInfos()
        {
            var list = new Collection<PropertyEditorInfo>
            {
                new PropertyEditorInfo(nameof(DataInput), DataInputProperty, LinkType.DirectBinding, QuickSelectType.Parameter),
                new PropertyEditorInfo(nameof(ChartTitle), ChartTitleProperty, LinkType.DirectBinding, QuickSelectType.None),
                new PropertyEditorInfo(nameof(LineColor), LineColorProperty, LinkType.DirectBinding, QuickSelectType.Color),
                new PropertyEditorInfo(nameof(YRangeMax), YRangeMaxProperty, LinkType.DirectBinding, QuickSelectType.None),
                new PropertyEditorInfo(nameof(YUnit), YUnitProperty, LinkType.DirectBinding, QuickSelectType.None),
                new PropertyEditorInfo(nameof(UpperLimit), UpperLimitProperty, LinkType.DirectBinding, QuickSelectType.None),
                new PropertyEditorInfo(nameof(LowerLimit), LowerLimitProperty, LinkType.DirectBinding, QuickSelectType.None),
                new PropertyEditorInfo(nameof(StandardValue), StandardValueProperty, LinkType.DirectBinding, QuickSelectType.None)
            };
            PropertyEditorInfos = new ReadOnlyCollection<PropertyEditorInfo>(list);
        }

        public object DataInput
        {
            get => dataInput;
            set { SetAndRaise(DataInputProperty, ref dataInput, value); UpdateChart(value); }
        }

        public string YUnit { get => yUnit; set { SetAndRaise(YUnitProperty, ref yUnit, value); } }
        public string ChartTitle { get => chartTitle; set { SetAndRaise(ChartTitleProperty, ref chartTitle, value); } }
        public IBrush LineColor { get => lineColor; set { SetAndRaise(LineColorProperty, ref lineColor, value); if (_polyline != null) _polyline.Stroke = lineColor; } }
        public double YRangeMax { get => yRangeMax; set { SetAndRaise(YRangeMaxProperty, ref yRangeMax, value); } }

        public double UpperLimit { get => upperLimit; set { SetAndRaise(UpperLimitProperty, ref upperLimit, value); } }
        public double LowerLimit { get => lowerLimit; set { SetAndRaise(LowerLimitProperty, ref lowerLimit, value); } }
        public double StandardValue { get => standardValue; set { SetAndRaise(StandardValueProperty, ref standardValue, value); } }

        /// <summary>
        /// 核心数据入口：接收外部传入的数据字符串，进行解析并存入全局持久化缓冲池。
        /// 包含超大容量内存防爆处理（超10万剔除），并触发布局的平移与重绘更新。
        /// </summary>
        /// <param name="rawInput">逗号或换行分隔的数据字符串</param>
        private void UpdateChart(object rawInput)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (rawInput == null) return;
                    string currentStr = rawInput.ToString() ?? "";
                    if (currentStr == _lastRawInput) return;
                    _lastRawInput = currentStr;

                    var parts = currentStr.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var allValues = new List<double>();
                    foreach (var part in parts) if (double.TryParse(part.Trim(), out double d)) allValues.Add(d);

                    bool isNewRun = allValues.Count < _lastInputListCount || _valueBuffer.Count == 0;
                    if (isNewRun)
                    {
                        _isFollowing = true;
                        _lastInputListCount = 0;
                    }

                    List<double> newToAppend = allValues.Skip(_lastInputListCount).ToList();
                    
                    if (newToAppend.Count > 0)
                    {
                        foreach (var val in newToAppend)
                        {
                            _valueBuffer.Add(val);
                            _timeBuffer.Add(DateTime.Now);
                        }
                    }

                    _lastInputListCount = allValues.Count;

                    // 【内存防爆保护】：当内存积累到十万个数据时。
                    if (_valueBuffer.Count > 100000)
                    {
                        // 像心电图机一样，切除最老旧的，保证不超量膨胀。 
                        int removeCount = _valueBuffer.Count - 100000;
                        _valueBuffer.RemoveRange(0, removeCount);
                        _timeBuffer.RemoveRange(0, removeCount);
                        // 如果处于自动跟踪（_isFollowing=true），_panX 将会在 RedrawChart 中被自动追平。
                    }

                    if (!_isDragging && _isFollowing)
                    {
                        UpdateFollowPanX();
                    }

                    RedrawChart();
                }
                catch { }
            });
        }

        /// <summary>
        /// 获取当前主画布的实际可用宽度，防止在初始化阶段宽度未获取导致的计算异常。
        /// </summary>
        private double GetCanvasWidth()
        {
            if (_canvas != null && _canvas.Bounds.Width > 10) return _canvas.Bounds.Width;
            if (this.Bounds.Width > 100) return this.Bounds.Width - 80;
            return 800;
        }

        /// <summary>
        /// 跟随模式的摇杆控制算法：自动计算最新数据是否应该平移画布，
        /// 确保新生成的数据永远粘在屏幕 80% 相对位置的最佳视觉观察点。
        /// </summary>
        private void UpdateFollowPanX()
        {
            double w = GetCanvasWidth();
            if (w < 10) return;
            
            double lastPointX = Math.Max(0, (_valueBuffer.Count - 1) * _xStep);
            double targetX = w * 0.8;

            if (lastPointX < targetX)
            {
                _panX = 0;
            }
            else
            {
                _panX = targetX - lastPointX;
            }
        }

        /// <summary>
        /// 核心渲染引擎：基于屏幕绝对坐标系重新结算所有的图形节点位置。
        /// 包含双向平移空气墙约束，以及视野外渲染节点自动剔除的工业级性能优化。
        /// </summary>
        private void RedrawChart()
        {
            if (_canvas == null || _polyline == null || _xAxisCanvas == null) return;

            // Ensure hit-testability and remove transform caching oddities if any
            if (_canvas.Background == null) _canvas.Background = Brushes.Transparent;
            _canvas.RenderTransform = null;
            _xAxisCanvas.RenderTransform = null;
            
            _polyline.StrokeThickness = 2.0;

            double w = GetCanvasWidth();
            double h = _canvas.Bounds.Height;
            if (w < 10 || h < 10) return;

            double padding = 5.0;
            double actualH = h - 2 * padding;
            double currentMax = YRangeMax > 0.1 ? YRangeMax : 1000.0;
            
            // ========== 拖拽空气墙物理规则碰撞检测 ==========
            double lastPointX = Math.Max(0, (_valueBuffer.Count - 1) * _xStep);
            
            // 【规则一向右拖壁垒】：向右拖拽查看老数据时，最多拖到最新数据卡在80%的地方（限制看未来空气）
            double minPanX = (w * 0.8) - lastPointX; 
            if (minPanX > 0) minPanX = 0; 

            // 【规则二向左拖壁垒】：向左滑回看新数据时，最多只能拖到第一个点到达Y轴（不允许轴分离）
            double maxPanX = 0; 
            
            // 如果撞壁，锁死_panX不让他继续滑行
            if (_panX < minPanX && !_isFollowing) _panX = minPanX; 
            if (_panX > maxPanX) _panX = maxPanX;

            var points = new List<Point>();
            var polylinePoints = new List<Point>(); 
            for (int i = 0; i < _valueBuffer.Count; i++)
            {
                double px = i * _xStep + _panX;
                double safeVal = _valueBuffer[i] < 0 ? 0 : _valueBuffer[i];
                double py = h - padding - (safeVal / currentMax * actualH);
                py = Math.Clamp(py, padding, h - padding);
                
                var pt = new Point(px, py);
                points.Add(pt);

                // 性能剪裁策略：只把将会在屏幕内或屏幕边缘（左右一定缓冲距）的点，加入 PolyLine 渲染几何体中
                if (px >= -w && px <= w * 2)
                {
                    polylinePoints.Add(pt);
                }
            }
            _polyline.Points = polylinePoints;

            // ========== 动态隐藏标签与密集恐惧症预防 ==========
            // 每次把现存画面上附着的点与文本清空（用作下面的重新裁剪分发）
            _canvas.Children.RemoveAll(_canvas.Children.Where(c => c is Ellipse || c is TextBlock));
            bool showDetails = _xStep >= 10.0;
            if (showDetails)
            {
                for (int i = 0; i < _valueBuffer.Count; i++)
                {
                    double px = points[i].X;
                    if (px >= -50 && px <= w + 50)
                    {
                        double py = points[i].Y;
                        IBrush dotColor = i % 2 == 0 ? Brushes.Red : Brushes.Orange;
                        var dot = new Ellipse { Width = 6, Height = 6, Fill = dotColor, StrokeThickness = 1, Stroke = Brushes.White };
                        Canvas.SetLeft(dot, px - 3); Canvas.SetTop(dot, py - 3);
                        _canvas.Children.Add(dot);

                        var label = new TextBlock { Text = _valueBuffer[i].ToString("F0"), Foreground = Brushes.Yellow, FontSize = 11 };
                        Canvas.SetLeft(label, px - 15); Canvas.SetTop(label, i % 2 == 0 ? py - 25 : py + 10);
                        _canvas.Children.Add(label);
                    }
                }
            }

            _xAxisCanvas.Children.Clear();
            int stepCount = _xStep < 40 ? (int)Math.Ceiling(40 / _xStep) : 1;
            for (int i = 0; i < _valueBuffer.Count; i += stepCount)
            {
                double px = i * _xStep + _panX;
                if (px >= -50 && px <= w + 50)
                {
                    var label = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.Gray, FontSize = 10, Width = 40, TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(label, px - 20); Canvas.SetTop(label, 2);
                    _xAxisCanvas.Children.Add(label);
                }
            }

            if (_totalCountLabel != null) _totalCountLabel.Text = $"Total: {_valueBuffer.Count}";

            UpdateLimitLines(h, padding, currentMax, actualH, _valueBuffer.Count > 0 ? _valueBuffer.Average() : 0, _valueBuffer.Count > 0);
        }

        /// <summary>
        /// UI 事件：保存按钮被点击时，触发 CSVApi 自动清除 14天过期记录并写入本次实时数据记录。
        /// </summary>
        public void OnSaveButtonClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_valueBuffer.Count == 0) return;
                var csv = new CSVApi();
                int count = Math.Min(_valueBuffer.Count, _timeBuffer.Count);
                for (int i = 0; i < count; i++) csv.SaveData(_valueBuffer[i].ToString(), "OK", _timeBuffer[i]);
            }
            catch { }
        }

        /// <summary>
        /// 更新所有辅助基准线（上限、下限、标准线、平均值）的显示位置。
        /// </summary>
        private void UpdateLimitLines(double h, double padding, double currentMax, double actualH, double avgValue, bool hasData)
        {
            if (_canvas == null) return;
            void UpdateLineAndLabel(Line? line, TextBlock? label, double val, string prefix, bool show)
            {
                if (line == null && label == null) return;
                if (!show) { if (line != null) line.IsVisible = false; if (label != null) label.IsVisible = false; return; }
                double y = (h - padding) - (val / currentMax * actualH);
                y = Math.Clamp(y, padding, h - padding);
                if (line != null)
                {
                    line.IsVisible = true; line.StartPoint = new Point(-1000000, y); line.EndPoint = new Point(5000000, y);
                }
                if (label != null)
                {
                    label.IsVisible = true; label.Text = prefix + val.ToString("F0"); Canvas.SetTop(label, y - 6);
                }
            }
            UpdateLineAndLabel(_upperLine, _upperLabel, UpperLimit, "上限:", true);
            UpdateLineAndLabel(_lowerLine, _lowerLabel, LowerLimit, "下限:", true);
            UpdateLineAndLabel(_stdLine, _stdLabel, StandardValue, "标准:", true);
            UpdateLineAndLabel(_avgLine, _avgLabel, avgValue, "均值:", hasData);
        }

        /// <summary>
        /// 用户交互：鼠标滚轮缩放事件。以当前鼠标悬停的 X 坐标为支点进行动态伸缩，并中断自动跟踪。
        /// </summary>
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)  
        { 
            base.OnPointerWheelChanged(e); 
            if (_canvas == null) return;
            
            var currentPos = e.GetPosition(_canvas);
            double zoomFactor = e.Delta.Y > 0 ? ZoomSpeed : 1.0 / ZoomSpeed;
            double oldXStep = _xStep;
            double newXStep = Math.Clamp(_xStep * zoomFactor, MinXStep, MaxXStep);
            
            if (oldXStep == newXStep) return;
            _xStep = newXStep;
            
            // Zoom centered on mouse
            _panX = currentPos.X - (currentPos.X - _panX) * (_xStep / oldXStep);
            _isFollowing = false; // Pause follow when user manually zooms
            
            RedrawChart();
        }
        /// <summary>
        /// 用户交互：鼠标按下事件，进入拖拽准备状态，中断自动跟踪。
        /// </summary>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true; _lastMousePos = e.GetPosition(this);
                Cursor = new Cursor(StandardCursorType.Hand); e.Pointer.Capture(this); e.Handled = true;
                _isFollowing = false;
            }
        }
        /// <summary>
        /// 用户交互：鼠标拖拽移动事件，修改 _panX 全局坐标轴平移量，并执行实时重绘。
        /// </summary>
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isDragging && _canvas != null)
            {
                var currentPos = e.GetPosition(this); 
                _panX += currentPos.X - _lastMousePos.X; 
                _lastMousePos = currentPos;
                RedrawChart();
            }
        }
        /// <summary>
        /// 用户交互：鼠标松开事件。判定如果用户把画面拖拽回距离最新数据 80% 落点不到 50px 时，
        /// 自动发出“咔哒”吸附动作，并恢复数据动态跟踪机制。
        /// </summary>
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isDragging)
            {
                _isDragging = false;
                Cursor = Cursor.Default;
                e.Pointer.Capture(null);
                
                double w = GetCanvasWidth();
                double lastPointX = Math.Max(0, (_valueBuffer.Count - 1) * _xStep);
                double targetPanX = (w * 0.8) - lastPointX;
                if (targetPanX > 0) targetPanX = 0;

                // 检测是否拖拽到了最末尾(距跟踪目标小于50px)，是的话就恢复跟踪
                if (Math.Abs(_panX - targetPanX) < 50)
                {
                    _isFollowing = true;
                    _panX = targetPanX;
                }
                RedrawChart();
            }
        }
    }
}