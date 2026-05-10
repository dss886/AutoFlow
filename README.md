# AutoFlow

基于 `C# + WPF + Lua` 的 Windows 自动化脚本宿主工具。

## 功能

- 扫描固定目录 `scripts/` 下的 Lua 脚本
- 在 GUI 中查看脚本列表、运行状态和执行日志
- 支持启动、停止、刷新脚本
- 支持鼠标移动、鼠标点击、键盘按键、延迟和 Lua 循环
- 脚本文件使用系统默认外部编辑器打开

## 运行

```powershell
dotnet build .\AutoFlow.sln
dotnet run --project .\AutoFlow.App\AutoFlow.App.csproj
```

## 异常日志

- 启动异常、UI 线程异常、未观察到的后台任务异常都会自动写入 `logs/`
- 日志文件格式：`logs/automation-host-yyyyMMdd.log`
- 发生严重异常时，程序会弹出错误提示框，并显示日志文件路径

## 脚本目录

- 固定目录：项目根目录下的 `scripts/`
- 示例脚本：`scripts/demo.lua`

## Lua API

- `host.log(message)`
- `host.sleep(milliseconds)`
- `host.stop_requested()`
- `mouse.move(x, y)`
- `mouse.click(button)`
- `mouse.down(button)`
- `mouse.up(button)`
- `keyboard.press(keys)`
- `keyboard.down(key)`
- `keyboard.up(key)`

## 按键示例

- `keyboard.press("A")`
- `keyboard.press("Ctrl+Shift+S")`
- `keyboard.press("Enter")`
- `keyboard.press("Left")`

## 鼠标示例

- `mouse.move(640, 360)`
- `mouse.click("left")`
- `mouse.click("right")`

## 注意事项

- 自动化输入会直接作用于当前系统前台环境，运行脚本前请确认目标窗口安全。
- 若目标程序以管理员权限运行，本工具通常也需要以管理员权限运行。
- 当前版本是 MVP，后续可以继续扩展录制、热键、窗口绑定、图像识别和任务编排。
