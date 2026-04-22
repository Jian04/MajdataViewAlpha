"""
simai_parser.py
将 maidata.txt 的指定难度谱面转换为 MajdataView 所需的 Majson JSON 格式。
MajdataView HTTP 接口：POST http://localhost:8013/
请求体：EditRequestjson（见 HttpHandler.cs）
"""

import re
import json
import struct
import zlib
import time
import datetime
import threading
import http.client
from pathlib import Path

# ---------------------------------------------------------------------------
# Majson 难度名
# ---------------------------------------------------------------------------
DIFF_NAMES = {
    '1': 'Easy', '2': 'Basic', '3': 'Advanced',
    '4': 'Expert', '5': 'Master', '6': 'Re:Master',
}

# ---------------------------------------------------------------------------
# 黑色背景 PNG（1×1 纯黑）
# ---------------------------------------------------------------------------

def _black_png_bytes() -> bytes:
    """返回最小有效 1×1 纯黑 PNG 字节"""
    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', crc)

    sig  = b'\x89PNG\r\n\x1a\n'
    ihdr = chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0))
    # 过滤字节(0=None) + RGB(0,0,0)
    raw  = b'\x00\x00\x00\x00'
    idat = chunk(b'IDAT', zlib.compress(raw))
    iend = chunk(b'IEND', b'')
    return sig + ihdr + idat + iend


# ---------------------------------------------------------------------------
# 单个 note 解析
# ---------------------------------------------------------------------------

def _parse_hold_time(bracket_str: str, current_bpm: float) -> float:
    """把 [n:k] 转换为秒数：k * 240 / (n * bpm)"""
    m = re.search(r'\[(\d+):(\d+)\]', bracket_str)
    if m and current_bpm > 0:
        return int(m.group(2)) * 240.0 / (int(m.group(1)) * current_bpm)
    return 0.0


def _make_note():
    return {
        "holdTime": 0.0, "isBreak": False, "isEx": False,
        "isFakeRotate": False, "isForceStar": False, "isHanabi": False,
        "isSlideBreak": False, "isSlideNoHead": False,
        "noteContent": "", "noteType": 0,
        "slideStartTime": 0.0, "slideTime": 0.0,
        "startPosition": 1, "touchArea": " ",
    }


def _parse_single_note(part: str, current_bpm: float) -> dict | None:
    """解析单个 note 字符串（不含 /）"""
    p = part.strip()
    if not p:
        return None

    note = _make_note()

    # 触摸 note: A1 B3 C D5 E2 等（首字母为 A-E）
    if p[0].upper() in 'ABCDE' and not p[0].isdigit():
        area = p[0].upper()
        rest = p[1:]
        pm = re.match(r'(\d+)', rest)
        pos = int(pm.group(1)) if pm else 1
        rest = rest[len(pm.group(1)):] if pm else rest
        note["touchArea"] = area
        note["startPosition"] = pos
        # 烟花修饰符 'f' 可以出现在 h 前后任意位置：Cf / Chf[2:1] / Ch[2:1]f
        if 'f' in rest:
            note["isHanabi"] = True
        # h 后可接 f 再接 [n:k]，或直接 [n:k]：用 hf?\[n:k\] 匹配两种写法
        hm = re.search(r'h\s*f?\s*\[(\d+):(\d+)\]', rest, re.IGNORECASE)
        if hm and current_bpm > 0:
            note["noteType"] = 4  # TouchHold
            note["holdTime"] = int(hm.group(2)) * 240.0 / (int(hm.group(1)) * current_bpm)
        elif 'h' in rest.lower():
            note["noteType"] = 4  # TouchHold（无明确时值）
        else:
            note["noteType"] = 3  # Touch
        return note

    # 普通 note：必须以 1-8 开头
    if not (p[0].isdigit() and 1 <= int(p[0]) <= 8):
        return None

    note["startPosition"] = int(p[0])
    rest = p[1:]

    # 修饰符 b x f !（在 note 类型字符之前）
    while rest and rest[0] in 'bxf!':
        c = rest[0]
        if c == 'b':
            note["isBreak"] = True
        elif c == 'x':
            note["isEx"] = True
        elif c == 'f':
            note["isFakeRotate"] = True
        elif c == '!':
            note["isHanabi"] = True
        rest = rest[1:]

    if not rest:
        return note  # 普通 tap

    # Hold: h[n:k]   (C# enum: Hold = 2)
    if rest[0] == 'h':
        note["noteType"] = 2  # Hold
        note["holdTime"] = _parse_hold_time(rest, current_bpm)
        return note

    # Slide / Star：-  >  <  v  q  p  s  z  V  w  pp  qq  等  (C# enum: Slide = 1)
    slide_chars = set('-><vqpszVwW')
    if rest[0] in slide_chars or (len(rest) >= 2 and rest[:2] in ('pp', 'qq')):
        note["noteType"] = 1   # Slide
        # noteContent 必须是「起始位 + slide类型字符 + 终点...」，不含 bxf! 修饰符
        # 'b' must NOT appear in noteContent — the C# slide parser treats it as
        # a slide-type character and throws "组合星星有错误 / SLIDE CHAIN ERROR".
        # isSlideBreak already captures the break flag separately.
        note["noteContent"] = (str(note["startPosition"]) + rest).replace('b', '')
        note["isForceStar"] = True
        # 'b' 出现在 rest 中任意位置均为 slide break（'b' 不是合法的 slide 路径字符）
        # 覆盖两种写法：1-5b[8:1]（b 在 [ 前）和 1-5[8:1]b（b 在 ] 后）
        if 'b' in rest:
            note["isSlideBreak"] = True
        # 解析 [n:k] slide 时值 → slideTime（秒）
        # slideStartTime 将在 _chart_to_timing_list 中根据绝对时间填充
        dur_m = re.search(r'\[(\d+):(\d+)\]', rest)
        if dur_m and current_bpm > 0:
            n, k = int(dur_m.group(1)), int(dur_m.group(2))
            note["slideTime"] = k * 240.0 / (n * current_bpm)
        else:
            # 默认 1 拍
            note["slideTime"] = 60.0 / current_bpm if current_bpm > 0 else 1.0
        return note

    return note  # 仍按 tap 返回


