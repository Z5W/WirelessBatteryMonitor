# 1.2.0 验证记录

日期：2026-11-18。本轮未运行安装脚本、未重启 Explorer、未更改用户托盘缓存或开机启动项。

## 已完成

- 使用系统 .NET Framework 64 位 C# 编译器 + Microsoft.Windows.SDK.Contracts 10.0.22621.3233 编译通过。
- 自动测试 38/38：原有 20 项，加设备分组、同名设备、缓存、断连重连、通知防抖等 18 项回归断言。
- 真实扫描成功：PRO X2 SUPERSTRIKE，2.4G，71%，connected=True，stale=False。第一次修订扫描约 699 ms。
- 最终回归命令：`tools/verify.ps1 -Hardware -Tray`。自检退出码 0、扫描退出码 0、托盘诊断退出码 0；整个验证脚本通过。最终验证对象是 `dist/WirelessBatteryMoniter.exe`。

## 托盘根因及对照实验

用户确认任务栏和隐藏区均没有图标。初始 WinForms 探针三次返回 `0x80004005`，框架内部 `added=True` 不能证明 Shell 接受注册。

增加原生 Shell API 对照后，得到 `NIM_ADD=False`，紧接调用的 lastError=5。随后检查发现：

- 发布 exe 及构建 exe 继承工作区的低完整性标签。
- 程序实际进程完整性为 `S-1-16-4096`（Low），Explorer 为中完整性。
- 经用户授权，仅将 `dist/WirelessBatteryMoniter.exe` 设为中完整性；未改变工作区、系统目录或其他文件标签。
- 再运行时进程为 `S-1-16-8192`（Medium），原生 `NIM_ADD=True`，WinForms 和原生 API 都获得有效托盘矩形。
- 同时发现部分查询返回 `S_FALSE (1)` 且带有效矩形，原探针将其误判为失败。现按负 HRESULT 判失败，仍要求矩形宽高大于零。

最终 6 项通过：WinForms/原生 API 各自的静态图标、动态图标、删除后重新注册。正式应用仍保留 WinForms 托盘；原生实现仅用于对照诊断。

本轮没有重启 Explorer 或删除托盘缓存。注册及矩形测试不能替代人工验收图标像素、悬停和菜单交互。文件标签属于本机发布物属性，清理重建后需重新检查；zip 不承载 NTFS 完整性标签，解压结果仍取决于目标目录。

新增恢复措施：三次查询失败后显示主窗口、持续记录恢复状态，并提供主窗口“退出”按钮。进程日志记录完整性 SID。

参考：[Microsoft Shell_NotifyIconGetRect](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)。

## 界面与脚本

- 演示界面导出成功，并检查主窗口截图，底部退出按钮与刷新按钮布局正常。截图：`screenshot-v1.2.0-main.png`。
- build.ps1、Framework 构建脚本、验证脚本及安装脚本通过 PowerShell 语法解析。
- 安装脚本不再强杀现有进程；需要用户先退出。快捷方式直连 exe，保留已有启动偏好并迁移路径。

## 待人工/硬件验收

| 场景 | 通过条件 | 状态 |
|---|---|---|
| 当前 Windows 托盘 | 可见图标，悬停设备列表，左键窗口、右键菜单正常 | 6 项注册/矩形测试通过，交互待人工确认 |
| 双同型号接收器 | 两台分别计数、不串读 | 逻辑测试通过，缺硬件 |
| 一个罗技接收器多设备 | 各槽位设备完整列出 | 实现完成，缺硬件 |
| 蓝牙/BLE | 连接、超时、断连、电量更新正确 | 缺真实配对设备 |
| 拔插、休眠、重连 | 旧值灰显，恢复后替换且无崩溃 | 缓存逻辑测试通过，待实机操作 |
| Explorer 重启 | 图标恢复且日志记录变化 | 未执行 |
| Windows 重启和开机启动 | 按偏好启动，不重复实例 | 未执行 |
| 安装升级 | 快捷方式、已启用启动项迁移正常 | 脚本审阅，未安装 |
| SDK 构建 | dotnet restore/build 成功 | 无 SDK，未验证 |

自动测试不覆盖真实 HID 驱动取消完成、全部厂商协议、托盘像素可见性或系统重启。测试输出留在项目 `.tmp/validation/`，诊断日志与普通应用日志隔离。
