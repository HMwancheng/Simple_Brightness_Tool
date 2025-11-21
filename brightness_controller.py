import sys
import json
import os
import platform
import ctypes
from ctypes import wintypes
from dataclasses import dataclass, field
from typing import List, Dict, Optional, Tuple
from PyQt6.QtWidgets import (QApplication, QSystemTrayIcon, QMenu, QWidget, 
                             QLabel, QVBoxLayout, QHBoxLayout, QSlider, QDialog,
                             QCheckBox, QPushButton, QGroupBox)
from PyQt6.QtGui import QIcon, QPixmap, QColor, QFont
from PyQt6.QtCore import (Qt, QTimer, QSize, QPoint, pyqtSignal, 
                          QThread, QObject)
import screen_brightness_control as sbc
import win32gui
import win32con

# 配置文件路径
CONFIG_PATH = os.path.join(os.getenv('APPDATA'), 'BrightnessTool', 'config.json')

# DDC/CI相关常量
DISPLAY_DEVICE_ACTIVE = 0x00000001
DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000008

# Windows API函数声明
user32 = ctypes.WinDLL('user32', use_last_error=True)
setupapi = ctypes.WinDLL('setupapi', use_last_error=True)
dxva2 = ctypes.WinDLL('dxva2.dll', use_last_error=True)

# 结构体定义
class DISPLAY_DEVICE(ctypes.Structure):
    _fields_ = [
        ('cb', wintypes.DWORD),
        ('DeviceName', ctypes.c_wchar * 32),
        ('DeviceString', ctypes.c_wchar * 128),
        ('StateFlags', wintypes.DWORD),
        ('DeviceID', ctypes.c_wchar * 128),
        ('DeviceKey', ctypes.c_wchar * 128),
    ]

class PHYSICAL_MONITOR(ctypes.Structure):
    _fields_ = [
        ('hPhysicalMonitor', wintypes.HANDLE),
        ('szPhysicalMonitorDescription', ctypes.c_wchar * 128),
    ]

# 函数原型
user32.EnumDisplayDevicesW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, ctypes.POINTER(DISPLAY_DEVICE), wintypes.DWORD
]
user32.EnumDisplayDevicesW.restype = wintypes.BOOL

dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR.argtypes = [
    wintypes.HMONITOR, ctypes.POINTER(wintypes.DWORD)
]
dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR.restype = wintypes.BOOL

dxva2.GetPhysicalMonitorsFromHMONITOR.argtypes = [
    wintypes.HMONITOR, wintypes.DWORD, ctypes.POINTER(PHYSICAL_MONITOR)
]
dxva2.GetPhysicalMonitorsFromHMONITOR.restype = wintypes.BOOL

dxva2.GetVCPFeatureAndVCPFeatureReply.argtypes = [
    wintypes.HANDLE, wintypes.BYTE, wintypes.LPVOID, wintypes.LPVOID, wintypes.LPVOID
]
dxva2.GetVCPFeatureAndVCPFeatureReply.restype = wintypes.BOOL

dxva2.SetVCPFeature.argtypes = [
    wintypes.HANDLE, wintypes.BYTE, wintypes.DWORD
]
dxva2.SetVCPFeature.restype = wintypes.BOOL

dxva2.DestroyPhysicalMonitor.argtypes = [wintypes.HANDLE]
dxva2.DestroyPhysicalMonitor.restype = wintypes.BOOL

@dataclass
class ScreenConfig:
    """屏幕配置类"""
    id: str
    name: str
    type: str  # 'internal' 或 'external'
    min_brightness: int = 0
    max_brightness: int = 100
    current_brightness: int = 50
    monitor_handle: Optional[int] = None  # 用于DDC/CI操作的句柄

@dataclass
class AppConfig:
    """应用程序配置"""
    use_old_ddc_detection: bool = False
    brightness_step: int = 5
    show_overlay: bool = True