def _parse_star_slides(part: str, current_bpm: float) -> list[dict]:
    """
    解析同头双压（或多压）：1-4[8:1]*-5[8:1]
    '*' 分隔从同一起始位同时触发的多条 slide 路径。
    第二条及以后的 slide 设 isSlideNoHead=True（共用星星头）。
    """
    # 提取起始位和修饰符（b x f !），只属于第一段
    start_char = part[0]
    rest = part[1:]
    mods = ''
    while rest and rest[0] in 'bxf!':
        mods += rest[0]
        rest = rest[1:]

    segments = rest.split('*')
    notes = []
    for i, seg in enumerate(segments):
        if not seg:
            continue
        # 只在第一段保留修饰符
        full = start_char + (mods if i == 0 else '') + seg
        n = _parse_single_note(full, current_bpm)
        if n:
            if i > 0:
                n['isSlideNoHead'] = True  # 共用星星头，不重复渲染
            notes.append(n)
    return notes


def _parse_token_notes(token_body: str, current_bpm: float) -> list[dict]:
    """解析 token 正文（已去掉 {n} 和 (bpm)）"""
    notes = []
    for part in token_body.split('/'):
        # 同头双压：1-4[8:1]*-5[8:1]（'*' 只在 slide note 中作路径分隔符）
        if '*' in part and part and part[0].isdigit():
            notes.extend(_parse_star_slides(part, current_bpm))
            continue
        n = _parse_single_note(part, current_bpm)
        if n:
            notes.append(n)
    return notes


# ---------------------------------------------------------------------------
# 谱面字符串 → timingList
# ---------------------------------------------------------------------------

def _parse_color(s: str) -> str | None:
    """Normalise a color value to an uppercase hex string: RRGGBB or RRGGBBAA.

    Accepted formats:
      FF8800              6-digit hex (no alpha)
      FF880080            8-digit hex (last 2 bytes = opacity 0x00–0xFF)
      #FF8800             with leading '#'
      rgb(255, 136, 0)    CSS rgb()  → no alpha channel
      rgba(255,136,0,0.5) CSS rgba() → opacity 0.0–1.0 as last 2 hex bytes
    """
    s = s.strip()
    m = re.match(
        r'rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)$',
        s, re.IGNORECASE)
    if m:
        r = max(0, min(255, int(m.group(1))))
        g = max(0, min(255, int(m.group(2))))
        b = max(0, min(255, int(m.group(3))))
        hex6 = f'{r:02X}{g:02X}{b:02X}'
        if m.group(4) is not None:
            a = max(0.0, min(1.0, float(m.group(4))))
            return hex6 + f'{int(round(a * 255)):02X}'
        return hex6
    # Plain hex: optionally prefixed with '#'
    s = s.lstrip('#')
    if len(s) in (6, 8) and all(c in '0123456789ABCDEFabcdef' for c in s):
        return s.upper()
    return None


