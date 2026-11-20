# 关于窗口文字布局修复

## 最新调整：固定窗口与移除日志路径

按用户要求将关于窗口改为固定大小（96 DPI 下客户区 540 × 410），禁止拖动缩放，关闭滚动；删除按钮下方的日志路径说明，保留正文自动换行和 DPI 缩放。

Framework 编译、发布暂存及替换后的全部 9 个文件 SHA256 校验通过，发布至 dist/v1.2.2。--export-ui 导出成功，目视检查 artifacts/tests/about-fixed/docs/screenshot-about.png，正文与按钮完整显示，底部日志路径已移除。未验证所有 Windows DPI 和多屏组合。

以下为前一次修订记录，其中可调整窗口大小和日志路径显示已由本次调整取代。

日期：2026-11-19，1.2.2 修订构建。

将固定位置和固定高度的说明改为 TableLayoutPanel 自动布局。说明文字、来源、限制、日志路径按宽度换行并自动计算高度，取消日志省略号；开启 DPI 缩放和窗口缩放，小窗口可滚动查看。来源补充当前已支持的 XINMENG。

Framework 编译与全部发布文件 SHA256 校验通过。--export-ui 成功，并目视检查截图：限制部分末行完整，日志路径换行完整显示。截图位于 artifacts/tests/about-layout/docs/screenshot-about.png。未验证所有 Windows DPI 和多屏组合。

原 dist/v1.2.2 被运行中的程序占用，事务未替换它。修订版独立发布到 dist/v1.2.2-about-layout，需先退出旧程序再启动。
