# 殖民地预设格式
# 当前预设固定拆成 16 个 room_*.txt，每个文件只保存一个房间。Mod 只加载这些独立文件，不加载合并总表。
#
# 每个 room_*.txt 文件是一套单房间预设。预设按需求使用：
# AI 只有在当前确实需要某个房间时才输出 PRESET <文件名>，用不到的文件和房间跳过。
# PRESET 会自动寻找当前 AI 地图的合法落点，一次性放置预设中的墙、门、设施、地板和区域状态；也可写 PRESET <文件名> <mapID> <左下角x> <左下角z> [旋转0-3] 指定位置和整体旋转。每次只建设当前文件的一个房间。
# 完成当前房间后输出 PRESET_DONE <房间ID>；需要其他房间时重新选择另一个文件。
# 坐标使用目标地图坐标；协议行中的 MAP 会由 AI 替换为当前地图 ID。
#
# 示例：
# ROOM id=R01 purpose=宿舍 priority=1
# Q MAP 20 20 32 32 Wood
# B MAP Bed 22 22 0 Wood
# END_ROOM
# ROOM id=R02 purpose=厨房 priority=2
# Q MAP 40 20 52 32 Wood
# B MAP FueledStove 45 25 0 Steel
# END_ROOM
#
# 支持写入现有协议命令（Q、B、G、S、P、C、T 等），每行一条；也可以使用 TEMPLATE、
# FOOTPRINT、LAYOUT、FACILITIES 等文字描述作为相对布局图纸。AI 需要选择地图锚点并换算坐标。
# FOOTPRINT 描述占地尺寸；从“建筑”存档导出的 BUILD 行包含墙、门及房间内设施的相对坐标、朝向和材料，FLOOR/FLOOR_HASH/ZONE/STATE 行分别记录地板网格、区域单元和床/门/制冷器状态。AI 使用房间预设时按 BUILD 顺序恢复墙门，再恢复设施与区域；两个房间之间至少留一格过道。整张布局可旋转 90/180/270 度；预设只固定房间用途/房间类型，只要用途不变即可自由增加、删除或调整墙、门、地板、设施、温控和状态，不得让房间失去有效封闭。
# 不要写 PLAN、GUIDE_DONE、
# PRESET 或 PRESET_DONE。文件放入本目录后，重启游戏或重新载入 Mod 才会刷新文件列表。
# 防御专用预设：room_defense_gate_battery.txt（大门附近，自动对齐普通外墙的左右开口并在需要时扩展墙体）；
# room_defense_active_turrets.txt（墙内主动炮台，选址时与其他建筑至少间隔两格）。
