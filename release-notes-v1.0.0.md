# KG Music Converter v1.0.0

WinUI 3 桌面版首次正式发版（原 Kugo-Music-Converter Modpacks 的图形界面重构版）。

---

## 界面

- **V2rayN Vertical 风格转换页** — 左侧操作区 / 可拖动分割线 / 右侧与窗口内容区同高的实时日志栏；
  拖动分割线调整左右占比，控件与文字自动换行
- **左侧紧凑导航** — 收起 48px 图标条、展开 200px，带 120ms 滑动动画；侧边栏整列背景 +
  靠内容一侧 12px 圆角
- **自绘 32px 标题栏** — 配色跟随应用内主题，最小化 / 最大化 / 关闭按钮自绘
- **关于页** — 程序信息 + 项目详情 / 鸣谢项目分区链接

## 外观设置（草稿 + 应用 / 取消更改）

改动先写进草稿，右下角浮出「应用 / 取消更改」卡片，点「应用」才真正生效——避免了"边改边生效"
带来的误操作，也不会在拖动滑条时重建页面。

- 主题：跟随系统 / 亮色 / 暗色
- 主题色：HEX 输入，默认 `#ff66ab`
- 窗口不透明度（云母 / 亚克力激活时固定 100%，整行禁用并改写文案）
- 背景图片 + 背景图不透明度 + 高斯模糊半径（0–1024px）
- 背景效果：默认（背景图）/ 云母 (Mica) / 亚克力 (Acrylic)
- 设置持久化到 `%LOCALAPPDATA%\KGMusicConverter\settings.json`（旧目录配置自动迁移）

## 功能

- 解密 `.kgg` / `.kgm` / `.kgma` / `.vpr` 与加密伪装的 `.flac`
- 可选批量转 MP3（保留元数据与封面）
- 拖放添加文件、后台解密、进度与日志实时输出
- 转换页运行选项（跳过复制 / 仅解密 / 统一输出目录）会被记住

## 性能与稳定性

- 背景图高斯模糊：先降采样到 512px、核半径封顶、并行卷积并缓存结果
- 页面切换会注销事件处理器（不再累积泄漏的订阅），并恢复上次滚动位置
- 诊断日志：`%TEMP%\KGMusicConverter.log`

## 本版修复

- 云母 / 亚克力模式下不再绘制背景图（原先背景图直接盖住材质）
- 外观设置不再即时预览，必须点「应用」才生效
- 长文件名不再把「选择图片… / 清除」按钮顶出可视区（改为星列约束 + 省略号 + 悬停看全路径）
- 修复启动导航目标被 `NavigationView.Loaded` 默认选中项覆盖的问题

---

## 下载说明

| 文件 | 说明 |
|------|------|
| `KGMusicConverter.exe` | **单文件版**（不含 .NET 运行时）。需要先安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `KGMusicConverter-framework-dependent.zip` | **完整目录版**（框架依赖）。解压后运行 `KGMusicConverter.exe`，同样需要 .NET 8 Desktop Runtime |

> 两个版本都自带 Windows App SDK 运行时，无需额外安装。
> 首次运行前，请把解密引擎（`unlockKuGoWin-64.exe` / `kgg-dec.exe` / `kgm.mask`）放到程序同目录。

## 系统要求

- Windows 10 1809 (17763) 或更高 / Windows 11
- .NET 8 Desktop Runtime
- x64

---

## 注意事项

- `.kgg` 解密失败并提示缺少密钥时，先用酷狗音乐客户端播放一次该文件以获取密钥缓存
- `unlockKuGoWin` 仅支持 78MB 以下的文件
- 本工具仅用于解密你已购买或合法获取的音乐文件，请尊重音乐版权

## 鸣谢

- 原始 CLI 项目：[hu568/Kugo-Music-Converter-Modpacks](https://github.com/hu568/Kugo-Music-Converter-Modpacks)
- UI 设计规范：[Wei-Canxie/winui3-tool-ui-template](https://github.com/Wei-Canxie/winui3-tool-ui-template)
