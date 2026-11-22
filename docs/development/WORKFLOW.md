# 开发与发布工作流

## 文件边界

- `src/` 保存程序实现、项目文件、清单和编译资源。根目录 `Directory.Build.props` 只调整 SDK 输出路径。
- `design/assets/` 保存设计原稿；`tools/make_icon.py` 从任意工作目录运行均写入这里。替换程序图标属于单独的源码资源变更，不自动覆盖 `src/`。
- `.tools/` 保存机器本地依赖，不提交；`artifacts/` 保存可再生输出和本次迁移保留的本地档案，不提交。
- `docs/history/` 保存历史对话，`docs/validation/reports/` 保存验收报告，`docs/validation/results/` 保存对应原始结果。不要将新的日志直接堆到项目根目录。
- 发布只使用 `docs/user/` 的两份说明；根 README 面向开发者，不直接随包分发。

## 命令（在项目根目录执行）

```powershell
.\build.ps1 -FrameworkCompiler
.\tools\verify.ps1 -Hardware -Tray
# 首次安装或升级：生成包后运行 dist/v1.4.0/Install.cmd
.\run.ps1 -Open # 仅启动固定安装目录
.\build.ps1 -FrameworkCompiler -Package
.\tools\verify-package.ps1
.\tools\verify.ps1 -Executable .\dist\v1.4.0\WirelessBatteryMoniter.exe -Hardware -Tray
```

构建默认为 Release；Debug 可用 `-Configuration Debug`，测试时显式指定对应 exe。无 `-FrameworkCompiler` 时尝试 SDK，没有 SDK 则回退系统编译器。`-VersionedOutput` 是 `-Package` 的兼容入口。版本来自编译后的文件版本。

开发输出：`artifacts/build/bin/WirelessBatteryMoniter/Release/net48/`。SDK 中间输出：`artifacts/build/obj/WirelessBatteryMoniter/`。验证输出：`artifacts/tests/validation/`。`-Clean` 只清开发构建目录。

构建后的程序需要中完整性标签才能向普通 Explorer 注册托盘图标。构建脚本仅对指定产物设置标签，权限不足时停止；不要通过更改整个工作区标签处理。

## 发布审查

发布目录为 `dist/v<版本>/`，只允许以下 10 个文件及对应 zip：

```text
WirelessBatteryMoniter.exe
WirelessBatteryMoniter.exe.config
README.md
QUICKSTART.txt
LICENSE
VERSION.txt
Install.cmd
install.ps1
Start.cmd
install-transaction.ps1
```

检查必需文件、意外文件、文档相对链接、版本、完整性标签，以及压缩包全部文件的 SHA256；同长度内容变更、重复条目和非白名单条目均拒绝。测试通过后交付 zip。若需记录验收证据，将本次测试结果复制到 `docs/validation/results/` 并编写报告；不把证据塞进用户包。

发布先在 `dist/.staging-<唯一编号>/` 准备完整目录和 zip，通过检查后才进行切换。切换时以独占锁阻止并发发布，将旧目录改名保留为 `dist/.previous-v<版本>-<唯一编号>/`，再将暂存目录改名为正式目录，并再次校验。切换或最终校验抛出异常时尝试恢复旧目录；失败暂存和旧备份保留用于排查，不自动删除。

这是带异常回滚的两次目录改名，不是断电级原子事务；进程被强制结束或磁盘故障时可能需要根据 `.previous-*` 手动恢复。回滚本身失败会明确输出保留路径。发布程序运行时应先自行退出，不强制终止用户程序。同版本备份可在确认新包稳定后人工清理，不能把备份目录当作当前交付入口。

事务回归测试：`tools/test-release-transaction.ps1`，使用 `artifacts/tests/` 中的独立目录，不修改正式发布。

## 历史材料

迁移前 `dist/` 整体保留在 `artifacts/archive/legacy-dist/`。旧源码 bin 和临时目录归档至 `artifacts/archive/`。这些是本地留档，不是新的发布目录。历史文档正文保留当时的命令和路径，Markdown 引用已按新位置修复。

`packaging/manual/` 中的脚本会改变下载标记或重启 Explorer，只留作人工排障，不自动执行，也不随常规发布包分发。

程序运行时的用户配置路径与单实例机制保持原状。开发/发布分离不代表同时运行两份独立配置的程序。UI 导出入口仍按程序实现写入当前工作目录的 `docs/`；请在 `artifacts/tests/` 下启动导出，避免覆盖归档截图。

## 固定运行身份（1.2.3 起）

`src/` 是源码；`artifacts/build/` 仅用于构建和无托盘诊断；`dist/v*/` 只分发安装包。唯一正常运行位置是 `%LOCALAPPDATA%/Programs/WirelessBatteryMoniter/WirelessBatteryMoniterTray.exe`。正常启动在路径检查通过前不会注册托盘图标，非安装路径只显示安装指引。

`run.ps1` 启动已安装版本，不再隐式运行开发 EXE。开发预览使用 `--export-ui`、`--export-icons`；逻辑和硬件诊断仍可直接运行构建 EXE。需要实际托盘交互时先安装待测包。没有绕过固定路径的开发开关。

安装器使用 `tools/release-transaction.ps1` 的同一份代码，构建时复制为包内 `install-transaction.ps1`，避免维护两套回滚实现。安装核对全部 6 个目标文件的 SHA256；应用文件切换成功后更新快捷方式和已有开机项。壳集成失败不会回滚已正确安装的文件，重跑安装可修复。文件替换仍不是断电原子事务。

`--diagnose-tray` 已取消临时 Shell 图标，报告明确只验证离线绘制。不会生成随机 GUID。安装测试可用 `-SkipShellIntegration` 禁用快捷方式/注册表操作，并在独立子进程中重定向 LOCALAPPDATA 到 artifacts/tests；普通安装不要重定向该环境变量。

本次历史发布目录已归档至 artifacts/archive/releases-before-fixed-install/，迁移映射见该目录 migration.json。安装集成测试：tools/test-install.ps1（先退出程序，测试使用 artifacts/tests 中的独立 LOCALAPPDATA，不修改真实快捷方式或注册表）。

