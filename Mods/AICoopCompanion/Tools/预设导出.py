#!/usr/bin/env python3
"""环世界 AI 队友：从玩家选定的存档离线导出房间预设或成品布局。

只读取存档，绝不修改或删除；输出同名时不覆盖，自动加序号。
仅使用 Python 标准库，需要 Python 3.10 及以上。
"""
import argparse
import io
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if getattr(sys, 'frozen', False):  # PyInstaller 冻结后 __file__ 在临时解包目录，只能从 exe 位置推 MOD_DIR
    MOD_DIR = Path(sys.executable).resolve().parent.parent
else:
    MOD_DIR = Path(__file__).resolve().parent.parent  # 本文件位于 <mod>/Tools
AREA_CLASS = 'Area_AICoopPreset'
BAD_CHARS = re.compile(r'[<>:"/\\|?*\x00-\x1f\s]')
RESERVED = {'CON', 'PRN', 'AUX', 'NUL', *(f'COM{i}' for i in range(1, 10)), *(f'LPT{i}' for i in range(1, 10))}
MENU_MODES = {'1': 'rooms', '2': 'layout'}


def legal_name(name: str) -> str:
    """去掉 .txt 后校验文件名：拒绝路径穿越、Windows 保留名和非法字符。"""
    name = (name or '').strip()
    if name.lower().endswith('.txt'):
        name = name[:-4]
    if (not name or len(name) > 100 or BAD_CHARS.search(name) or name.endswith('.')
            or name.split('.')[0].upper() in RESERVED):
        raise ValueError(f'{name!r} 不能作为文件名（不要用空格、路径符号、控制字符、末尾点或 Windows 保留名）')
    return name


def derive_name(stem: str, suffix: str) -> str:
    """把存档名这类自由文本派生成安全文件名，非法字符换成下划线。"""
    name = BAD_CHARS.sub('_', (stem or '').strip()).strip('._')[:60] or '存档'
    return legal_name(name + suffix)


def write_new(directory: Path, name: str, text: str) -> Path:
    """新建 name.txt；已存在则依次尝试 name_1.txt、name_2.txt，绝不覆盖。"""
    directory.mkdir(parents=True, exist_ok=True)
    title = legal_name(name)
    for index in range(1, 1000):
        path = directory / (title + ('' if index == 1 else f'_{index - 1}') + '.txt')
        try:
            with path.open('x', encoding='utf-8', newline='\n') as out:
                out.write(text.rstrip('\n') + '\n')
            return path
        except FileExistsError:
            continue
    raise ValueError('同名文件过多，请用 --out 换一个输出目录。')


def export_rooms(root: ET.Element, directory: Path, map_id=None) -> list:
    """导出命名范围：存档里的 presetExport 已含完整 ROOM/BUILD/FLOOR/ZONE/STATE 文本，原样写出。"""
    known = [int(node.findtext('uniqueID', '-1')) for node in root.findall('./game/maps/li')]
    if map_id is not None and map_id not in known:
        raise ValueError('存档里没有地图 ID ' + str(map_id) + '；现有地图：' + '、'.join(map(str, known)))
    exported, skipped = [], []
    for map_node in root.findall('./game/maps/li'):
        current = int(map_node.findtext('uniqueID', '-1'))
        if map_id is not None and current != map_id:
            continue
        for area in map_node.findall('./areaManager/areas/li'):
            if AREA_CLASS not in area.get('Class', ''):
                continue
            name = (area.findtext('presetName') or '').strip()
            text = area.findtext('presetExport') or ''
            if not (re.search(r'(?m)^ROOM ', text) and re.search(r'(?m)^FOOTPRINT \d+x\d+', text)
                    and re.search(r'(?m)^END_ROOM\s*$', text)):
                skipped.append(f'地图{current}的范围“{name or "（未命名）"}”没有导出数据，已跳过。')
                continue
            try:
                if name.upper() == 'README':
                    raise ValueError('不要与 Presets/README.txt 冲突')
                path = write_new(directory, name, text)
            except ValueError as error:
                skipped.append(f'地图{current}的范围名“{name}”不能用作文件名（{error}），已跳过。')
                continue
            notes = [line for line in text.splitlines() if line.startswith('# WARNING')]
            print(f'已导出：{path}（地图{current}，范围“{name}”，{len(notes)} 条警告）')
            for note in notes:
                print('  ' + note)
            exported.append(path)
    for note in skipped:
        print('警告：' + note)
    if not exported:
        raise ValueError('没有可导出的范围。请用新版 Mod 拖动“殖民地预设范围”并命名，保存后再导出；'
                         '旧存档没有这些数据，不会生成空文件。')
    print(f'共导出 {len(exported)} 个房间预设到 {directory}')
    return exported