# All recognised note-type keys for <NC*...>
_ALL_NOTE_TYPES = ('tap', 'each', 'hold', 'slide', 'star', 'break', 'touch', 'touchhold')


def _chart_to_timing_list(chart_str: str, default_bpm: float) -> tuple[list[dict], list[dict], list[dict], list[dict]]:
    """把 simai 谱面字符串转换为 (timingList, svTable, colorTable, sizeTable)。

    ALPHA: 支持的命令（统一 <CMD*value> 风格，与 SV/HS 一致）：
      <HS*X>          落下速度倍率（仅影响之后生成的音符）
      <SV*X>          全局卷轴速度变化点
      <NC*color>      颜色缺省：所有类型统一设为 color（之后的音符）
      <NC*k=c,...>    颜色逐类型：tap/each/hold/slide/star/break/touch/touchhold
                      color 格式: RRGGBB | RRGGBBAA | rgb(r,g,b) | rgba(r,g,b,a)
                      例: <NC*FF8800>
                          <NC*tap=FF0000,break=rgba(0,255,238,0.8)>
      <NS*x>          音符大小倍率（之后的音符，1.0=原始）
                      例: <NS*1.5>
    """
    clean = re.sub(r'\s+', '', chart_str)
    # Split on commas that are NOT inside <...> brackets (protects <NC a=x,b=y>)
    tokens = re.split(r',(?![^<>]*>)', clean)

    current_bpm = default_bpm
    current_div = 4
    current_time = 0.0  # 秒
    current_hs: float = 1.0  # per-note HSpeed

    timing_list: list[dict] = []
    sv_table:    list[dict] = []   # ALPHA: true SV
    color_table: list[dict] = []   # ALPHA: note color — list of {time, noteType, color}
    size_table:  list[dict] = []   # ALPHA: note size  — list of {time, scale}

    for token in tokens:
        if token == 'E':
            break

        remaining = token

        # 提取 BPM 变化 (180)
        bpm_hits = re.findall(r'\((\d+(?:\.\d+)?)\)', remaining)
        if bpm_hits:
            current_bpm = float(bpm_hits[-1])
        remaining = re.sub(r'\(\d+(?:\.\d+)?\)', '', remaining)

        # 提取细分变化 {n}
        while True:
            m = re.match(r'\{(\d+)\}', remaining)
            if m:
                current_div = int(m.group(1))
                remaining = remaining[m.end():]
            else:
                break

        # 提取速度控制命令
        while True:
            # <SV*X> — 在当前时刻记录一个全局 SV 变化点
            sv_m = re.search(r'<SV\*([\d.]+)>', remaining, re.IGNORECASE)
            if sv_m:
                sv_table.append({"time": current_time, "multiplier": float(sv_m.group(1))})
                remaining = remaining[:sv_m.start()] + remaining[sv_m.end():]
                continue
            # <HS*X> — 仅修改之后音符的落下速度
            hs_m = re.search(r'<HS\*([\d.]+)>', remaining, re.IGNORECASE)
            if hs_m:
                current_hs = float(hs_m.group(1))
                remaining = remaining[:hs_m.start()] + remaining[hs_m.end():]
                continue
            # <NC*color>  or  <NC*tap=c,hold=c,...>
            nc_m = re.search(r'<NC\*([^>]+)>', remaining, re.IGNORECASE)
            if nc_m:
                content = nc_m.group(1).strip()
                if '=' not in content:
                    # Shorthand: all note types get the same color
                    color = _parse_color(content)
                    if color:
                        for nt in _ALL_NOTE_TYPES:
                            color_table.append({"time": current_time, "noteType": nt, "color": color})
                else:
                    # Per-type: split on commas not inside ()
                    for pair in re.split(r',(?![^()]*\))', content):
                        kv = pair.strip().split('=', 1)
                        if len(kv) == 2:
                            nt = kv[0].strip().lower()
                            color = _parse_color(kv[1])
                            if color:
                                color_table.append({"time": current_time, "noteType": nt, "color": color})
                remaining = remaining[:nc_m.start()] + remaining[nc_m.end():]
                continue
            # <NS*x> — 音符大小倍率
            ns_m = re.search(r'<NS\*([\d.]+)>', remaining, re.IGNORECASE)
            if ns_m:
                try:
                    size_table.append({"time": current_time, "scale": float(ns_m.group(1))})
                except ValueError:
                    pass
                remaining = remaining[:ns_m.start()] + remaining[ns_m.end():]
                continue
            break

        # token 时长
        dur = 240.0 / (current_div * current_bpm) if current_bpm > 0 else 0.0

        if remaining:
            notes = _parse_token_notes(remaining, current_bpm)
            if notes:
                beat = 60.0 / current_bpm if current_bpm > 0 else 0.0
                for note in notes:
                    if note["noteType"] == 1:  # Slide
                        note["slideStartTime"] = current_time + beat

                timing_list.append({
                    "currentBpm": current_bpm,
                    "time": current_time,
                    "noteContent": remaining,
                    "noteList": notes,
                    "havePlayed": False,
                    "HSpeed": current_hs,
                    "rawTextPositionX": 0,
                    "rawTextPositionY": 0,
                })

        current_time += dur

    return timing_list, sv_table, color_table, size_table