class DDCController:
    """内置DDC/CI控制器"""
    
    @staticmethod
    def get_external_monitors_new_method() -> List[Tuple[str, str, Optional[int]]]:
        """新的DDC/CI显示器检测方法"""
        monitors = []
        display_device = DISPLAY_DEVICE()
        display_device.cb = ctypes.sizeof(DISPLAY_DEVICE)
        
        dev_num = 0
        while user32.EnumDisplayDevicesW(None, dev_num, ctypes.byref(display_device), 0):
            if display_device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP:
                # 获取显示器句柄
                hmonitor = user32.MonitorFromDisplayName(display_device.DeviceName, 0)
                if hmonitor:
                    # 获取物理显示器数量
                    count = wintypes.DWORD()
                    if dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hmonitor, ctypes.byref(count)):
                        if count.value > 0:
                            physical_monitors = (PHYSICAL_MONITOR * count.value)()
                            if dxva2.GetPhysicalMonitorsFromHMONITOR(hmonitor, count.value, physical_monitors):
                                for i in range(count.value):
                                    monitor_name = physical_monitors[i].szPhysicalMonitorDescription
                                    monitor_handle = physical_monitors[i].hPhysicalMonitor
                                    monitors.append((
                                        f"external_{dev_num}_{i}",
                                        f"{monitor_name}",
                                        monitor_handle
                                    ))
            dev_num += 1
            display_device = DISPLAY_DEVICE()
            display_device.cb = ctypes.sizeof(DISPLAY_DEVICE)
        
        return monitors
    
    @staticmethod
    def get_external_monitors_old_method() -> List[Tuple[str, str, Optional[int]]]:
        """旧版本（V1.15.5）DDC/CI显示器检测方法"""
        monitors = []
        
        # 模拟旧版本的检测逻辑
        import winreg
        
        try:
            # 从注册表读取显示器信息
            reg_path = r"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration"
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, reg_path)
            
            i = 0
            while True:
                try:
                    subkey_name = winreg.EnumKey(key, i)
                    subkey = winreg.OpenKey(key, subkey_name)
                    
                    # 检查是否是连接到桌面的显示器
                    try:
                        conn_status = winreg.QueryValueEx(subkey, "DisplayConnectionStatus")[0]
                        if conn_status == 2:  # 已连接
                            dev_num = len(monitors)
                            monitors.append((
                                f"external_old_{dev_num}",
                                f"外接显示器 {dev_num + 1}",
                                None  # 旧方法不直接获取句柄
                            ))
                    except WindowsError:
                        pass
                    
                    winreg.CloseKey(subkey)
                    i += 1
                except WindowsError:
                    break
            
            winreg.CloseKey(key)
        except Exception as e:
            print(f"旧方法检测显示器失败: {e}")
        
        return monitors
    
    @staticmethod
    def get_brightness(monitor_handle: int) -> Optional[int]:
        """获取DDC/CI显示器亮度"""
        if not monitor_handle:
            return None
        
        try:
            feature_code = 0x10  # VCP code for brightness
            current_value = wintypes.DWORD()
            max_value = wintypes.DWORD()
            
            if dxva2.GetVCPFeatureAndVCPFeatureReply(
                monitor_handle, feature_code, None, 
                ctypes.byref(current_value), ctypes.byref(max_value)
            ):
                if max_value.value > 0:
                    return int((current_value.value / max_value.value) * 100)
        except Exception as e:
            print(f"获取亮度失败: {e}")
        
        return None
    
    @staticmethod
    def set_brightness(monitor_handle: int, percent: int) -> bool:
        """设置DDC/CI显示器亮度"""
        if not monitor_handle:
            return False
        
        try:
            feature_code = 0x10  # VCP code for brightness
            max_value = wintypes.DWORD()
            
            # 获取最大亮度值
            if dxva2.GetVCPFeatureAndVCPFeatureReply(
                monitor_handle, feature_code, None, None, ctypes.byref(max_value)
            ):
                if max_value.value > 0:
                    target_value = int((percent / 100) * max_value.value)
                    return dxva2.SetVCPFeature(monitor_handle, feature_code, target_value)
        except Exception as e:
            print(f"设置亮度失败: {e}")
        
        return False