def export_layout(root: ET.Element, directory: Path, save_stem: str, map_id=None, origin: int = 1) -> Path:
    """导出一键放置记录为成品布局：只写房间之间的相对位置，房间内部数据留在存档里。"""
    records = []
    for node in root.findall('./game/components/li/aicoopPresetPlacements/li'):
        try:
            record = {key: int(node.findtext(key, '0')) for key in ('mapId', 'x', 'z', 'rotation', 'room', 'tick')}
        except (TypeError, ValueError):
            raise ValueError('放置记录里有非整数坐标，无法导出。')
        record['file'] = legal_name(node.findtext('file'))
        if record['rotation'] not in (0, 1, 2, 3):
            raise ValueError('放置记录的 rotation 不在 0-3 之间，无法导出。')
        records.append(record)
    if not records:
        raise ValueError('存档里没有“一键预设”放置记录。更新前的历史放置无法推断：'
                         '请用新版 Mod 放置房间并保存后再导出，不会生成空文件。')
    maps = sorted({record['mapId'] for record in records})
    if map_id is None:
        if len(maps) > 1:
            raise ValueError('放置记录来自多个地图，请用 --map 指定一个地图 ID：' + '、'.join(map(str, maps)))
        map_id = maps[0]
    picked = [record for record in records if record['mapId'] == map_id]
    if not picked:
        raise ValueError(f'地图 {map_id} 没有放置记录；有记录的地图：' + '、'.join(map(str, maps)))
    if not 1 <= origin <= len(picked):
        raise ValueError(f'--origin 只能是 1 到 {len(picked)} 之间的序号。')
    anchor = picked[origin - 1]
    lines = ['LAYOUT version=1 units=cells']
    for record in picked:
        lines.append(f"（{record['x'] - anchor['x']},{record['z'] - anchor['z']}）{record['file']} rotation={record['rotation']}")
    path = write_new(directory, derive_name(save_stem, '成品'), '\n'.join(lines))
    for index, record in enumerate(picked, 1):
        print(f"{index}. {record['file']} 地图={record['mapId']} 坐标=({record['x']},{record['z']}) "
              f"旋转={record['rotation']} 房间序号={record['room']}{'（原点）' if index == origin else ''}")
    print(f'共 {len(picked)} 条记录；已导出：{path}')
    return path


def choose_save() -> str:
    """弹出 Tk 选择窗口；失败时改为命令行输入路径。"""
    try:
        import tkinter
        from tkinter import filedialog
        window = tkinter.Tk()
        window.withdraw()
        start = Path.home() / 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Saves'
        picked = filedialog.askopenfilename(title='选择要读取的环世界存档（只读，不会修改）',
                                            initialdir=str(start), filetypes=[('环世界存档', '*.rws')])
        window.destroy()
        return picked
    except Exception:
        return input('没能打开文件选择窗口，请粘贴 .rws 存档的完整路径：').strip().strip('"')


def parse_save(save) -> tuple:
    """解析存档，返回 (存档路径, 根节点)；只读取，不修改。"""
    save_path = Path(save).expanduser().resolve()
    if not save_path.is_file():
        raise ValueError('找不到存档文件：' + str(save_path))
    return save_path, ET.parse(save_path).getroot()


