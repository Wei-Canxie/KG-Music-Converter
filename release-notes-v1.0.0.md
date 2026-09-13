## ✨ v1.0.0 — WinUI 3 桌面版

酷狗加密音频解密工具箱的图形界面版（原 CLI 项目 Kugo-Music-Converter Modpacks）：
从命令行菜单改为 WinUI 3 桌面应用 —— 拖放添加文件、后台解密、进度与日志实时输出，
并内置「格式整理工具」做统一转码与批量清理。

> 单文件版为 self-contained（约 222 MB），**无需安装 .NET 运行时**，下载即用；程序不需要管理员权限。

---

### 📦 下载与用法

本 Release 提供**两个版本**，按需选择：

| 资产 | 版本类型 | 体积 | 依赖 | 适用场景 |
|---|---|---|---|---|
| `KGMusicConverter.exe` | **Self-contained（全自包含）** | ~222 MB | 无（.NET 与 WinAppSDK 全部内置） | 无任何运行时的电脑、便携使用、发给小白用户。**解密引擎需自备**（见下一节） |
| `KGMusicConverter-full-framework-dependent.zip` | **框架依赖（推荐日常用）** | ~58 MB | 需 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) + [Windows App SDK Runtime](https://aka.ms/windowsappsdk/2.4/latest/windowappruntimeinstall-x64.exe) | 已装两个运行时；**解密引擎已内置**，解压即用 |

> 为什么没有"仅需 .NET（WinAppSDK 内置）"那一版：实测 `WindowsAppSDK 2.4.1-experimental`
> 在"自带 WinAppSDK 运行时 + 散文件发布"组合下启动必崩
> （`0xC000027B` stowed exception in `Microsoft.UI.Xaml.dll`，100% 复现），
> 而单文件版（同一份运行时打进 exe）与框架依赖版都正常。
> 宁可少一版，也不发一个跑不起来的包。

#### 使用方法

**Self-contained 版（单 exe）：**
1. 下载 `KGMusicConverter.exe`，放到一个独立工具目录
2. 双击运行（无需管理员权限）
3. 把要解密的音乐文件拖进窗口左侧拖放区，点「开始转换」

**框架依赖版（zip，推荐）：**
1. 下载 `KGMusicConverter-full-framework-dependent.zip` 并解压到任意目录
2. 安装依赖（如未安装）：[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows x64）+ [Windows App SDK Runtime 2.4](https://aka.ms/windowsappsdk/2.4/latest/windowappruntimeinstall-x64.exe)
3. 双击解压目录中的 `KGMusicConverter.exe` —— 解密引擎已随之解压，无需另外准备
4. 把音乐文件拖进左侧拖放区，点「开始转换」

> ⚠️ **两版区别**：
> - **Self-contained 单文件**：体积最大（~222 MB）但**零依赖**，任何 Win10/11 机器下载即用，最适合分发；
>   它是**单个 exe**，里面没有解密引擎 —— 需要把引擎放到 exe 同目录（下一节），或直接下框架依赖版。
> - **框架依赖 zip**（推荐）：体积小（~58 MB，含引擎），但**必须安装 .NET 8 与 Windows App SDK Runtime**，否则启动报缺运行时。
>
> 功能上两个版本完全一致，只是运行时打包方式不同。

---

### ⚙️ 解密引擎

**zip 两个版本已内置引擎，不需要你做任何事。** 程序首次启动会把引擎从程序目录
自动复制（"播种"）到应用工作区：

```
%LOCALAPPDATA%\KGMusicConverter\workspace\
├── unlockKuGoWin-64.exe / unlockKuGoWin-32.exe
├── kgg-dec.exe
├── kgm.mask
└── kgm-vpr-out\ffmpeg.exe
```

对比程序版本号判断是否需要重播：同版本只补缺失文件，换了版本会整体刷新。
**所以解密全程都在应用自己的工作区里进行，你的音乐文件夹不会再被引擎或中间产物污染。**

> 单文件版（`KGMusicConverter.exe`）是单独的 exe，没法把引擎一起塞进去 ——
> 请把 `unlockKuGoWin-64.exe`、`unlockKuGoWin-32.exe`、`kgg-dec.exe`、`kgm.mask`
> 放在**与 exe 相同的目录**（`ffmpeg.exe` 放在同目录的 `kgm-vpr-out\` 下），
> 首次运行时它们会被自动播种进工作区。或者直接下 zip 版，省这一步。

| 文件 | 用途 |
|---|---|
| `unlockKuGoWin-64.exe` / `-32.exe` | `.kgm` / `.kgma` / `.vpr` 解密 |
| `kgg-dec.exe` | `.kgg` 解密为 `.ogg` |
| `kgm.mask` | 解密掩码文件 |
| `kgm-vpr-out\ffmpeg.exe` | 转码（MP3 / WAV / FLAC）与格式整理工具 |

> 引擎取自原项目发行包，缺引擎时日志栏会提示「⚠ 缺少解密引擎」。

---

### ✨ 本版内容

**转换流程**

- 解密 `.kgg` / `.kgm` / `.kgma` / `.vpr` 与加密伪装的 `.flac`
- **成品各自回到源目录**：从多个不同目录拖入的文件，成品分别回到各自的目录
- **同名不同目录自动隔离**：不同目录里的同名文件会在工作区自动分配唯一名（`名字 (2)`），
  成品再改回原文件名投回各自目录 —— 不会再出现"只有一个拿到成品、另一个被静默跳过"
- **收件箱热文件夹**：丢进 `%LOCALAPPDATA%\KGMusicConverter\inbox` 的文件自动入队；
  收件箱来的成品放「成品目录」（不回投热文件夹，避免自喂循环）
- **可选统一输出**到指定目录
- **输出格式选项**（展开栏，默认收起）：解密后转 MP3 / WAV / FLAC，可多选；
  都不勾 = 只解密。MP3 与 FLAC 保留封面与元数据，WAV 装不下封面所以不映射
- **删除源文件**：转换完成后弹窗确认，删除进**回收站**（可还原），绝不静默删除
- 转换完成后临时文件自动清理；上次被强杀的话，下次启动会静默清理遗留文件

**🧰 格式整理工具**（转换页底部）

选一个目录，对**你自己的通用音频**做整理（和酷狗解密是两件事）：

- 统一转换为 MP3 / WAV / FLAC（目标格式与文件相同时跳过，不重复编码）
- **批量删除**：删除范围按扩展名筛选（展开栏，**默认只删加密文件**；
  要连音频一起删得手动勾上），需点两次确认，删除同样只进回收站

**界面**

- **V2rayN Vertical 风格转换页** — 左侧操作区 / 可拖动分割线 / 右侧与窗口内容区同高的实时日志栏
- **V2rayN 风格日志** — `2026/09/13 15:29:14.336502 [Info] [34208:1754463165] 正文`：
  时间戳（微秒）/ 级别 / **PID:会话号**（PID 区分同时运行的实例，会话号区分"同一次转换"），
  条目之间空一行；同步写入 `%TEMP%\KGMusicConverter.log`
- **左侧紧凑导航** — 收起 48px 图标条、展开 200px，带 120ms 滑动动画
- **自绘 32px 标题栏** — 配色跟随应用内主题
- **关于页** — 程序信息 + 项目详情 / 鸣谢项目分区链接
- 默认窗口 1000 × 800；**关窗即退出**（不做托盘驻留），退出前清干净临时文件

**外观设置（草稿 + 应用 / 取消更改）**

- 所有改动先写进草稿，右下角浮出「应用 / 取消更改」卡片，点「应用」才真正生效
- 主题：跟随系统 / 亮色 / 暗色（默认跟随系统）
- 主题色：HEX 输入，默认 `#33CCBB`
- 窗口不透明度（默认 95%）、背景图不透明度（默认 80%）、高斯模糊半径（0–1024px，默认 8）
- 背景效果：默认（背景图）/ 云母 (Mica) / 亚克力 (Acrylic)；后两者下不绘制背景图并锁定不透明度
- **背景图片不设默认值** —— 各人自己挑

---

### 📦 安装说明

1. 按上表选一个版本下载
2. 单文件版记得把解密引擎放到 exe 同目录（zip 版已内置）
3. 双击运行，把音乐文件拖进左侧拖放区，点「开始转换」
4. 成品回到源文件所在目录；收件箱来的文件成品放「成品目录」；勾了统一输出则都进你指定的目录

### ⚙️ 配置文件

`%LOCALAPPDATA%\KGMusicConverter\settings.json`（首次运行自动创建；
从旧版本目录 `KugoMusicConverter\` 自动迁移）

### 🖼️ 附注

- 解密引擎取自原项目发行包；zip 版内置，单文件版需自备
- UI 设计规范参考 [Wei-Canxie/winui3-tool-ui-template](https://github.com/Wei-Canxie/winui3-tool-ui-template)
- `unlockKuGoWin` 仅支持 78MB 以下的文件；`.kgg` 提示缺少密钥时，先用酷狗客户端播放一次该文件
- 本工具仅用于解密你已购买或合法获取的音乐文件，请尊重音乐版权