class BrightnessWorker(QObject):
    """亮度调节工作线程（避免UI阻塞）"""
    brightness_updated = pyqtSignal(int)
    
    def __init__(self, controller, delta):
        super().__init__()
        self.controller = controller
        self.delta = delta
    
    def run(self):
        new_brightness = self.controller.adjust_brightness(self.delta)
        self.brightness_updated.emit(new_brightness)

class BrightnessOverlay(QWidget):
    """亮度调节提示窗口"""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint | 
            Qt.WindowType.WindowStaysOnTopHint | 
            Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        # 设置窗口大小和位置
        self.setFixedSize(200, 100)
        self.move_to_center()
        
        # 创建UI元素
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        
        # 亮度图标
        self.icon_label = QLabel()
        self.icon_label.setFixedSize(48, 48)
        self.icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.icon_label, alignment=Qt.AlignmentFlag.AlignCenter)
        
        # 亮度百分比
        self.percent_label = QLabel("50%")
        self.percent_label.setFont(QFont("Segoe UI", 12, QFont.Weight.Bold))
        self.percent_label.setStyleSheet("color: white;")
        self.percent_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.percent_label)
        
        # 进度条
        self.slider = QSlider(Qt.Orientation.Horizontal)
        self.slider.setFixedWidth(150)
        self.slider.setRange(0, 100)
        self.slider.setValue(50)
        self.slider.setStyleSheet("""
            QSlider::groove:horizontal {
                height: 8px;
                background: rgba(255,255,255,0.3);
                border-radius: 4px;
            }
            QSlider::handle:horizontal {
                width: 16px;
                height: 16px;
                background: white;
                border-radius: 8px;
                margin: -4px 0;
            }
            QSlider::sub-page:horizontal {
                background: rgba(255,255,255,0.7);
                border-radius: 4px;
            }
        """)
        layout.addWidget(self.slider, alignment=Qt.AlignmentFlag.AlignCenter)
        
        self.setLayout(layout)
        
        # 自动隐藏计时器
        self.hide_timer = QTimer()
        self.hide_timer.setSingleShot(True)
        self.hide_timer.timeout.connect(self.hide)
        
        # 设置背景半透明
        self.setStyleSheet("background-color: rgba(0,0,0,0.6); border-radius: 10px;")
    
    def move_to_center(self):
        """将窗口移动到屏幕中央下方"""
        screen_geo = QApplication.desktop().availableGeometry()
        self.move(
            (screen_geo.width() - self.width()) // 2,
            screen_geo.height() - self.height() - 100
        )
    
    def update_brightness(self, percent):
        """更新亮度显示"""
        self.slider.setValue(percent)
        self.percent_label.setText(f"{percent}%")
        
        # 设置亮度图标
        if percent <= 30:
            icon_text = "🌑"
        elif percent <= 70:
            icon_text = "🌓"
        else:
            icon_text = "☀️"
        
        self.icon_label.setText(icon_text)
        self.icon_label.setFont(QFont("Segoe UI Emoji", 24))
        
        self.show()
        self.hide_timer.start(2000)  # 2秒后自动隐藏

