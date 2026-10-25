"""Disassemble 65816 code from a Kirby Super Star ROM.

Usage: python3 tools/dis65816.py ROM SNES_ADDR [COUNT] [--m8] [--x8]

SNES_ADDR is a CPU address such as 05:9A47 or C7:0000. M and X flags start
16-bit unless --m8/--x8 are given, and follow REP/SEP as they are decoded.
"""
import sys

MODES = {
    'imp': 0, 'acc': 0, 'imm8': 1, 'immM': -1, 'immX': -2, 'dp': 1, 'dpx': 1, 'dpy': 1,
    'idp': 1, 'idpx': 1, 'idpy': 1, 'ildp': 1, 'ildpy': 1, 'abs': 2, 'absx': 2, 'absy': 2,
    'iabs': 2, 'iabsx': 2, 'ilabs': 2, 'long': 3, 'longx': 3, 'rel': 1, 'rell': 2,
    'sr': 1, 'isry': 1, 'mv': 2, 'imm16': 2,
}

OPS = """
00 BRK imm8|01 ORA idpx|02 COP imm8|03 ORA sr|04 TSB dp|05 ORA dp|06 ASL dp|07 ORA ildp|08 PHP imp|09 ORA immM|0A ASL acc|0B PHD imp|0C TSB abs|0D ORA abs|0E ASL abs|0F ORA long
10 BPL rel|11 ORA idpy|12 ORA idp|13 ORA isry|14 TRB dp|15 ORA dpx|16 ASL dpx|17 ORA ildpy|18 CLC imp|19 ORA absy|1A INC acc|1B TCS imp|1C TRB abs|1D ORA absx|1E ASL absx|1F ORA longx
20 JSR abs|21 AND idpx|22 JSL long|23 AND sr|24 BIT dp|25 AND dp|26 ROL dp|27 AND ildp|28 PLP imp|29 AND immM|2A ROL acc|2B PLD imp|2C BIT abs|2D AND abs|2E ROL abs|2F AND long
30 BMI rel|31 AND idpy|32 AND idp|33 AND isry|34 BIT dpx|35 AND dpx|36 ROL dpx|37 AND ildpy|38 SEC imp|39 AND absy|3A DEC acc|3B TSC imp|3C BIT absx|3D AND absx|3E ROL absx|3F AND longx
40 RTI imp|41 EOR idpx|42 WDM imm8|43 EOR sr|44 MVP mv|45 EOR dp|46 LSR dp|47 EOR ildp|48 PHA imp|49 EOR immM|4A LSR acc|4B PHK imp|4C JMP abs|4D EOR abs|4E LSR abs|4F EOR long
50 BVC rel|51 EOR idpy|52 EOR idp|53 EOR isry|54 MVN mv|55 EOR dpx|56 LSR dpx|57 EOR ildpy|58 CLI imp|59 EOR absy|5A PHY imp|5B TCD imp|5C JML long|5D EOR absx|5E LSR absx|5F EOR longx
60 RTS imp|61 ADC idpx|62 PER rell|63 ADC sr|64 STZ dp|65 ADC dp|66 ROR dp|67 ADC ildp|68 PLA imp|69 ADC immM|6A ROR acc|6B RTL imp|6C JMP iabs|6D ADC abs|6E ROR abs|6F ADC long
70 BVS rel|71 ADC idpy|72 ADC idp|73 ADC isry|74 STZ dpx|75 ADC dpx|76 ROR dpx|77 ADC ildpy|78 SEI imp|79 ADC absy|7A PLY imp|7B TDC imp|7C JMP iabsx|7D ADC absx|7E ROR absx|7F ADC longx
80 BRA rel|81 STA idpx|82 BRL rell|83 STA sr|84 STY dp|85 STA dp|86 STX dp|87 STA ildp|88 DEY imp|89 BIT immM|8A TXA imp|8B PHB imp|8C STY abs|8D STA abs|8E STX abs|8F STA long
90 BCC rel|91 STA idpy|92 STA idp|93 STA isry|94 STY dpx|95 STA dpx|96 STX dpy|97 STA ildpy|98 TYA imp|99 STA absy|9A TXS imp|9B TXY imp|9C STZ abs|9D STA absx|9E STZ absx|9F STA longx
A0 LDY immX|A1 LDA idpx|A2 LDX immX|A3 LDA sr|A4 LDY dp|A5 LDA dp|A6 LDX dp|A7 LDA ildp|A8 TAY imp|A9 LDA immM|AA TAX imp|AB PLB imp|AC LDY abs|AD LDA abs|AE LDX abs|AF LDA long
B0 BCS rel|B1 LDA idpy|B2 LDA idp|B3 LDA isry|B4 LDY dpx|B5 LDA dpx|B6 LDX dpy|B7 LDA ildpy|B8 CLV imp|B9 LDA absy|BA TSX imp|BB TYX imp|BC LDY absx|BD LDA absx|BE LDX absy|BF LDA longx
C0 CPY immX|C1 CMP idpx|C2 REP imm8|C3 CMP sr|C4 CPY dp|C5 CMP dp|C6 DEC dp|C7 CMP ildp|C8 INY imp|C9 CMP immM|CA DEX imp|CB WAI imp|CC CPY abs|CD CMP abs|CE DEC abs|CF CMP long
D0 BNE rel|D1 CMP idpy|D2 CMP idp|D3 CMP isry|D4 PEI dp|D5 CMP dpx|D6 DEC dpx|D7 CMP ildpy|D8 CLD imp|D9 CMP absy|DA PHX imp|DB STP imp|DC JML ilabs|DD CMP absx|DE DEC absx|DF CMP longx
E0 CPX immX|E1 SBC idpx|E2 SEP imm8|E3 SBC sr|E4 CPX dp|E5 SBC dp|E6 INC dp|E7 SBC ildp|E8 INX imp|E9 SBC immM|EA NOP imp|EB XBA imp|EC CPX abs|ED SBC abs|EE INC abs|EF SBC long
F0 BEQ rel|F1 SBC idpy|F2 SBC idp|F3 SBC isry|F4 PEA imm16|F5 SBC dpx|F6 INC dpx|F7 SBC ildpy|F8 SED imp|F9 SBC absy|FA PLX imp|FB XCE imp|FC JSR iabsx|FD SBC absx|FE INC absx|FF SBC longx
"""
TABLE = {}
for item in OPS.replace('\n', '|').split('|'):
    if item.strip():
        code, mn, mode = item.split()
        TABLE[int(code, 16)] = (mn, mode)