# ---------------------------------------------------------------------------
# 公开接口：maidata → Majson dict
# ---------------------------------------------------------------------------

def maidata_to_majson(content: str, diff_num: str) -> dict | None:
    """
    将 maidata.txt 文本中的指定难度转换为 Majson dict（可序列化为 JSON）。
    diff_num: '1'~'6' 对应 Easy~Re:Master
    """
    def _field(key):
        m = re.search(rf'&{key}=(.+?)(?=\n&|\Z)', content, re.DOTALL)
        return m.group(1).strip() if m else ''

    title = _field('title')
    artist = _field('artist')
    designer = _field(f'des_{diff_num}')
    level = _field(f'lv_{diff_num}')
    wholebpm_str = _field('wholebpm')
    default_bpm = float(wholebpm_str) if wholebpm_str else 120.0

    chart_m = re.search(
        rf'&inote_{diff_num}=(.*?)(?=\n&[a-z_]|\Z)', content, re.DOTALL)
    if not chart_m:
        return None
    chart_str = chart_m.group(1).strip()
    if not chart_str:
        return None

    timing_list, sv_table, color_table, size_table = _chart_to_timing_list(chart_str, default_bpm)
    if not timing_list:
        return None

    return {
        "title": title or 'Unknown',
        "artist": artist,
        "designer": designer,
        "difficulty": DIFF_NAMES.get(diff_num, 'Unknown'),
        "diffNum": int(diff_num) - 1,  # 0-indexed
        "level": level,
        "timingList": timing_list,
        "svTable":    sv_table,     # ALPHA: true SV
        "colorTable": color_table,  # ALPHA: note color
        "sizeTable":  size_table,   # ALPHA: note size
    }


# ---------------------------------------------------------------------------
# 保存临时 JSON + 黑色背景
# ---------------------------------------------------------------------------

def save_temp_majson(majson_dict: dict, out_dir: str | Path) -> str:
    """
    把 Majson dict 保存到临时文件，同时在同目录写入黑色 bg.png（若不存在）。
    返回文件绝对路径（Windows 反斜杠）。
    """
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / '__preview_chart.json'
    out_path.write_text(json.dumps(majson_dict, ensure_ascii=False, indent=2),
                         encoding='utf-8')
    # 写入黑色背景（MajdataView 在 JSON 同目录寻找 bg.png）
    bg_path = out_dir / 'bg.png'
    bg_path.write_bytes(_black_png_bytes())
    return str(out_path.resolve())


# ---------------------------------------------------------------------------
# C# DateTime.Ticks 换算（必须用本地时间！C# DateTime.Now 是本地时间）
# ---------------------------------------------------------------------------

_CS_EPOCH = datetime.datetime(1, 1, 1)   # C# DateTime 纪元


def local_cs_ticks(offset_seconds: float = 0.0) -> int:
    """
    返回「本地当前时间 + offset_seconds」对应的 C# DateTime.Ticks。
    必须用本地时间（不能用 UTC），因为 C# 用 DateTime.Now 做差计算延迟。
    """
    target = datetime.datetime.now() + datetime.timedelta(seconds=offset_seconds)
    delta = target - _CS_EPOCH
    return int(delta.total_seconds() * 10_000_000)


