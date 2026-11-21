# 应用内置字体

WBM Mixed Sans 是按 SIL OFL 1.1 更名的组合衍生字体。中文和其他非拉丁字形来自 Adobe Source Han Sans SC；U+0000–024F、U+1E00–1EFF 中两源共有的 336 个字符（包含英文与数字）来自 Google Noto Sans CJK SC。提供 400 / 700 两个字重，随 EXE 内置，通过 GDI 和 GDI+ 私有加载，不安装系统字体。

原始文件：
- https://raw.githubusercontent.com/adobe-fonts/source-han-sans/release/Variable/TTF/SourceHanSansSC-VF.ttf
- https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/Variable/TTF/Subset/NotoSansSC-VF.ttf

复现：将两份原文件分别保存到 `artifacts/temp/font-sources/SourceHanSansSC-VF.ttf`、`NotoSansSC-VF.ttf`，使用 Python 和 fontTools 4.62.1 运行 `tools/build-ui-fonts.py`。源与输出 SHA256 见 `font-build.json`，源站分支会更新，复现前须核对源哈希。授权文件保存在本目录，并合并到发行包 LICENSE。
