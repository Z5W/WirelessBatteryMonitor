# 托盘图标设置跳转修复

日期：2026-11-20。

菜单原来在 Windows 10 调用 control.exe /name Microsoft.NotificationAreaIcons；旧入口可能静默返回，异常回退不能覆盖该情形。

改用 Windows 10 设置深链接 ms-settings:taskbar-selectwhichicon，并显式启用 UseShellExecute。Windows 11 保留 ms-settings:taskbar 入口。启动异常时记录日志并尝试任务栏设置，第二次失败显示操作说明，避免仅写日志而没有用户反馈。此修改只导航，不改变系统设置。

发布版编译、94 项自检与发布前后全部 9 个文件 SHA256 校验通过。调用发布版 SystemSettings.OpenNotificationArea 已完成并发现 SystemSettings 进程。computer-use 的 node_repl 连续初始化失败（failed to write kernel assets，os error 3），未能目视确认最终页面标题；进程存在不作为页面正确落点的证明。

原 dist/v1.2.2 被运行中的程序锁定，事务未修改旧版。修复版发布至 dist/v1.2.2-tray-settings，需退出旧版后运行该版本。

## 后续修正：验证实际子页面导航

用户确认原 ms-settings:taskbar-selectwhichicon 仅打开首页，该 URI 方案无效，前述启动调用成功不能证明导航成功。

现在使用有效的 ms-settings:taskbar 打开任务栏设置，再通过 Windows UI Automation 调用本机实际观察到的 SystemSettings_Taskbar_SelectIconsToAppearOnTaskbar_HyperlinkButton。只查找 SystemSettings/ApplicationFrameHost 窗口及精确控件标识；不操作任何开关。后台导航避免阻塞应用 UI，重复点击有并发保护，轮询预算 8 秒（单次系统 UI Automation 调用耗时不受该预算硬性约束），失败显示手动入口说明。Windows 11 仍使用任务栏设置入口，未在 Windows 11 验证。

实测通过：从已有子页面重新导航，以及从 Windows 设置首页调用正式发布版处理函数，均验证 pageTitle=选择哪些图标显示在任务栏上，并检测到唯一控件 SystemSettings_Notifications_ShowIconsOnTaskbar_ToggleSwitch。验证脚本位于 artifacts/tests/tray-settings-navigation/verify-navigation.ps1。

Framework 编译、94 项自检、暂存和发布后的 9 文件 SHA256 校验均通过。最新正确版本发布至 dist/v1.2.2；dist/v1.2.2-tray-settings 为已被替代的旧尝试，请勿继续使用。
