// 新的CurveEditorForm构造函数内容 - 使用TableLayoutPanel自动布局
// 这部分代码将替换UI_Forms.cs中的构造函数

public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
    _monitor = monitor;
    _config = config;
    _points = new Dictionary<int, int>(GetCurveWithFallback());
    _tooltip = new ToolTip();
    
    this.Size = new Size(900, 700);
    this.BackColor = ThemeManager.Background;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.Text = "曲线编辑器";
    this.ForeColor = ThemeManager.Text;
    this.KeyPreview = true;
    
    this.HandleCreated += (s, e) => {
        Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
    };
    
    // Main TableLayoutPanel
    TableLayoutPanel mainTable = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        RowCount = 3,
        ColumnCount = 1,
        BackColor = ThemeManager.Background
    };
    mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); // Top panel
    mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Graph
    mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // Bottom panel
    this.Controls.Add(mainTable);
    
    // Top panel with title and info
    Panel topPanel = new Panel {
        Dock = DockStyle.Fill,
        BackColor = ThemeManager.Surface,
        Padding = new Padding(20, 15, 20, 10)
    };
    mainTable.Controls.Add(topPanel, 0, 0);
    
    // Title label
    Label title = new Label { 
        Text = $"编辑: {monitor.Name}", 
        Location = new Point(20, 15), 
        AutoSize = true, 
        ForeColor = ThemeManager.Text,
        Font = new Font("Segoe UI Variable Display", 14)
    };
    topPanel.Controls.Add(title);
    
    // Info label
    Label lblInfo = new Label {
        Text = "🖱️ 点击选中/添加 | 再次拖拽移动 | 方向键微调(↑↓输出 ←→输入) | Ctrl+Z撤销 | 右键删除",
        Location = new Point(20, 45),
        AutoSize = true,
        ForeColor = ThemeManager.Accent,
        Font = new Font("Segoe UI", 9)
    };
    topPanel.Controls.Add(lblInfo);
    
    // Graph control
    _graph = new CurveGraphControl(_points, monitor, config) {
        Dock = DockStyle.Fill,
        BackColor = ThemeManager.Background
    };
    mainTable.Controls.Add(_graph, 0, 1);
    
    // Bottom panel
    Panel bottomPanel = new Panel {
        Dock = DockStyle.Fill,
        BackColor = ThemeManager.Surface,
        Padding = new Padding(20)
    };
    mainTable.Controls.Add(bottomPanel, 0, 2);
    
    // Bottom panel TableLayout
    TableLayoutPanel bottomTable = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        RowCount = 2,
        ColumnCount = 3,
        BackColor = ThemeManager.Surface
    };
    bottomTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
    bottomTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
    bottomTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
    bottomPanel.Controls.Add(bottomTable);
    
    // Row 1: Description and selected point
    Label lblDesc = new Label {
        Text = "💡 输入亮度 = 软件界面显示值  |  输出亮度 = 显示器实际亮度",
        AutoSize = true,
        ForeColor = ThemeManager.TextSecondary,
        Font = new Font("Segoe UI", 9),
        Dock = DockStyle.Fill
    };
    bottomTable.Controls.Add(lblDesc, 0, 0);
    
    FlowLayoutPanel selectedPanel = new FlowLayoutPanel {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        BackColor = ThemeManager.Surface,
        Dock = DockStyle.Fill
    };
    bottomTable.Controls.Add(selectedPanel, 1, 0);
    
    Label lblSelected = new Label {
        Text = "选中节点:",
        AutoSize = true,
        ForeColor = ThemeManager.TextSecondary,
        Margin = new Padding(0, 5, 5, 0)
    };
    selectedPanel.Controls.Add(lblSelected);
    
    Label lblSelectedValue = new Label {
        Text = "无",
        AutoSize = true,
        ForeColor = ThemeManager.Text,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 5, 0, 0)
    };
    selectedPanel.Controls.Add(lblSelectedValue);
    
    // Row 2: Controls
    FlowLayoutPanel controlsPanel = new FlowLayoutPanel {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        BackColor = ThemeManager.Surface,
        Dock = DockStyle.Fill
    };
    bottomTable.Controls.Add(controlsPanel, 0, 1);
    bottomTable.SetColumnSpan(controlsPanel, 2);
    
    CheckBox chkPreview = new CheckBox {
        Text = "实时预览",
        AutoSize = true,
        ForeColor = ThemeManager.TextSecondary,
        Checked = false,
        Margin = new Padding(0, 8, 20, 0)
    };
    controlsPanel.Controls.Add(chkPreview);
    
    CheckBox chkUnlock = new CheckBox {
        Text = "解锁 0%/100%",
        AutoSize = true,
        ForeColor = ThemeManager.TextSecondary,
        Checked = false,
        Margin = new Padding(0, 8, 20, 0)
    };
    controlsPanel.Controls.Add(chkUnlock);
    
    Win11Button saveBtn = new Win11Button { 
        Text = "保存并生效", 
        Size = new Size(120, 36),
        BackColor = ThemeManager.Accent,
        Dock = DockStyle.Right
    };
    bottomTable.Controls.Add(saveBtn, 2, 1);
    
    // Key event handler
    this.KeyDown += (s, e) => {
        // Focus graph for keyboard events
        if (!_graph.Focused && !_graph.IsCapturingKey) {
            _graph.Focus();
        }
        
        // Ctrl+Z for undo
        if (e.Control && e.KeyCode == Keys.Z) {
            _graph.Undo();
            if (_graph.SelectedPoint.HasValue && _points.ContainsKey(_graph.SelectedPoint.Value)) {
                int sx = _graph.SelectedPoint.Value;
                lblSelectedValue.Text = $"输入{sx}% → 输出{_points[sx]}%";
            } else {
                lblSelectedValue.Text = "无";
            }
            e.Handled = true;
            return;
        }
        
        // Arrow keys for adjusting selected point
        if (_graph.SelectedPoint.HasValue) {
            int x = _graph.SelectedPoint.Value;
            if (!_points.ContainsKey(x)) return;
            
            int y = _points[x];
            bool isFixed = (x == 0 || x == 100);
            bool modified = false;
            
            switch (e.KeyCode) {
                case Keys.Up:
                    if (!isFixed || _showMinMaxUnlock) {
                        y = Math.Min(100, y + 1);
                        modified = true;
                    }
                    break;
                case Keys.Down:
                    if (!isFixed || _showMinMaxUnlock) {
                        y = Math.Max(0, y - 1);
                        modified = true;
                    }
                    break;
                case Keys.Left:
                    if (!isFixed) {
                        _graph.SaveStateForUndo();
                        int newX = Math.Max(1, x - 1);
                        if (!_points.ContainsKey(newX)) {
                            _points.Remove(x);
                            _points[newX] = y;
                            _graph.SetSelectedPoint(newX);
                            lblSelectedValue.Text = $"输入{newX}% → 输出{y}%";
                            _graph.Invalidate();
                        }
                    }
                    break;
                case Keys.Right:
                    if (!isFixed) {
                        _graph.SaveStateForUndo();
                        int newX = Math.Min(99, x + 1);
                        if (!_points.ContainsKey(newX)) {
                            _points.Remove(x);
                            _points[newX] = y;
                            _graph.SetSelectedPoint(newX);
                            lblSelectedValue.Text = $"输入{newX}% → 输出{y}%";
                            _graph.Invalidate();
                        }
                    }
                    break;
            }
            
            if (modified) {
                _points[x] = y;
                lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
                _graph.Invalidate();
                if (_graph.EnablePreview) {
                    _graph.ApplyPreview(x, y);
                }
            }
            
            e.Handled = true;
        }
    };
    
    // Event handlers
    saveBtn.Click += (s, e) => {
        _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_points);
        if (_monitor.UniqueId.StartsWith("DDC_") && _monitor.UniqueId.Contains("_H")) {
            string[] parts = _monitor.UniqueId.Split('_');
            if (parts.Length >= 4) {
                string nameHash = parts[1];
                string idxStr = parts[parts.Length - 1];
                string oldId = $"DDC_{nameHash}_IDX_{idxStr}";
                _config.Curves.Remove(oldId);
            }
        }
        _config.Save();
        this.Close();
    };
    
    chkUnlock.CheckedChanged += (s, e) => {
        _showMinMaxUnlock = chkUnlock.Checked;
        _graph.ShowMinMaxEdit = _showMinMaxUnlock;
        _graph.Invalidate();
    };
    
    chkPreview.CheckedChanged += (s, e) => {
        _graph.EnablePreview = chkPreview.Checked;
    };
    
    // Handle point selection from graph
    _graph.PointSelected += (x, y) => {
        lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
    };
    
    // Handle point deletion from graph
    _graph.PointDeleted += (x) => {
        if (x != 0 && x != 100) {
            _points.Remove(x);
            lblSelectedValue.Text = "无";
            _graph.Invalidate();
        }
    };
    
    // Handle point added from graph
    _graph.PointAdded += (x, y) => {
        _points[x] = y;
        lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
        _graph.Invalidate();
    };
    
    // Handle undo from graph
    _graph.ActionUndone += () => {
        if (_graph.SelectedPoint.HasValue) {
            lblSelectedValue.Text = $"输入{_graph.SelectedPoint.Value}% → 输出{_points[_graph.SelectedPoint.Value]}%";
        } else {
            lblSelectedValue.Text = "无";
        }
    };
    
    // Ensure minimum points
    if (!_points.ContainsKey(0)) _points[0] = 0;
    if (!_points.ContainsKey(100)) _points[100] = 100;
}