# ---------------------------------------------------------------------------
# 读取 EditorSetting.json（MajdataEdit/MajdataView 配置文件）
# ---------------------------------------------------------------------------

def load_editor_settings(editor_setting_path: str | Path) -> dict:
    """
    读取 EditorSetting.json，返回 dict。
    若文件不存在或解析失败则返回空 dict（调用方使用默认值）。
    """
    try:
        p = Path(editor_setting_path)
        if p.exists():
            return json.loads(p.read_text(encoding='utf-8'))
    except Exception:
        pass
    return {}


# ---------------------------------------------------------------------------
# 发送到 MajdataView
# ---------------------------------------------------------------------------

def send_to_majdata_view(
    json_path: str,
    start_delay: float = 5.0,
    start_time: float = 0.0,
    note_speed: float = 7.5,
    audio_speed: float = 1.0,
    background_cover: float = 0.6,
    combo_status: int = 1,
    smooth_slide: bool = False,
    host: str = 'localhost',
    port: int = 8013,
    timeout: float = 15.0,
) -> tuple[bool, str]:
    """
    向 MajdataView（localhost:8013）发送 Start 指令。
    返回 (success, message)。
    """
    start_at = local_cs_ticks(offset_seconds=start_delay)

    payload = {
        "control": 0,             # EditorControlMethod.Start
        "jsonPath": json_path,
        "noteSpeed": note_speed,
        "touchSpeed": note_speed,
        "audioSpeed": audio_speed,
        "startAt": start_at,
        "startTime": start_time,
        "backgroundCover": background_cover,
        "editorPlayMethod": 0,    # AutoPlayMode.Enable (AutoPlay)
        "comboStatusType": combo_status,
        "smoothSlideAnime": smooth_slide,
    }

    try:
        body = json.dumps(payload).encode('utf-8')
        conn = http.client.HTTPConnection(host, port, timeout=timeout)
        conn.request('POST', '/', body,
                     {'Content-Type': 'application/json; charset=utf-8'})
        resp = conn.getresponse()
        resp.read()
        conn.close()
        if resp.status == 200:
            return True, 'OK'
        return False, f'HTTP {resp.status}'
    except ConnectionRefusedError:
        return False, '无法连接到 MajdataView（未运行？）'
    except Exception as e:
        return False, str(e)


# ---------------------------------------------------------------------------
# 按键音 SFX 播放（MajdataView 本身不播放按键音，由 Python 侧驱动）
# ---------------------------------------------------------------------------

# noteType → SFX 文件名（正常音）
_SFX_NOTE_MAP = {
    0: 'answer.wav',   # Tap
    2: 'answer.wav',   # Hold（头拍触发）
    3: 'touch.wav',    # Touch
    4: 'touch.wav',    # TouchHold（头拍触发）
}

# SFX 文件名 → EditorSetting.json 音量键
_SFX_VOLUME_KEY: dict[str, str] = {
    'answer.wav':            'Default_Answer_Level',
    'break_tap.wav':         'Default_Break_Level',
    'break.wav':             'Default_Break_Level',
    'break_slide.wav':       'Default_Break_Slide_Level',
    'break_slide_start.wav': 'Default_Break_Slide_Level',
    'slide.wav':             'Default_Slide_Level',
    'touch.wav':             'Default_Touch_Level',
    'touch_hanabi.wav':      'Default_Hanabi_Level',
    'touch_Hold_riser.wav':  'Default_Touch_Level',
    'touchHold_riser.wav':   'Default_Touch_Level',
    'hanabi.wav':            'Default_Hanabi_Level',
    'tap_ex.wav':            'Default_Ex_Level',
    'judge.wav':             'Default_Judge_Level',
    'judge_break.wav':       'Default_Judge_Level',
    'judge_break_slide.wav': 'Default_Judge_Level',
    'judge_ex.wav':          'Default_Judge_Level',
}

# 当前 SFX 播放任务的取消事件（全局，每次新播放时重置）
_sfx_cancel: threading.Event = threading.Event()

# ---------------------------------------------------------------------------
# BASS 音频库（复用 MajdataEdit 随附的 bass.dll，支持多路混音）
# winsound 同一时刻只能播一路，多音符同时触发时互相截断；
# BASS 支持真正的混音，与 MajdataEdit 的声音逻辑一致。
# ---------------------------------------------------------------------------
import ctypes as _ctypes

