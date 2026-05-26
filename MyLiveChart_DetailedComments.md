# MyLiveChart 折线图核心文件详解 (MyLiveChart.xaml.cs)

**此文档对 `MyLiveChart.xaml.cs` 里的核心逻辑引擎进行逐行和分块拆解，用以未来代码交接和深度理解修改的工业级波形图原理。**

---

### 一、 核心内存状态与缓冲块 (Line 55-79)
```csharp
// 全局记录：存放接收到的所有的历史真实数字数据。最大容量 100,000 个。
private List<double> _valueBuffer = new List<double>();
// 与 _valueBuffer 同步，存放记录对应进入的时间。
private List<DateTime> _timeBuffer = new List<DateTime>();

// 记录上一次数据输入进来时的列表对象元素个数（用于增量更新判定）。
private int _lastInputListCount = 0;

// Avalonia 原生 UI 组件对象的引用
private Polyline? _polyline;    // 波浪线主体
private Canvas? _canvas;        // 折线图绘制主画板区域
private Canvas? _xAxisCanvas;   // 底部 X 轴的各种尺标的画板
private TextBlock? _totalCountLabel; // 显示当前数据总量
private Line? _avgLine;         // 横穿画布的均值水平虚线
// ... 其他上限、下限、标准线

// 【核心机制参数】：缩放与滚动补偿
private double _xStep = 25.0; // 图表动态缩放间距（每个点在 X 轴的物理距离，初始25）。
private const double MinXStep = 2.0;    // 无限缩小的极限（数据密恐极限保护）。
private const double MaxXStep = 100.0;  // 放大到最大的极限。
private const double ZoomSpeed = 1.2;   // 每次鼠标滚动一格，放缩的倍率。

private double _panX = 0;        // 【灵魂参数】：当前摄影机（视口）的全局 X 轴平移补偿。一切拖动都只改变它。
private bool _isDragging = false;// 判断用户当前是否按下了鼠标左键。
private bool _isFollowing = true;// 判断当前数据产生时，画面要不要自动跟踪到最新数据（80%吸附）。
private Point _lastMousePos;     // 记录上一帧鼠标的拖拽位置，用于计算位移距离。
```

---

### 二、 外部数据接收与输入口 `UpdateChart()` (Line 145-197)
```csharp
private void UpdateChart(object rawInput)
{
    Dispatcher.UIThread.Post(() =>
    {
        try
        {
            // 这是外部传递字符串数据进来的核心入口（如："12.5, 30.1, 40"）
            if (rawInput == null) return;
            string currentStr = rawInput.ToString() ?? "";
            if (currentStr == _lastRawInput) return; // 判定优化：如果数据没变化，不进行重复渲染。
            _lastRawInput = currentStr;

            // 分割字符串提取 double 数据。
            var parts = currentStr.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var allValues = new List<double>();
            foreach (var part in parts) if (double.TryParse(part.Trim(), out double d)) allValues.Add(d);

            // 当判定这一轮传来的数据比上一轮的个数少，说明这是一次新的触发运行事件。
            bool isNewRun = allValues.Count < _lastInputListCount || _valueBuffer.Count == 0;
            if (isNewRun)
            {
                // 注意！这里不再清除 _valueBuffer，这样新一轮的数据会拼接在旧数据后面，形成历史追溯（累加效果）。
                _isFollowing = true; // 强制画面切回自动跟踪（恢复 80% 吸附）。
                _lastInputListCount = 0; // 重置指针，准备提取增量。
            }

            // 提取出本次比上次多出来的“新数据”。
            List<double> newToAppend = allValues.Skip(_lastInputListCount).ToList();
            
            if (newToAppend.Count > 0)
            {
                // 将新接收到的数据全部塞入底层的统一持久化池 _valueBuffer 中。
                foreach (var val in newToAppend)
                {
                    _valueBuffer.Add(val);
                    _timeBuffer.Add(DateTime.Now);
                }
            }
            _lastInputListCount = allValues.Count; // 更新指针。

            // 【内存防爆保护】：当内存积累到十万个数据时。
            if (_valueBuffer.Count > 100000)
            {
                // 像心电图机一样，切除最老旧的，保证不超量膨胀。 
                int removeCount = _valueBuffer.Count - 100000;
                _valueBuffer.RemoveRange(0, removeCount);
                _timeBuffer.RemoveRange(0, removeCount);
            }

            // 如果当且用户没在进行强行拖拉（干预），且处于跟随模式时，就动态算一下追踪补偿。
            if (!_isDragging && _isFollowing)
            {
                UpdateFollowPanX();
            }

            // 触发绝对坐标系终极重绘渲染核心。
            RedrawChart();
        }
        catch { }
    });
}
```

---

