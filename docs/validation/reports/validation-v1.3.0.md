# v1.3.0 发布验证

日期：2026-11-21。

- 检查了 v1.2.3 以来的源码改动，并合并 GitHub 上的 README 精简修改。
- AssemblyInfo、项目版本、用户说明和快速入门统一为 1.3.0；安装测试从项目读取版本，不再固定测试旧包。
- `build.ps1 -FrameworkCompiler -Package` 成功，输出 `dist/v1.3.0/`。
- 针对发布 EXE 执行 `tools/verify.ps1`：104 项逻辑自检通过。
- 针对发布 EXE 在 Windows PowerShell 桌面会话执行 `tools/test-ui.ps1`：52 项界面检查通过；检查生成的主界面截图。受限会话首次运行界面测试未成功，桌面会话重试通过。
- `tools/test-release-transaction.ps1`：8 项检查通过，覆盖失败回滚、首次发布、旧版本保留及并发锁。
- 包含 10 个白名单文件，文件版本为 1.3.0.0，完整性标签为 Medium；ZIP 各文件 SHA256 与发布目录一致。
- `tools/test-install.ps1` 在首次安装前因已存在运行实例被保护检查阻止。没有关闭用户程序，没有修改真实安装目录、快捷方式或开机项；本次不宣称安装回归通过。
- 本次未运行真实硬件扫描、真实托盘交互或多显示器 DPI 测试；离屏 DPI 布局测试不能替代这些验证。

原始结果和界面截图保存在本地 `artifacts/tests/`，不随源代码公开或打入发布包。