_BASS_LIB: '_ctypes.WinDLL | None' = None
_BASS_READY: bool = False
_BASS_ATTRIB_VOL   = 2
_BASS_SAMPLE_OVER_VOL = 0x10000   # 满通道时替换音量最小的实例


def _bass_init(dll_path: 'str | Path') -> bool:
    global _BASS_LIB, _BASS_READY
    if _BASS_READY:
        return True
    try:
        lib = _ctypes.WinDLL(str(dll_path))
        lib.BASS_Init.restype  = _ctypes.c_int
        lib.BASS_Init.argtypes = [_ctypes.c_int, _ctypes.c_uint32, _ctypes.c_uint32,
                                   _ctypes.c_void_p, _ctypes.c_void_p]
        lib.BASS_SampleLoad.restype  = _ctypes.c_uint32
        lib.BASS_SampleLoad.argtypes = [_ctypes.c_int, _ctypes.c_char_p,
                                         _ctypes.c_uint64, _ctypes.c_uint32,
                                         _ctypes.c_uint32, _ctypes.c_uint32]
        lib.BASS_SampleGetChannel.restype  = _ctypes.c_uint32
        lib.BASS_SampleGetChannel.argtypes = [_ctypes.c_uint32, _ctypes.c_uint32]
        lib.BASS_ChannelSetAttribute.restype  = _ctypes.c_int
        lib.BASS_ChannelSetAttribute.argtypes = [_ctypes.c_uint32, _ctypes.c_uint32,
                                                  _ctypes.c_float]
        lib.BASS_ChannelPlay.restype  = _ctypes.c_int
        lib.BASS_ChannelPlay.argtypes = [_ctypes.c_uint32, _ctypes.c_int]
        if lib.BASS_Init(-1, 44100, 0, None, None):
            _BASS_LIB = lib
            _BASS_READY = True
            return True
    except Exception:
        pass
    return False


def _bass_load_sample(path: str) -> int:
    if not _BASS_LIB:
        return 0
    try:
        return _BASS_LIB.BASS_SampleLoad(0, path.encode('utf-8'),
                                          0, 0, 65, _BASS_SAMPLE_OVER_VOL)
    except Exception:
        return 0


def _bass_play(handle: int, volume: float) -> None:
    if not _BASS_LIB or not handle:
        return
    try:
        ch = _BASS_LIB.BASS_SampleGetChannel(handle, 0)
        if ch:
            _BASS_LIB.BASS_ChannelSetAttribute(ch, _BASS_ATTRIB_VOL,
                                                _ctypes.c_float(volume))
            _BASS_LIB.BASS_ChannelPlay(ch, 0)
    except Exception:
        pass


# ---------------------------------------------------------------------------
# Winsound fallback（bass.dll 不可用时）
# ---------------------------------------------------------------------------
try:
    import winsound as _winsound
except ImportError:
    _winsound = None


def _play_wav_winsound(path: str) -> None:
    if _winsound is None:
        return
    try:
        _winsound.PlaySound(path, _winsound.SND_FILENAME | _winsound.SND_NODEFAULT)
    except Exception:
        pass


# ---------------------------------------------------------------------------
# SFX worker（BASS 多路混音版）
# ---------------------------------------------------------------------------
def _sfx_worker(majson: dict,
                samples: 'dict[str, tuple[int, float]]',
                t_start: float,
                cancel: threading.Event) -> None:
    """samples = {sfx_filename: (bass_handle, volume)}"""
    def play(name: str) -> None:
        entry = samples.get(name)
        if entry:
            _bass_play(entry[0], entry[1])

    for timing in majson.get('timingList', []):
        if cancel.is_set():
            return
        note_time = float(timing.get('time', 0))
        if note_time < 0:
            continue
        target  = t_start + note_time
        sleep_s = target - time.monotonic()
        if sleep_s > 0:
            time.sleep(sleep_s)
        elif sleep_s < -0.15:
            continue
        if cancel.is_set():
            return

        has_slide = has_break_slide = False
        for note in timing.get('noteList', []):
            nt        = note.get('noteType', 0)
            is_break  = note.get('isBreak', False)
            is_ex     = note.get('isEx', False)
            is_hanabi = note.get('isHanabi', False)
            if nt == 1:
                if note.get('isSlideBreak', False):
                    has_break_slide = True
                else:
                    has_slide = True
                continue
            if is_ex:
                play('tap_ex.wav')
            elif is_break:
                play('break_tap.wav')
            elif is_hanabi and nt in (3, 4):
                play('touch_hanabi.wav')
            else:
                play(_SFX_NOTE_MAP.get(nt, 'answer.wav'))

        if has_slide:
            play('slide.wav')
        if has_break_slide:
            play('break_slide.wav')


