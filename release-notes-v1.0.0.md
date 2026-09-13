## ✨ v1.0.0 — WinUI 3 桌面版首发

酷狗加密音频解密工具箱的图形界面重构版（原 CLI 项目 Kugo-Music-Converter Modpacks）：
从命令行菜单改为 WinUI 3 桌面应用 —— 拖放添加文件、后台解密、进度与日志实时输出。

> 单文件版为 self-contained（约 222 MB），**无需安装 .NET 运行时**，下载即用；程序不需要管理员权限。
---

### 📦 下载与用法

本 Release 提供**三个版本**，按需选择：

| 资产 | 版本类型 | 体积 | 依赖 | 适用场景 |
|---|---|---|---|---|
| `KGMusicConverter.exe` | **Self-contained（全自包含）** | ~222 MB | 无（.NET 与 WinAppSDK 全部内置） | 无任何运行时的电脑、便携使用、发给小白用户 |
| `KGMusicConverter-dotnet-only.zip` | **仅需 .NET（推荐）** | ~56 MB | 仅需 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)；Windows App SDK 已内置 | 已装 .NET 8 的电脑；体积适中、免装 WinAppSDK |
| `KGMusicConverter-full-framework-dependent.zip` | **完全框架依赖** | ~27 MB | 需 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) + [Windows App SDK Runtime](https://aka.ms/windowsappsdk/2.4/latest/windowsappruntimeinstall-x64.exe) | 追求最小下载体积，且两个运行时都已安装 |

#### 使用方法

**Self-contained 版（单 exe）：**
1. 下载 `KGMusicConverter.exe`，放到一个独立工具目录
2. 解压音乐文件夹或直接双击运行（无需管理员权限）
3. 把要解密的音乐文件拖进窗口左侧拖放区，点「开始转换」

**仅需 .NET 版（zip，推荐）：**
1. 下载 `KGMusicConverter-dotnet-only.zip` 并解压到任意目录
2. 如未安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows x64），先安装
3. 双击解压目录中的 `KGMusicConverter.exe`
4. 把音乐文件拖进左侧拖放区，点「开始转换」

**完全框架依赖版（zip）：**
1. 下载 `KGMusicConverter-full-framework-dependent.zip` 并解压
2. 安装依赖（如未安装）：[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) + [Windows App SDK Runtime 2.4](https://aka.ms/windowsappsdk/2.4/latest/windowsappruntimeinstall-x64.exe)
3. 双击解压目录中的 `KGMusicConverter.exe`
4. 把音乐文件拖进左侧拖放区，点「开始转换」

> ⚠️ **三版区别**：
> - **Self-contained**：体积最大（~222 MB）但**零依赖**，任何 Win10/11 机器下载即用，最适合分发。
> - **仅需 .NET**（推荐）：体积适中（~56 MB），Windows App SDK 已内置，**只需装 .NET 8** 即可运行；兼顾体积与便捷。
> - **完全框架依赖**：体积最小（~27 MB），但**必须同时安装 .NET 8 和 Windows App SDK Runtime**，否则启动报缺运行时。
>
> 功能上三个版本完全一致，只是运行时打包方式不同。

---

### ⚙️ 解密引擎（必需）

程序本身只负责界面与流程编排，实际解密由酷狗解密引擎完成。
请把下列文件放在**与要转换的音乐文件相同的目录**（程序以首个加入队列的音乐文件所在目录作为工作目录）：

| 文件 | 用途 |
|---|---|
| `unlockKuGoWin-64.exe` | `.kgm` / `.kgma` / `.vpr` 解密 |
| `kgg-dec.exe` | `.kgg` 解密为 `.ogg` |
| `kgm.mask` | 解密掩码文件 |
| `kgm-vpr-out\ffmpeg.exe` | 转 MP3（勾选转码时需要） |

> 这些引擎来自原项目发行包，本 Release 不包含它们。
> 缺引擎时日志栏会提示「⚠ unlockKuGoWin 未找到」。

---

### ✨ 本版内容

**界面**

- **V2rayN Vertical 风格转换页** — 左侧操作区 / 可拖动分割线 / 右侧与窗口内容区同高的实时日志栏；
  拖动分割线调整左右占比，控件与文字自动换行
- **左侧紧凑导航** — 收起 48px 图标条、展开 200px，带 120ms 滑动动画
- **自绘 32px 标题栏** — 配色跟随应用内主题，最小化 / 最大化 / 关闭按钮自绘
- **关于页** — 程序信息 + 项目详情 / 鸣谢项目分区链接

**外观设置（草稿 + 应用 / 取消更改）**

- 所有改动先写进草稿，右下角浮出「应用 / 取消更改」卡片，点「应用」才真正生效
- 主题：跟随系统 / 亮色 / 暗色
- 主题色：HEX 输入，默认 `#ff66ab`
- 窗口不透明度（云母 / 亚克力激活时固定 100%，整行禁用并改写文案）
- 背景图片 + 背景图不透明度 + 高斯模糊半径（0–1024px）
- 背景效果：默认（背景图）/ 云母 (Mica) / 亚克力 (Acrylic)
- 云母 / 亚克力模式下不绘制背景图（否则背景图会直接盖住材质）

**功能**

- 解密 `.kgg` / `.kgm` / `.kgma` / `.vpr` 与加密伪装的 `.flac`
- 可选批量转 MP3（保留元数据与封面）
- 拖放添加文件、后台解密、进度与日志实时输出
- 转换页运行选项（跳过复制 / 仅解密 / 统一输出目录）会被记住

**性能与稳定性**

- 背景图高斯模糊：先降采样到 512px、核半径封顶、并行卷积并缓存结果
- 页面切换会注销事件处理器（不再累积泄漏的订阅），并恢复上次滚动位置
- 诊断日志：`%TEMP%\KGMusicConverter.log`

---

### 📦 安装说明

1. 按上表选一个版本下载
2. 放好解密引擎（见上面「解密引擎」一节）
3. 双击运行，把音乐文件拖进左侧拖放区
4. 产物在源文件旁的 `kgm-vpr-out\`，或你指定的统一输出目录

### ⚙️ 配置文件

`%LOCALAPPDATA%\KGMusicConverter\settings.json`（首次运行自动创建；
从旧版本目录 `KugoMusicConverter\` 自动迁移）

### 🖼️ 附注

- 解密引擎取自原项目发行包，本 Release 不分发
- UI 设计规范参考 [Wei-Canxie/winui3-tool-ui-template](https://github.com/Wei-Canxie/winui3-tool-ui-template)
- `unlockKuGoWin` 仅支持 78MB 以下的文件；`.kgg` 提示缺少密钥时，先用酷狗客户端播放一次该文件
- 本工具仅用于解密你已购买或合法获取的音乐文件，请尊重音乐版权
