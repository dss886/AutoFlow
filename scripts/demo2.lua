-- @name: 演示脚本
-- @description: 每隔 500 毫秒移动鼠标，并发送一次 Ctrl+Shift+S 组合键。运行前请确认当前前台窗口安全可操作。

host.log("演示脚本启动")

for i = 1, 3 do
    host.log("第 " .. i .. " 轮开始")
    mouse.move(400 + i * 40, 300)
    host.sleep(500)
    mouse.click("left")
    host.sleep(500)
    keyboard.press("Ctrl+Shift+S")
    host.sleep(500)

    if host.stop_requested() then
        host.log("检测到停止请求，提前退出")
        return
    end
end

host.log("演示脚本结束")