class BrightnessController:
    """亮度控制器核心类"""
    def __init__(self):
        self.screens: List[ScreenConfig] = []
        self.app_config: AppConfig = AppConfig()
        self.ddc_controller = DDCController()
        self.load_config()
        self.detect_screens()
    
    def load_config(self):
        """加载配置文件"""
        os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
        if os.path.exists(CONFIG_PATH):
            try:
                with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
                    config_data = json.load(f)
                    
                    # 加载屏幕配置
                    if 'screens' in config_data:
                        self.screens = [ScreenConfig(**screen) for screen in config_data['screens']]
                    
                    # 加载应用配置
                    if 'app_config' in config_data:
                        self.app_config = AppConfig(**config_data['app_config'])
            except Exception as e:
                print(f"加载配置失败: {e}")
                self.screens = []
    
    def save_config(self):
        """保存配置文件"""
        try:
            config_data = {
                'screens': [screen.__dict__ for screen in self.screens],
                'app_config': self.app_config.__dict__
            }
            with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
                json.dump(config_data, f, indent=2)
        except Exception as e:
            print(f"保存配置失败: {e}")
    
    def detect_screens(self):
        """检测所有屏幕"""
        # 清除现有屏幕列表
        self.screens.clear()
        
        # 检测内置屏幕
        try:
            internal_screens = sbc.list_monitors()
            for i, screen in enumerate(internal_screens):
                if 'internal' in screen.lower() or 'laptop' in screen.lower():
                    current_brightness = sbc.get_brightness(display=i)[0]
                    self.screens.append(ScreenConfig(
                        id=f"internal_{i}",
                        name=f"内置屏幕 {i+1}",
                        type="internal",
                        current_brightness=current_brightness
                    ))
        except Exception as e:
            print(f"检测内置屏幕失败: {e}")
        
        # 检测外接DDC/CI屏幕
        try:
            if self.app_config.use_old_ddc_detection:
                external_monitors = self.ddc_controller.get_external_monitors_old_method()
            else:
                external_monitors = self.ddc_controller.get_external_monitors_new_method()
            
            for monitor_id, monitor_name, monitor_handle in external_monitors:
                # 获取当前亮度
                current_brightness = 50
                if monitor_handle:
                    brightness = self.ddc_controller.get_brightness(monitor_handle)
                    if brightness is not None:
                        current_brightness = brightness
                
                self.screens.append(ScreenConfig(
                    id=monitor_id,
                    name=monitor_name if monitor_name else f"外接屏幕 {len(self.screens) - len(internal_screens) + 1}",
                    type="external",
                    current_brightness=current_brightness,
                    monitor_handle=monitor_handle
                ))
        except Exception as e:
            print(f"检测外接屏幕失败: {e}")
        
        # 保存配置
        self.save_config()
    
    def set_brightness(self, percent):
        """设置所有屏幕亮度"""
        for screen in self.screens:
            try:
                # 计算实际亮度值（基于屏幕的亮度范围）
                actual_percent = screen.min_brightness + (percent / 100) * (screen.max_brightness - screen.min_brightness)
                actual_percent = max(0, min(100, actual_percent))
                
                if screen.type == "internal":
                    display_idx = int(screen.id.split('_')[1])
                    sbc.set_brightness(int(actual_percent), display=display_idx)
                
                elif screen.type == "external":
                    if screen.monitor_handle:
                        self.ddc_controller.set_brightness(screen.monitor_handle, int(actual_percent))
                
                screen.current_brightness = int(actual_percent)
            except Exception as e:
                print(f"设置{screen.name}亮度失败: {e}")
        
        self.save_config()
        return int(percent)
    
    def adjust_brightness(self, delta):
        """调节亮度（增量）"""
        # 获取当前平均亮度
        if not self.screens:
            return 0
        
        current_percent = sum(s.current_brightness for s in self.screens) // len(self.screens)
        new_percent = max(0, min(100, current_percent + delta))
        
        # 设置新亮度
        self.set_brightness(new_percent)
        return new_percent
    
    def get_current_brightness(self):
        """获取当前平均亮度"""
        if not self.screens:
            return 0
        return sum(s.current_brightness for s in self.screens) // len(self.screens)
    
    def toggle_old_ddc_detection(self, enabled):
        """切换旧版DDC/CI检测方式"""
        self.app_config.use_old_ddc_detection = enabled
        self.save_config()
        self.detect_screens()