### 三、 追踪器与 80% 吸附算法 `UpdateFollowPanX()` (Line 206-222)
```csharp
private void UpdateFollowPanX()
{
    double w = GetCanvasWidth(); // 拿到外层画板的总宽度。
    if (w < 10) return;
    
    // 算一下全局物理空间里，最右侧最新一个圆点所在的极限坐标（如果它自己排列排到天边的话）。
    double lastPointX = Math.Max(0, (_valueBuffer.Count - 1) * _xStep);
    
    // 我们的目标是把它钉在屏幕的 80% 处。
    double targetX = w * 0.8;

    if (lastPointX < targetX)
    {
        // 如果哪怕加上老数据，总宽度还是没越过 80% 那条警戒线的话，图表靠紧最坐标原点（保持0偏移）。
        _panX = 0;
    }
    else
    {
        // 如果数据过长撑破了 80%，反向计算摄像机需要平移的数值赋予 _panX，拉回视口。
        _panX = targetX - lastPointX; 
    }
}
```

---

### 四、 核心重绘与剪裁算法引擎 `RedrawChart()` (Line 224-313)
```csharp
private void RedrawChart()
{
    // ... 前置参数获取
    double padding = 5.0; // 上下边距预留空间

    // ========== 拖拽空气墙物理规则碰撞检测 ==========
    double lastPointX = Math.Max(0, (_valueBuffer.Count - 1) * _xStep);
    
    // 【规则一】：向右拖拽查看老数据时，最多拖到最新数据卡在 80% 的地方（相当于一堵限制未来空气的墙）。
    double minPanX = (w * 0.8) - lastPointX; 
    if (minPanX > 0) minPanX = 0; // 如果数据太少连 80%都没塞满，则把这堵墙移到原点以防误触。
    
    // 【规则二】：向左拖拽滑回看新数据时，最多只能拖到图表的 Y 轴底边原点，以防坐标轴被拽飞出去（分离）。
    double maxPanX = 0; 
    
    // 如果碰壁，则卡死变量不让他继续滑行。
    if (_panX < minPanX && !_isFollowing) _panX = minPanX;
    if (_panX > maxPanX) _panX = maxPanX;


    // ========== CPU 性能渲染剪裁核心 ==========
    var polylinePoints = new List<Point>(); 
    for (int i = 0; i < _valueBuffer.Count; i++)
    {
        // 不借助图层 Transform ，全部在此处利用初中代数实时结算每一个点的屏幕“绝对坐标” 
        double px = i * _xStep + _panX;
        double py = 算高度... //（根据上限计算换算为 Y 轴位置，并防止打穿上边距）
        
        var pt = new Point(px, py);

        // 【超级性能剪裁】：这 10 万个坐标点，只有属于这个坐标段（即落在咱们屏幕视口这几十厘米框线内）的，才准被喂进图形画笔去渲染线段，没落进物理区域外的九万多个点全部被丢进黑洞，以保证超强流畅度！
        if (px >= -w && px <= w * 2) 
        {
            polylinePoints.Add(pt);
        }
    }
    _polyline.Points = polylinePoints;


    // ========== 动态隐藏标签与密集恐惧症预防 ==========
    _canvas.Children.RemoveAll(小圆点与文本); // 每次清理一下上一次的视觉点。

    bool showDetails = _xStep >= 10.0; // 如果鼠标滚轮把图表缩小到两个点相距不到 10 像素时：
    if (showDetails) // 只有不密集的情况才会进来画数字
    {
        for (int i = 0; i < _valueBuffer.Count; i++)
        {
            // 内部一样采用 px >= -50 视野外剔除算法 
            // 同时引入红橙双指交替发光算法： IBrush dotColor = i % 2 == 0 ? Brushes.Red : Brushes.Orange;
        }
    }
}
```

---

### 五、 滚轮中心缩放工业算法 `OnPointerWheelChanged()` (Line 351-369)
```csharp
protected override void OnPointerWheelChanged(PointerWheelEventArgs e) 
{ 
    // 1. 根据滚轮方向决定放大缩小系数。
    double zoomFactor = e.Delta.Y > 0 ? ZoomSpeed : 1.0 / ZoomSpeed;
    double oldXStep = _xStep;
    double newXStep = Math.Clamp(_xStep * zoomFactor, MinXStep, MaxXStep);
    
    if (oldXStep == newXStep) return; // 已经缩到极点，不处理。
    _xStep = newXStep;
    
    // 【重点：鼠标定向缩放】不按照边缘无脑缩，而是以用户鼠标目前的悬停位置（currentPos.X）为支点去动态计算补偿的偏移量。
    _panX = currentPos.X - (currentPos.X - _panX) * (_xStep / oldXStep);
    
    // 只要使用了手动滚轮，一定打断自动吸附逻辑。
    _isFollowing = false; 
    
    RedrawChart();
}
```

---

### 六、 CSV 清理守护流 `CleanUpOldFiles()` 
（位于 *CSVAPI.cs* 中）
```csharp
private void CleanUpOldFiles(string path)
{
    // 只要有任何针对 SaveData 动作的触发，就顺带从 C#底层通过 Directory.GetFiles 扫描 D:\MyData
    // 找到距今日前时间 - 14 天前的所有过期存量 csv 文件。
    // 然后静默通过 File.Delete 删除，永保硬盘不炸。
}
```
