-- @name: 颜色采样与基础输入
-- @description: 读取屏幕坐标颜色，移动鼠标并点击，然后发送组合键

local points = {
    { x = 560, y = 320 },
    { x = 640, y = 360 },
    { x = 720, y = 400 },
}

for i = 1, #points do
    local point = points[i]
    local color = screen.get_color(point.x, point.y)
    host.log("采样点 " .. i .. " 坐标 (" .. point.x .. ", " .. point.y .. ") 颜色: " .. color)

    mouse.move(point.x, point.y)
    host.sleep(250)
end

local target = points[2]
host.log("移动到目标点并执行左键点击")
mouse.move(target.x, target.y)
host.sleep(300)
mouse.click("left")
host.sleep(300)

host.log("发送 Ctrl+Shift+S 组合键")
keyboard.press("Ctrl+Shift+S")
host.sleep(300)