class SettingsDialog(QDialog):
    """设置对话框"""
    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.setWindowTitle("应用设置")
        self.setFixedSize(400, 300)
        
        layout = QVBoxLayout()
        
        # DDC/CI设置组
        ddc_group = QGroupBox("DDC/CI 设置")
        ddc_layout = QVBoxLayout()
        
        # 旧版检测方式选项
        self.old_ddc_checkbox = QCheckBox("使用老旧的DDC/CI检测方式 (V1.15.5)")
        self.old_ddc_checkbox.setChecked(self.controller.app_config.use_old_ddc_detection)
        self.old_ddc_checkbox.stateChanged.connect(self.on_old_ddc_changed)
        ddc_layout.addWidget(self.old_ddc_checkbox)
        
        # 刷新屏幕按钮
        refresh_btn = QPushButton("刷新屏幕列表")
        refresh_btn.clicked.connect(self.controller.detect_screens)
        ddc_layout.addWidget(refresh_btn)
        
        ddc_group.setLayout(ddc_layout)
        layout.addWidget(ddc_group)
        
        # 亮度设置组
        brightness_group = QGroupBox("亮度设置")
        brightness_layout = QVBoxLayout()
        
        # 调节步长
        step_layout = QHBoxLayout()
        step_layout.addWidget(QLabel("亮度调节步长:"))
        self.step_slider = QSlider(Qt.Orientation.Horizontal)
        self.step_slider.setRange(1, 10)
        self.step_slider.setValue(self.controller.app_config.brightness_step)
        self.step_slider.valueChanged.connect(self.on_step_changed)
        self.step_label = QLabel(f"{self.controller.app_config.brightness_step}%")
        step_layout.addWidget(self.step_slider)
        step_layout.addWidget(self.step_label)
        brightness_layout.addLayout(step_layout)
        
        # 显示提示条选项
        self.overlay_checkbox = QCheckBox("调节时显示亮度提示条")
        self.overlay_checkbox.setChecked(self.controller.app_config.show_overlay)
        self.overlay_checkbox.stateChanged.connect(self.on_overlay_changed)
        brightness_layout.addWidget(self.overlay_checkbox)
        
        brightness_group.setLayout(brightness_layout)
        layout.addWidget(brightness_group)
        
        # 保存提示
        save_label = QLabel("设置会自动保存")
        save_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(save_label)
        
        self.setLayout(layout)
    
    def on_old_ddc_changed(self, state):
        """旧版DDC检测方式变更"""
        self.controller.toggle_old_ddc_detection(state == Qt.CheckState.Checked)
    
    def on_step_changed(self, value):
        """步长变更"""
        self.controller.app_config.brightness_step = value
        self.step_label.setText(f"{value}%")
        self.controller.save_config()
    
    def on_overlay_changed(self, state):
        """提示条显示选项变更"""
        self.controller.app_config.show_overlay = state == Qt.CheckState.Checked
        self.controller.save_config()