FMT = {
    'imp': '', 'acc': 'A', 'imm8': '#${:02X}', 'immM': '#${}', 'immX': '#${}', 'imm16': '#${:04X}',
    'dp': '${:02X}', 'dpx': '${:02X},X', 'dpy': '${:02X},Y', 'idp': '(${:02X})', 'idpx': '(${:02X},X)',
    'idpy': '(${:02X}),Y', 'ildp': '[${:02X}]', 'ildpy': '[${:02X}],Y', 'abs': '${:04X}', 'absx': '${:04X},X',
    'absy': '${:04X},Y', 'iabs': '(${:04X})', 'iabsx': '(${:04X},X)', 'ilabs': '[${:04X}]', 'long': '${:06X}',
    'longx': '${:06X},X', 'sr': '${:02X},S', 'isry': '(${:02X},S),Y',
}


def to_file(addr):
    bank, off = addr >> 16, addr & 0xFFFF
    if bank >= 0xC0:
        return (bank - 0xC0) << 16 | off
    return ((bank & 0x3F) << 15) | (off & 0x7FFF)


def disassemble(rom, addr, count, m8=False, x8=False):
    """Yield (address, bytes, text) for COUNT instructions starting at ADDR."""
    for _ in range(count):
        o = to_file(addr)
        op = rom[o]
        mn, mode = TABLE[op]
        size = MODES[mode]
        if size == -1:
            size = 1 if m8 else 2
        elif size == -2:
            size = 1 if x8 else 2
        raw = rom[o:o + 1 + size]
        val = int.from_bytes(raw[1:], 'little') if size else 0
        if mode == 'rel':
            text = f'{mn} ${(addr & 0xFF0000) | ((addr + 2 + (val - 256 if val > 127 else val)) & 0xFFFF):06X}'
        elif mode == 'rell':
            text = f'{mn} ${(addr & 0xFF0000) | ((addr + 3 + (val - 65536 if val > 32767 else val)) & 0xFFFF):06X}'
        elif mode == 'mv':
            text = f'{mn} ${raw[2]:02X},${raw[1]:02X}'
        elif mode in ('immM', 'immX'):
            text = f'{mn} #$' + (f'{val:02X}' if size == 1 else f'{val:04X}')
        else:
            text = f'{mn} {FMT[mode].format(val)}'.rstrip()
        if mn == 'REP':
            m8 = m8 and not (val & 0x20)
            x8 = x8 and not (val & 0x10)
        elif mn == 'SEP':
            m8 = m8 or bool(val & 0x20)
            x8 = x8 or bool(val & 0x10)
        yield addr, raw, text
        addr = (addr & 0xFF0000) | ((addr + 1 + size) & 0xFFFF)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    rom = open(args[0], 'rb').read()
    addr = int(args[1].replace(':', ''), 16)
    count = int(args[2]) if len(args) > 2 else 40
    for a, raw, text in disassemble(rom, addr, count, '--m8' in sys.argv, '--x8' in sys.argv):
        print(f'{a >> 16:02X}:{a & 0xFFFF:04X}  {raw.hex(" ").upper():<12} {text}')


if __name__ == '__main__':
    main()