# ---------------------------------------------------------------------------
# SFX worker（winsound fallback 版，单路，仅备用）
# ---------------------------------------------------------------------------
def _sfx_worker_winsound(majson: dict, sfx_dir: Path,
                          t_start: float, cancel: threading.Event) -> None:
    if _winsound is None:
        return
    try:
        _winsound.PlaySound(None, _winsound.SND_PURGE)
    except Exception:
        pass

    def sfx(name: str) -> 'str | None':
        p = sfx_dir / name
        return str(p) if p.exists() else None

    for timing in majson.get('timingList', []):
        if cancel.is_set():
            return
        note_time = float(timing.get('time', 0))
        if note_time < 0:
            continue
        target  = t_start + note_time
        sleep_s = target - time.monotonic()
        if sleep_s > 0:
            time.sleep(sleep_s)
        elif sleep_s < -0.15:
            continue
        if cancel.is_set():
            return

        has_slide = False
        for note in timing.get('noteList', []):
            nt = note.get('noteType', 0)
            if nt == 1:
                has_slide = True
                continue
            fname = sfx('break_tap.wav' if note.get('isBreak') else
                        _SFX_NOTE_MAP.get(nt, 'answer.wav'))
            if fname:
                threading.Thread(target=_play_wav_winsound, args=(fname,),
                                 daemon=True).start()
        if has_slide:
            fname = sfx('slide.wav')
            if fname:
                threading.Thread(target=_play_wav_winsound, args=(fname,),
                                 daemon=True).start()


# ---------------------------------------------------------------------------
# 公开入口
# ---------------------------------------------------------------------------
def play_sfx_for_chart(majson: dict, sfx_dir: 'str | Path',
                       t_start: 'float | None' = None,
                       start_delay: float = 3.0,
                       editor_settings: 'dict | None' = None) -> None:
    """
    启动后台线程，随 MajdataView 播放同步触发按键音。
    优先使用与 MajdataEdit 同目录的 bass.dll（多路混音），
    不可用时降级为 winsound（单路，有互截问题）。
    sfx_dir 不存在时静默跳过，每次调用终止上一次任务。
    """
    global _sfx_cancel
    _sfx_cancel.set()
    _sfx_cancel = threading.Event()

    sfx_dir = Path(sfx_dir)
    if not sfx_dir.exists():
        return

    if t_start is None:
        t_start = time.monotonic() + start_delay

    # bass.dll 与 MajdataView.exe / MajdataEdit.exe 同目录
    bass_dll = sfx_dir.parent / 'bass.dll'
    use_bass = bass_dll.exists() and _bass_init(bass_dll)

    if use_bass:
        es = editor_settings or {}
        # 收集本次谱面需要的 SFX 并加载为 BASS sample（已加载的 handle 可复用）
        needed: set[str] = set()
        for timing in majson.get('timingList', []):
            for note in timing.get('noteList', []):
                nt = note.get('noteType', 0)
                if nt == 1:
                    needed.add('break_slide.wav' if note.get('isSlideBreak') else 'slide.wav')
                elif note.get('isEx'):
                    needed.add('tap_ex.wav')
                elif note.get('isBreak'):
                    needed.add('break_tap.wav')
                elif note.get('isHanabi') and nt in (3, 4):
                    needed.add('touch_hanabi.wav')
                else:
                    needed.add(_SFX_NOTE_MAP.get(nt, 'answer.wav'))

        samples: dict[str, tuple[int, float]] = {}
        for name in needed:
            p = sfx_dir / name
            if p.exists():
                h = _bass_load_sample(str(p))
                if h:
                    vol_key = _SFX_VOLUME_KEY.get(name, 'Default_Answer_Level')
                    samples[name] = (h, float(es.get(vol_key, 0.7)))

        threading.Thread(
            target=_sfx_worker,
            args=(majson, samples, t_start, _sfx_cancel),
            daemon=True,
        ).start()
    else:
        # fallback：winsound 单路模式
        threading.Thread(
            target=_sfx_worker_winsound,
            args=(majson, sfx_dir, t_start, _sfx_cancel),
            daemon=True,
        ).start()