class SystemTrayApp(QApplication):
    """系统托盘应用"""
    def __init__(self, args):
        super().__init__(args)
        
        # 创建控制器
        self.controller = BrightnessController()
        
        # 创建提示窗口
        self.overlay = BrightnessOverlay()
        
        # 创建托盘图标
        self.tray_icon = QSystemTrayIcon(self)
        self.tray_icon.setIcon(self.create_default_icon())
        
        # 创建托盘菜单
        self.tray_menu = QMenu()
        
        # 亮度调节菜单项
        brightness_menu = QMenu("亮度调节")
        for percent in [0, 25, 50, 75, 100]:
            brightness_menu.addAction(f"{percent}%", lambda p=percent: self.set_brightness_and_show(p))
        
        # 屏幕设置菜单项
        settings_menu = QMenu("设置")
        
        # 亮度范围设置
        settings_menu.addAction("亮度范围设置", self.show_brightness_settings)
        
        # 应用设置
        settings_menu.addAction("应用设置", self.show_app_settings)
        
        # 退出菜单项
        exit_action = self.tray_menu.addAction("退出")
        exit_action.triggered.connect(self.quit)
        
        # 组装菜单
        self.tray_menu.addMenu(brightness_menu)
        self.tray_menu.addMenu(settings_menu)
        self.tray_menu.addSeparator()
        self.tray_menu.addAction(exit_action)
        
        self.tray_icon.setContextMenu(self.tray_menu)
        self.tray_icon.activated.connect(self.on_tray_activated)
        
        # 显示托盘图标
        self.tray_icon.show()
    
    def create_default_icon(self):
        """创建默认图标"""
        pixmap = QPixmap(32, 32)
        pixmap.fill(QColor(255, 200, 0))
        return QIcon(pixmap)
    
    def on_tray_activated(self, reason):
        """托盘图标激活事件"""
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            # 左键单击显示当前亮度
            current_brightness = self.controller.get_current_brightness()
            if self.controller.app_config.show_overlay:
                self.overlay.update_brightness(current_brightness)
    
    def wheelEvent(self, event):
        """滚轮事件处理"""
        # 检查鼠标是否在托盘图标上
        tray_pos = self.tray_icon.geometry().center()
        mouse_pos = QCursor.pos()
        
        if (mouse_pos.x() - tray_pos.x()) **2 + (mouse_pos.y() - tray_pos.y())** 2 <= 1000:  # 粗略判断
            step = self.controller.app_config.brightness_step
            delta = step if event.angleDelta().y() > 0 else -step
            
            # 在新线程中调节亮度
            self.worker = BrightnessWorker(self.controller, delta)
            self.worker_thread = QThread()
            self.worker.moveToThread(self.worker_thread)
            self.worker.brightness_updated.connect(self.on_brightness_updated)
            self.worker_thread.started.connect(self.worker.run)
            self.worker_thread.start()
    
    def on_brightness_updated(self, brightness):
        """亮度更新回调"""
        if self.controller.app_config.show_overlay:
            self.overlay.update_brightness(brightness)
    
    def set_brightness_and_show(self, percent):
        """设置亮度并显示提示"""
        self.controller.set_brightness(percent)
        if self.controller.app_config.show_overlay:
            self.overlay.update_brightness(percent)
    
    def show_brightness_settings(self):
        """显示亮度范围设置窗口"""
        dialog = QDialog()
        dialog.setWindowTitle("亮度范围设置")
        dialog.setFixedSize(400, 300)
        
        layout = QVBoxLayout()
        
        for screen in self.controller.screens:
            screen_layout = QHBoxLayout()
            
            # 屏幕名称
            name_label = QLabel(screen.name)
            screen_layout.addWidget(name_label)
            
            # 最小值滑块
            min_slider = QSlider(Qt.Orientation.Horizontal)
            min_slider.setRange(0, 100)
            min_slider.setValue(screen.min_brightness)
            min_slider.valueChanged.connect(lambda val, s=screen: setattr(s, 'min_brightness', val))
            screen_layout.addWidget(QLabel("最小:"))
            screen_layout.addWidget(min_slider)
            
            # 最大值滑块
            max_slider = QSlider(Qt.Orientation.Horizontal)
            max_slider.setRange(0, 100)
            max_slider.setValue(screen.max_brightness)
            max_slider.valueChanged.connect(lambda val, s=screen: setattr(s, 'max_brightness', val))
            screen_layout.addWidget(QLabel("最大:"))
            screen_layout.addWidget(max_slider)
            
            layout.addLayout(screen_layout)
        
        # 保存按钮
        save_btn = QLabel("关闭窗口自动保存设置")
        save_btn.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(save_btn)
        
        dialog.setLayout(layout)
        dialog.exec()
        self.controller.save_config()
    
    def show_app_settings(self):
        """显示应用设置窗口"""
        dialog = SettingsDialog(self.controller)
        dialog.exec()

def main():
    """主函数"""
    # 确保Windows系统
    if platform.system() != 'Windows':
        print("此软件仅支持Windows系统！")
        return
    
    # 创建应用
    app = SystemTrayApp(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    
    # 运行应用
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
