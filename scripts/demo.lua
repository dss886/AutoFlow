-- @name: 按住与拖拽演示
-- @description: 展示鼠标按下抬起、拖拽，以及键盘按下抬起组合

local start_x = 680
local start_y = 420
local step = 35
local steps = 4

host.log("按住与拖拽演示开始")
host.log("起点颜色: " .. screen.get_color(start_x, start_y))

mouse.move(start_x, start_y)
host.sleep(300)

host.log("按下左键并向右拖拽")
mouse.down("left")

for i = 1, steps do
    mouse.move(start_x + i * step, start_y)
    host.sleep(120)
end

mouse.up("left")
host.sleep(250)

host.log("按住 Shift 后发送字母 A")
keyboard.down("Shift")
host.sleep(120)
keyboard.press("A")
host.sleep(120)
keyboard.up("Shift")

host.log("按住与拖拽演示结束")
