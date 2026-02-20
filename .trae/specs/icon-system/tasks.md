# 图标系统改造任务列表

- [x] Task 1: 修改应用程序图标为 Icon_App.ico
  - [x] SubTask 1.1: 修改项目文件 (.csproj) 设置应用程序图标
  - [x] SubTask 1.2: 修改主窗口图标加载逻辑

- [x] Task 2: 修改任务栏图标加载逻辑
  - [x] SubTask 2.1: 在 AppConfig 中添加 TrayIconStyle 配置项
  - [x] SubTask 2.2: 修改 IconDrawer 类，支持从 ICO 文件加载图标
  - [x] SubTask 2.3: 修改 Program.cs 根据配置加载对应图标

- [x] Task 3: 在设置界面添加图标样式切换选项
  - [x] SubTask 3.1: 在 SettingsForm 中添加图标选择下拉框
  - [x] SubTask 3.2: 实现图标切换即时预览功能
  - [x] SubTask 3.3: 保存用户选择到配置文件

# Task Dependencies
- Task 2 依赖 Task 1
- Task 3 依赖 Task 2
