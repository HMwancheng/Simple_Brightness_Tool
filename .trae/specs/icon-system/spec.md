# 图标系统改造规格说明

## Why
当前软件使用程序生成的字体图标作为任务栏图标，存在显示不一致和DPI适配问题。用户希望使用预设计的ICO图标文件，并在设置中提供多种任务栏图标样式选择。

## What Changes
- 将应用程序图标改为 `app.ico/Icon_App.ico`
- 将任务栏图标默认改为 `app.ico/Icon_Tray_Hybrid.ico`
- 在设置界面添加任务栏图标样式切换选项：
  - Hybrid（默认）: `Icon_Tray_Hybrid.ico`
  - Minimalist: `Icon_Tray_Minimalist.ico`
  - Transparent: `Icon_Tray_Transparent.ico`
- 保存用户选择的图标样式到配置文件

## Impact
- 受影响的文件：
  - `Program.cs` - 任务栏图标加载逻辑
  - `UI_Forms.cs` - 设置界面添加图标选择控件
  - `AppConfig.cs` - 添加图标样式配置项

## ADDED Requirements

### Requirement: 应用程序图标
应用程序 SHALL 使用 `app.ico/Icon_App.ico` 作为窗口图标和可执行文件图标。

#### Scenario: 应用程序启动
- **WHEN** 应用程序启动
- **THEN** 窗口图标显示为 `Icon_App.ico`

### Requirement: 任务栏图标默认样式
任务栏图标 SHALL 默认使用 `app.ico/Icon_Tray_Hybrid.ico`。

#### Scenario: 首次启动
- **WHEN** 应用程序首次启动
- **THEN** 任务栏显示 Hybrid 样式图标

### Requirement: 图标样式切换
设置界面 SHALL 提供任务栏图标样式切换功能。

#### Scenario: 切换图标样式
- **WHEN** 用户在设置中选择不同的图标样式
- **THEN** 任务栏图标立即更新为选中的样式
- **AND** 用户选择保存到配置文件

#### Scenario: 重启后保持设置
- **GIVEN** 用户已选择非默认图标样式
- **WHEN** 应用程序重启
- **THEN** 任务栏图标显示为用户选择的样式

## MODIFIED Requirements

### Requirement: 配置文件
AppConfig SHALL 添加 `TrayIconStyle` 配置项，存储用户选择的任务栏图标样式。

**取值范围**:
- `"Hybrid"` - 默认
- `"Minimalist"`
- `"Transparent"`
