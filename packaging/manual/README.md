# 历史人工排障脚本

这些脚本不由构建、安装或测试流程自动执行，也不随标准发布包分发。

- `Repair-Tray.cmd`：会重启 Explorer 并重置托盘缓存，影响其他应用的托盘状态。
- `Unblock.cmd`：递归清除脚本所在目录的下载标记。如确有需要，应先审查脚本并仅复制到可信的目标解压目录运行；直接在本目录执行不会修复发布目录。

常规托盘排障优先使用程序 `--diagnose-tray` 和 `tools/repair-release-integrity.ps1`。
