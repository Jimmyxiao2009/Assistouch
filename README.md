# Assistouch

**Windows 桌面悬浮触控辅助工具** — 就像 iPhone 的 AssistiveTouch，但给你的 Windows 用。

一个始终置顶的悬浮按钮，点击弹出动作菜单，一键执行热键、启动应用、发送文本、运行脚本……还能根据当前窗口自动切换动作集。

![GitHub release)](https://img.shields.io/github/v/release/Jimmyxiao2009/Assistouch)
![GitHub LGPL-3.0](https://img.shields.io/badge/license-GPL%203.0-blue)

---

## ✨ 功能

| 功能 | 说明 |
|------|------|
| 🎯 **悬浮按钮** | 可拖动的全局按钮，松手自动吸附屏幕边缘 |
| 📋 **动作菜单** | 点击按钮弹出菜单，自动避开按钮位置，点击菜单项动作自动执行 |
| 🧠 **规则引擎** | 根据当前窗口的进程名/标题，自动匹配推荐的动作集 |
| 🔥 **热键注入** | 通过 Win32 `SendInput` 真实模拟按键 |
| 📝 **多种动作类型** | 热键、模拟点击、运行脚本（cmd/powershell/python）、打开 URL、启动应用、结束进程、锁屏、媒体控制、发送文本…… |
| 🖥️ **前景窗口识别** | 自动记住操作前的窗口，注入热键前切回目标窗口 |
| 🎛️ **规则 + 固定双模式** | 动作为"自动规则匹配"和"手动固定"两种模式并存 |
| 🪟 **系统托盘** | 左键菜单，右键跳过，减少干扰 |
| 📋 **崩溃日志** | 崩溃自动记录到 `%LocalAppData%\AssistiveTouch\crash.log` |

## 📸 截图

（待补充）

## 🚀 安装

### 方法一：侧载安装（推荐）

1. 前往 [Releases](https://github.com/Jimmyxiao2009/Assistouch/releases) 下载 `*_Sideload.msix`
2. 右键 → 属性 → 勾选"解除锁定"
3. 双击安装
4. 如提示需要证书，从 Release 下载 `.cer` 文件安装到"受信任的根证书颁发机构"

> 也可使用 `Add-AppDevPackage.ps1` 脚本来自动处理依赖和证书

### 方法二：开发者运行

```powershell
# 需要 Visual Studio 2022 + Windows App SDK
git clone https://github.com/Jimmyxiao2009/Assistouch.git
cd Assistouch
# 用 VS 打开 Assistouch1.slnx，选择 x64，按 F5
```

## 🛠️ 构建

项目使用 **WinUI 3** 和 **Windows App SDK**，需要：
- Visual Studio 2022（17.8+）
- .NET 8 SDK
- Windows 10 SDK (10.0.19041.0+)

```powershell
# 构建侧载包
.\build-release-sideload.ps1

# 构建 Store 上传包
.\build-store.ps1
```

构建输出：
- `Release_Output\Sideload\` — 侧载包（含依赖）
- `output\*.msixupload` — Store 上传包

## 🧩 项目结构

```
Assistouch/
├── Assistouch/                    # 主项目 (WinUI 3)
│   ├── Pages/                     # 设置页面
│   │   ├── GeneralPage.xaml       # 通用设置
│   │   ├── HomePage.xaml          # 首页概览
│   │   ├── PinnedPage.xaml        # 固定动作管理
│   │   ├── RulesPage.xaml         # 规则配置
│   │   └── WidgetsPage.xaml       # 小组件
│   ├── Services/                  # 核心服务
│   │   ├── ActionExecutor.cs      # 动作执行器(SendInput/进程/脚本)
│   │   ├── ConfigService.cs       # 配置持久化(JSON)
│   │   ├── CrashLogger.cs         # 崩溃日志
│   │   ├── ForegroundWindowService.cs  # 前景窗口轮询
│   │   ├── RuleEngine.cs          # 规则引擎
│   │   └── TrayIconService.cs     # 托盘图标
│   ├── Models/Models.cs           # 数据模型
│   ├── Win32/NativeMethods.cs     # Win32 P/Invoke
│   ├── FloatingButtonWindow.xaml   # 悬浮按钮窗口
│   ├── ActionMenuWindow.xaml      # 动作菜单弹窗
│   ├── TrayMenuWindow.xaml        # 托盘菜单
│   └── SettingsWindow.xaml        # 设置主窗口
├── Assistouch (Package)/          # WAP 打包项目
└── build-*.ps1                    # 构建脚本
```

## 📖 使用入门

1. 启动 Assistouch，桌面会出现一个半透明的悬浮按钮
2. **拖动**按钮到屏幕边缘，松手自动吸附
3. **点击**按钮 → 弹出动作菜单
4. 菜单自动包含"固定动作"和"当前窗口匹配的推荐动作"
5. 点击任意动作 → 自动执行，菜单关闭
6. 右键托盘图标 → 设置 → 自定义动作和规则

### 规则示例

| 规则 | 匹配条件 | 推荐动作 |
|------|---------|---------|
| 浏览器 | 进程名 `chrome.exe` / `msedge.exe` | Ctrl+T 新建标签页 / Ctrl+W 关闭标签页 |
| 开发工具 | 窗口标题含 `Visual Studio` | Ctrl+B 编译 / Ctrl+F5 运行 |
| 全局 | 所有窗口 | 锁屏 / 打开计算器 / 显示桌面 |

## 🧠 技术亮点

- **WinUI 3 + WAP 打包**：同时支持 MSIX 打包和免打包运行
- **`WS_EX_TOOLWINDOW` + `HWND_TOPMOST`**：悬浮按钮不显示在任务栏，始终置顶
- **前景窗口捕获时机**：在 `PointerPressed` 时捕获（按钮尚未抢焦），确保热键发到正确窗口
- **`Shell_NotifyIcon`**：纯 Win32 托盘，不依赖任何第三方库
- **COM 安全**：`IShellApplication` 的 `[ComImport]` 替代 `dynamic`，避免修剪警告
- **系统拦截快捷键覆盖**：Win+L → `LockWorkStation()`、Win+D → `ToggleDesktop()` 等

## 📄 许可证

MIT License

## 🙏 致谢

灵感来自 iOS AssistiveTouch 和 macOS 的类似辅助触控方案。