def layout_map_ids(root: ET.Element) -> list:
    """列出一键放置记录涉及的地图 ID，供图形界面在多地图时弹窗选择。"""
    ids = [int(node.findtext('mapId', '0') or 0)
           for node in root.findall('./game/components/li/aicoopPresetPlacements/li')]
    if not ids:
        return []
    return [ids[0]] + [value for value in sorted(set(ids)) if value != ids[0]]


def launch_gui() -> int:
    """无参数启动的中文图形界面：选存档 → 导出房间预设/成品布局；日志与错误都显示在窗口里。"""
    import tkinter
    from tkinter import filedialog, messagebox, scrolledtext

    class ExportApp:
        def __init__(self, master):
            self.master = master
            self.save = None
            master.title('环世界 AI 队友：预设导出')
            master.geometry('680x440')
            master.minsize(560, 360)
            self.label = tkinter.Label(master, text='存档：未选择', anchor='w')
            self.label.grid(row=0, column=0, columnspan=4, sticky='we', padx=10, pady=(10, 6))
            self.pick_button = tkinter.Button(master, text='选择存档（.rws）', command=self.pick_save)
            self.pick_button.grid(row=1, column=0, columnspan=2, sticky='we', padx=10)
            self.rooms_button = tkinter.Button(master, text='导出房间预设', command=lambda: self.export('rooms'))
            self.rooms_button.grid(row=1, column=2, sticky='we', padx=4)
            self.layout_button = tkinter.Button(master, text='导出成品布局', command=lambda: self.export('layout'))
            self.layout_button.grid(row=1, column=3, sticky='we', padx=10)
            for column in range(4):
                master.grid_columnconfigure(column, weight=1)
            tkinter.Label(master, text='日志（只读；进度、警告和错误都显示在这里）：', anchor='w').grid(
                row=2, column=0, columnspan=4, sticky='we', padx=10, pady=(10, 2))
            self.log = scrolledtext.ScrolledText(master, height=16, wrap='word', state='disabled')
            self.log.grid(row=3, column=0, columnspan=4, sticky='nsew', padx=10, pady=(0, 10))
            master.grid_rowconfigure(3, weight=1)
            self.write('只读取存档；同名输出不会覆盖，会自动加序号。请先选择 .rws 再点导出。')

        def write(self, text: str):
            self.log.configure(state='normal')
            for line in str(text).splitlines():
                if line.strip():
                    self.log.insert('end', line + '\n')
            self.log.see('end')
            self.log.configure(state='disabled')
            self.master.update_idletasks()

        def busy(self, active: bool):
            state = 'disabled' if active else 'normal'
            for button in (self.pick_button, self.rooms_button, self.layout_button):
                button.configure(state=state)
            self.master.configure(cursor='watch' if active else '')
            self.master.update_idletasks()

        def pick_save(self):
            start = Path.home() / 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Saves'
            picked = filedialog.askopenfilename(title='选择要读取的环世界存档（只读，不会修改）',
                                                initialdir=str(start), filetypes=[('环世界存档', '*.rws')])
            if picked:
                self.save = picked
                self.label.configure(text='存档：' + picked)
                self.write('已选择存档：' + picked)

        def export(self, mode: str):
            if not self.save:
                messagebox.showwarning('还没有选择存档', '请先点“选择存档（.rws）”选中一个 .rws 文件。')
                return
            self.busy(True)
            try:
                self.run(mode, self.save)
            except Exception as error:  # 任何异常都写进日志区，窗口不关闭、不闪退
                self.write('导出失败：' + str(error))
            finally:
                self.busy(False)

        def run(self, mode: str, save: str):
            save_path, root = parse_save(save)
            map_id = None
            if mode == 'layout':
                maps = layout_map_ids(root)
                if len(maps) > 1:
                    map_id = self.ask_map(maps)
                    if map_id is None:
                        self.write('已取消：没有选择地图，未写入任何文件。')
                        return
                    self.write('使用地图 ID：' + str(map_id))
            directory = MOD_DIR / ('Presets' if mode == 'rooms' else '成品')
            self.write(('导出房间预设 → ' if mode == 'rooms' else '导出成品布局 → ') + str(directory))
            captured, original = io.StringIO(), sys.stdout
            sys.stdout = captured
            try:
                if mode == 'rooms':
                    export_rooms(root, directory, map_id)
                else:
                    export_layout(root, directory, save_path.stem, map_id)
            finally:
                sys.stdout = original
                self.write(captured.getvalue())
            self.write('导出完成。')

        def ask_map(self, maps: list):
            """多地图时弹窗选择地图 ID，默认第一条记录所在地图；关窗视为取消。"""
            prompt = tkinter.Toplevel(self.master)
            prompt.title('选择地图 ID')
            prompt.transient(self.master)
            prompt.grab_set()
            tkinter.Label(prompt, text='放置记录来自多个地图，请选择要导出的地图 ID：').grid(
                row=0, column=0, columnspan=2, sticky='w', padx=12, pady=(12, 6))
            choice = tkinter.IntVar(value=maps[0])
            for index, value in enumerate(maps):
                tkinter.Radiobutton(prompt, text='地图 ID ' + str(value), variable=choice, value=value).grid(
                    row=1 + index, column=0, columnspan=2, sticky='w', padx=18)
            picked = []
            tkinter.Button(prompt, text='确定', command=lambda: (picked.append(choice.get()), prompt.destroy())).grid(
                row=1, column=2, padx=12, pady=12)
            tkinter.Button(prompt, text='取消', command=prompt.destroy).grid(row=2, column=2, padx=12)
            self.master.wait_window(prompt)
            return picked[0] if picked else None

    window = tkinter.Tk()
    ExportApp(window)
    window.mainloop()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description='从环世界存档离线导出房间预设或成品布局；只读存档，不修改、不覆盖。')
    parser.add_argument('--save', help='存档 .rws 的完整路径；不填则弹出文件选择窗口')
    parser.add_argument('--mode', choices=['rooms', 'layout'], help='rooms=命名范围导出房间预设；layout=放置记录导出成品布局')
    parser.add_argument('--out', type=Path, help='输出目录；默认分别是 Mod/Presets 与 Mod/成品')
    parser.add_argument('--map', dest='map_id', type=int, help='只导出该地图 ID（uniqueID）的记录')
    parser.add_argument('--origin', type=int, default=1, help='成品布局的原点记录序号，从 1 开始；默认第 1 条')
    args = parser.parse_args()
    if len(sys.argv) == 1:  # 无参数（双击 exe）进图形界面；带参数仍走下面的命令行流程
        try:
            return launch_gui()
        except Exception as error:
            print('无法打开图形界面：' + str(error), file=sys.stderr)
            print('可改用命令行参数导出，例如：预设导出.exe --mode rooms --save "存档路径.rws"', file=sys.stderr)
            return 1
    try:
        save = args.save or choose_save()
        if not save:
            print('没有选择存档，未写入任何文件。')
            return 0
        save_path, root = parse_save(save)
        mode = args.mode
        if mode is None:
            choice = input('请选择导出类型：\n  1 = 房间预设（命名范围 → Presets）\n'
                           '  2 = 成品布局（一键放置记录 → 成品）\n输入 1 或 2：').strip()
            mode = MENU_MODES.get(choice)
            if mode is None:
                raise ValueError('请输入 1 或 2。')
        if mode == 'rooms':
            export_rooms(root, args.out or MOD_DIR / 'Presets', args.map_id)
        else:
            export_layout(root, args.out or MOD_DIR / '成品', save_path.stem, args.map_id, args.origin)
        return 0
    except (ValueError, OSError, EOFError, ET.ParseError) as error:
        print('导出失败：' + str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
