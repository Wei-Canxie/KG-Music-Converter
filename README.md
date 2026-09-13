# KG Music Converter

> 酷狗音乐加密音频解密 / 转换工具箱 · WinUI 3 桌面版

一站式解密酷狗音乐的加密格式（`.kgg` / `.kgm` / `.kgma` / `.vpr` / 加密伪装的 `.flac`），
可选批量转 MP3（保留元数据与封面）。界面使用 WinUI 3 重写，支持拖放、后台解密、日志实时输出。

---

## 快速开始

1. 运行 `KGMusicConverter.exe`
2. 把加密文件拖进左侧拖放区（或点「添加文件…」）
3. 按需勾选运行选项，点「开始转换」
4. 产物在 `kgm-vpr-out/` 目录（或你指定的统一输出目录）

### 转换页运行选项

| 选项 | 说明 |
|------|------|
| 跳过复制 | 文件已在工作目录时使用，省去一次拷贝 |
| 跳过转 MP3（仅解密） | 只解密不做转码，保留 FLAC / OGG 原始格式 |
| 统一输出到指定目录 | 所有产物集中输出到一个目录，而不是各自源目录旁的 `kgm-vpr-out/` |

---

## 界面特性

- **V2rayN Vertical 风格转换页**：左侧操作区 + 可拖动分割线 + 右侧与窗口同高的实时日志栏，
  拖动分割线可调整左右占比，文字自动换行
- **外观设置（草稿 + 应用/取消更改）**：所有改动先写进草稿，点右下角「应用」才生效，
  「取消更改」可整批回退
  - 主题：跟随系统 / 亮色 / 暗色
  - 主题色：HEX 输入，默认 `#ff66ab`
  - 窗口不透明度（云母 / 亚克力激活时固定 100%）
  - 背景图片 + 背景图不透明度 + 高斯模糊半径（0–1024px，滑条覆盖 0–255）
  - 背景效果：默认（背景图）/ 云母 (Mica) / 亚克力 (Acrylic)
- **左侧紧凑导航**：收起 48px 图标条 / 展开 200px，带滑动动画
- **自绘标题栏**：32px，配色跟随应用内主题
- 设置持久化于 `%LOCALAPPDATA%\KGMusicConverter\settings.json`

> 云母 / 亚克力模式下背景图不会绘制 —— 否则背景图会直接盖住材质。

---

## 支持的加密格式

| 后缀 | 说明 | 处理方式 |
|------|------|----------|
| `.kgg` | 酷狗加密音频 | `kgg-dec.exe` 解密为 `.ogg` |
| `.kgm` / `.kgma` | 酷狗加密音频 | `unlockKuGoWin-64.exe` 自动解密 |
| `.vpr` | 酷狗加密音频 | `unlockKuGoWin-64.exe` 自动解密 |
| `.flac`（加密伪装） | 实为伪装的 KGM 格式 | 自动重命名为 `.kgm` 后解密 |

---

## 构建

需要 .NET 8 SDK + Visual Studio 2022（提供 Appx/PRI 构建任务）。

```bash
cd KGMusicConverter.UI
dotnet build -c Release
```

**必须从输出目录启动**，不要把 exe 单独复制到别处（自包含运行时不随之移动）：

```
KGMusicConverter.UI/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/KGMusicConverter.exe
```

换新构建前先结束旧实例，否则 exe 被占用会导致构建失败：

```bash
taskkill /F /IM KGMusicConverter.exe
```

### 发布单文件版（排除 .NET 运行时）

```bash
dotnet publish -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:EnableMsixTooling=true \
  -o ../Release-single
```

`-p:EnableMsixTooling=true` 是必需的（否则内嵌 `resources.pri` 生成会报错）。
目标机器需安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

---

## 项目结构

```
KGMusicConverter.UI/
├── App.xaml / App.xaml.cs        # 应用入口
├── MainWindow.cs                 # 外壳：自绘标题栏 + 导航 + 草稿/应用模型 + 外观
├── ConvertControl.cs             # 转换页（V2rayN Vertical：操作区 | 分割线 | 日志）
├── SettingsControl.cs            # 外观设置页（数值行三件套 + 单选组）
├── AboutControl.cs               # 关于页
├── ToolPage.cs                   # 页面基类：事件解绑登记 + 滚动位置
├── ConversionEngine.cs           # 解密 / 转码流程引擎
├── FileEntry.cs                  # 队列条目模型
├── Settings.cs                   # 设置模型（Load/Save/Clone/CopyFrom/Normalize）
├── ThemeManager.cs               # 主题画刷
├── GaussianBlurHelper.cs         # 背景图高斯模糊（降采样 + 并行）
├── AppLog.cs                     # 诊断日志 → %TEMP%\KGMusicConverter.log
└── app.manifest                  # PerMonitorV2 DPI
```

---

## 注意事项

- **`.kgg` 解密失败** — 提示缺少解密密钥时，先用酷狗音乐客户端播放一次该文件（获取密钥缓存）再试
- **文件大小限制** — `unlockKuGoWin` 仅支持 78MB 以下的文件
- 本工具仅用于解密你已购买或合法获取的音乐文件，请尊重音乐版权，不要传播解密后的文件

---

## 鸣谢

- 原始 CLI 项目：[hu568/Kugo-Music-Converter-Modpacks](https://github.com/hu568/Kugo-Music-Converter-Modpacks)
- UI 设计规范：[Wei-Canxie/winui3-tool-ui-template](https://github.com/Wei-Canxie/winui3-tool-ui-template)

## 开源许可

本项目基于 GPL v3 许可证发布。
