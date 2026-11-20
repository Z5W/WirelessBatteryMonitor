# 项目目录重构验证

日期：2026-11-19。程序版本仍为 1.2.1.0。

## 修改范围

只调整项目文件布局、文档链接、构建/运行/验证/安装包装脚本，以及根目录 SDK 输出配置。`src/` 内的程序实现、项目文件和资源未修改：迁移前后 34 个文件 SHA256 一致，且源码目录不再含生成的 bin/obj。

| 原位置 | 新位置 |
|---|---|
| 根目录 对话日志.md | docs/history/对话日志.md |
| 根目录 QUICKSTART.txt | docs/user/QUICKSTART.txt（更新为纯用户说明） |
| docs/validation-*.md | docs/validation/reports/ |
| docs/ 中的原始测试文本 | docs/validation/results/ |
| docs/ 中的图片 | docs/images/ |
| Assets/ 设计资源 | design/assets/ |
| 旧 dist/ | artifacts/archive/legacy-dist/ |
| 旧 src/WirelessBatteryMoniter/bin/ | artifacts/archive/legacy-source-bin/ |
| 旧 .tmp/ | artifacts/archive/legacy-temp/ |
| 手动系统修复脚本 | packaging/manual/（不进入常规包） |

开发输出改为 artifacts/build，测试输出改为 artifacts/tests，临时构建数据改为 artifacts/temp。本地工具链仍保存在 .tools，所有本地产物和发布目录均由 .gitignore 排除。

默认 build.ps1 仅编译开发程序。-Package 才生成 dist/v<版本>，-VersionedOutput 作为兼容入口；-Clean 只清理开发构建。构建后恢复调用进程的环境变量，不将构建使用的 APPDATA 设置泄漏到后续命令。

## 验证结果

- 所有受管理 PowerShell 脚本语法检查通过。
- 34 个源码及资源文件哈希不变，基线见 source-layout-baseline.json（本地归档，不公开）。
- 开发构建成功，确认单独构建未创建 dist；APPDATA 在返回后与调用前相同。
- 开发和发布程序的 72 项自检均通过；发布记录见 selftest-layout.txt（本地归档，不公开）。
- 发布包必需文件、白名单、版本、完整性标签和归档一致性检查通过。额外逐字节检查全部 9 个归档成员与发布目录一致。
- 真实设备扫描成功：PRO X2 SUPERSTRIKE，68%，已连接且非缓存；见 scan-layout.txt（本地归档，不公开）。
- 托盘 WinForms/原生各 3 种状态检查通过，96 字符多行文字写入成功；见 tray-layout.txt（本地归档，不公开）。
- 项目 README 与 docs 内所有 Markdown 相对链接均可解析。

## 边界

本机无可用 SDK，SDK 输出路径配置已修改但未做 SDK 编译实测。未执行安装、开机启动修改或手动 Explorer 修复脚本。单实例规则及程序运行时每用户配置目录保持原状，开发构建与发布构建不会因此获得独立运行配置。

历史资料保留当时的正文和命令，Markdown 路径已修复。旧产物仅归档，未删除；新发布包不包含这些资料。源码哈希不变不意味着每次 Framework 重编译的 exe 必然逐字节相同。

