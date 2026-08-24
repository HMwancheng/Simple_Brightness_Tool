using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BrightnessWpf.Models;

namespace BrightnessWpf;

/// <summary>
/// 曲线编辑器：横轴=软件亮度(0-100)，纵轴=硬件亮度(0-100)。
/// 左键拖拽控制点/在空白处加点，右键删除控制点（端点除外），保存写入配置。
/// </summary>
public partial class CurveEditorWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly AppConfig _config;
    private readonly List<(int X, int Y)> _points = new();
    private int? _dragIndex;
    private double _plotLeft, _plotTop, _plotRight, _plotBottom;

    public CurveEditorWindow(MonitorInfo monitor, AppConfig config)
    {
        InitializeComponent();
        _monitor = monitor;
        _config = config;
        TitleText.Text = $"编辑曲线: {monitor.Name}";

        var curve = config.GetCurveForMonitor(monitor.UniqueId);
        foreach (var kv in curve.OrderBy(k => k.Key))
            _points.Add((kv.Key, kv.Value));
        if (_points.Count == 0) { _points.Add((0, 0)); _points.Add((100, 100)); }
        if (_points[0].X != 0) _points.Insert(0, (0, 0));
        if (_points[^1].X != 100) _points.Add((100, 100));
    }

    private void Render()
    {
        GraphCanvas.Children.Clear();
        double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _plotLeft = 44; _plotTop = 16; _plotRight = w - 12; _plotBottom = h - 28;

        var gridBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        var accentBrush = (Brush)(FindResource("AccentBrush") ?? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)));
        var pointBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

        // 网格与刻度
        for (int v = 0; v <= 100; v += 20)
        {
            double gx = MapX(v);
            GraphCanvas.Children.Add(new Line { X1 = gx, Y1 = _plotTop, X2 = gx, Y2 = _plotBottom, Stroke = gridBrush, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = v.ToString(), FontSize = 10, Foreground = labelBrush };
            Canvas.SetLeft(lbl, gx - 10); Canvas.SetTop(lbl, _plotBottom + 3);
            GraphCanvas.Children.Add(lbl);

            double gy = MapY(v);
            GraphCanvas.Children.Add(new Line { X1 = _plotLeft, Y1 = gy, X2 = _plotRight, Y2 = gy, Stroke = gridBrush, StrokeThickness = 1 });
            var lbl2 = new TextBlock { Text = v.ToString(), FontSize = 10, Foreground = labelBrush };
            Canvas.SetLeft(lbl2, _plotLeft - 28); Canvas.SetTop(lbl2, gy - 6);
            GraphCanvas.Children.Add(lbl2);
        }

        // 曲线
        if (_points.Count > 1)
        {
            var pc = new PointCollection();
            foreach (var p in _points) pc.Add(new Point(MapX(p.X), MapY(p.Y)));
            GraphCanvas.Children.Add(new Polyline { Points = pc, Stroke = accentBrush, StrokeThickness = 2.5 });
        }

        // 控制点
        for (int i = 0; i < _points.Count; i++)
        {
            var ell = new Ellipse { Width = 14, Height = 14, Fill = pointBrush, Stroke = accentBrush, StrokeThickness = 2, Cursor = Cursors.Hand };
            Canvas.SetLeft(ell, MapX(_points[i].X) - 7);
            Canvas.SetTop(ell, MapY(_points[i].Y) - 7);
            GraphCanvas.Children.Add(ell);
        }
    }

    private double MapX(double x) => _plotLeft + x / 100.0 * (_plotRight - _plotLeft);
    private double MapY(double y) => _plotBottom - y / 100.0 * (_plotBottom - _plotTop);
    private double UnmapX(double px) => (px - _plotLeft) / (_plotRight - _plotLeft) * 100;
    private double UnmapY(double py) => (_plotBottom - py) / (_plotBottom - _plotTop) * 100;

    private int HitTestPoint(Point pos)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            double px = MapX(_points[i].X), py = MapY(_points[i].Y);
            if (Math.Abs(pos.X - px) <= 9 && Math.Abs(pos.Y - py) <= 9) return i;
        }
        return -1;
    }

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(GraphCanvas);
        int idx = HitTestPoint(pos);

        if (e.ChangedButton == MouseButton.Left)
        {
            if (idx >= 0)
            {
                _dragIndex = idx;
            }
            else
            {
                int x = (int)Math.Round(Math.Clamp(UnmapX(pos.X), 0, 100));
                int y = (int)Math.Round(Math.Clamp(UnmapY(pos.Y), 0, 100));
                _points.Add((x, y));
                _points.Sort((a, b) => a.X.CompareTo(b.X));
                Render();
            }
            GraphCanvas.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Right && idx >= 0)
        {
            if (_points[idx].X != 0 && _points[idx].X != 100)
            {
                _points.RemoveAt(idx);
                Render();
            }
        }
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragIndex is int di)
        {
            var pos = e.GetPosition(GraphCanvas);
            int x = (int)Math.Round(Math.Clamp(UnmapX(pos.X), 0, 100));
            int y = (int)Math.Round(Math.Clamp(UnmapY(pos.Y), 0, 100));

            if (di > 0) x = Math.Max(x, _points[di - 1].X + 1);
            if (di < _points.Count - 1) x = Math.Min(x, _points[di + 1].X - 1);

            _points[di] = (x, y);
            Render();
        }
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragIndex = null;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _points.Clear();
        _points.Add((0, 0));
        _points.Add((25, 25));
        _points.Add((50, 50));
        _points.Add((75, 75));
        _points.Add((100, 100));
        Render();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dict = new Dictionary<int, int>();
        foreach (var p in _points)
            dict[p.X] = p.Y;
        _config.Curves[_monitor.UniqueId] = dict;
        _config.Save();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded) Render();
    }
}
