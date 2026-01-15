using System;
using System.IO;

namespace OGNES.Components
{
    public class Cpu
    {
        // Registers
        public byte A;      // Accumulator
        public byte X;      // X Index
        public byte Y;      // Y Index
        public byte S;      // Stack Pointer
        public ushort PC;   // Program Counter
        public ushort CurrentInstructionPC; // PC at the start of the current instruction
        public byte P;      // Status Register

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(A);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(S);
            writer.Write(PC);
            writer.Write(P);
        }

        public void LoadState(BinaryReader reader)
        {
            A = reader.ReadByte();
            X = reader.ReadByte();
            Y = reader.ReadByte();
            S = reader.ReadByte();
            PC = reader.ReadUInt16();
            P = reader.ReadByte();
        }

        // Flags
        [Flags]
        public enum CpuFlags : byte
        {
            C = (1 << 0), // Carry
            Z = (1 << 1), // Zero
            I = (1 << 2), // Interrupt Disable
            D = (1 << 3), // Decimal Mode
            B = (1 << 4), // Break
            U = (1 << 5), // Unused
            V = (1 << 6), // Overflow
            N = (1 << 7), // Negative
        }

        private readonly Memory _bus;
        private bool _irqAfterOpcode;
        private bool? _overrideIrq;
        
        public long TotalCycles => _bus.TotalCycles;

        public Cpu(Memory bus)
        {
            _bus = bus;
        }

        public void Reset()
        {
            _pendingIrq = false;
            A = 0;
            X = 0;
            Y = 0;
            S = 0xFD;
            P = (byte)(CpuFlags.U | CpuFlags.I);
            
            // Reset sequence takes 7 cycles
            // In a real NES, it reads the reset vector
            _bus.Tick(); _bus.Tick(); _bus.Tick(); // 3 cycles
            _bus.Tick(); _bus.Tick(); // 2 cycles (stack operations usually)
            
            ushort lo = Read(0xFFFC);
            ushort hi = Read(0xFFFD);
            PC = (ushort)((hi << 8) | lo);
        }

        public void Nmi()
        {
            Read(PC); // Dummy read
            Read(PC); // Dummy read
            PushStack((byte)((PC >> 8) & 0xFF));
            PushStack((byte)(PC & 0xFF));
            PushStack((byte)(P & ~((byte)CpuFlags.B) | (byte)CpuFlags.U));
            SetFlag(CpuFlags.I, true);
            ushort lo = Read(0xFFFA);
            ushort hi = Read(0xFFFB);
            PC = (ushort)((hi << 8) | lo);
        }

        public void Irq()
        {
            // IRQ is level-triggered, but we only call this if the interrupt was polled and accepted
            // in the previous step (respecting the I flag latency).
            
            // Cycle 1: Dummy Read (Fetch next opcode, discarded)
            Read(PC); 
            // Cycle 2: Dummy Read (pc not incr)
            Read(PC); 

            // Cycle 3: Push PCH
            PushStack((byte)((PC >> 8) & 0xFF));
            // Cycle 4: Push PCL
            PushStack((byte)(PC & 0xFF));
            
            // Hijack Check (End of Cycle 4)
            ushort vectorAddr = 0xFFFE;
            if (_bus.Ppu != null && _bus.Ppu.TriggerNmi)
            {
                _bus.Ppu.TriggerNmi = false;
                vectorAddr = 0xFFFA;
            }

            // Cycle 5: Push P
            // If we hijacked, we push P based on the *original* interrupt type?
            // "Similarly, an NMI can hijack an IRQ ... save for whether the B bit is pushed as set"
            // If it was an IRQ, B is clear.
            PushStack((byte)(P & ~((byte)CpuFlags.B) | (byte)CpuFlags.U));
            
            SetFlag(CpuFlags.I, true);

            // Cycle 6: Read Vector Low
            ushort lo = Read(vectorAddr);
            // Cycle 7: Read Vector High
            ushort hi = Read((ushort)(vectorAddr + 1));
            PC = (ushort)((hi << 8) | lo);
        }

        private bool _pendingIrq = false;
        private bool _prevI = true;
        private int _stallCycles = 0;
        private ushort _lastReadAddress = 0;

        public void Stall(int cycles)
        {
            _stallCycles += cycles;
        }

        /// <summary>
        /// Executes a single instruction.
        /// </summary>
        public void Step()
        {
            if (_bus.Ppu != null && _bus.Ppu.TriggerNmi)
            {
                _bus.Ppu.TriggerNmi = false;
                _pendingIrq = false;
                Nmi();
                return;
            }

            // Interrupt polling for normal execution (happens after NMI check but before instruction)
            // But real hardware polls at the END of the previous instruction.
            // Our structure:
            // 1. Check NMI (Edge sensitive, high priority)
            // 2. Check pending IRQ (from prev instruction end)
            
            if (_pendingIrq)
            {
                // Note: We do NOT clear _pendingIrq here because it's level sensitive.
                // Instead, Irq() will check flags.
                // Actually, if we enter IRQ handler, we should clear _pendingIrq for this "event",
                // but if the line remains low, it will be detected again at the end of the helper.
                 _pendingIrq = false;

                Irq();
                return;
            }

            _prevI = GetFlag(CpuFlags.I);

            CurrentInstructionPC = PC;
            byte opcode = Read(PC++);

            _irqAfterOpcode = IsIrqAsserted();
            
            // Note: Branch instructions will overwrite this default behavior
            _overrideIrq = null;

            Execute(opcode);

            bool irqActive;
            if (_overrideIrq.HasValue)
            {
                irqActive = _overrideIrq.Value;
            }
            else
            {
                irqActive = IsIrqAsserted();
            }

            // CLI (0x58), SEI (0x78), PLP (0x28) affect the I flag.
            // The interrupt polling happens during the last cycle of the instruction (which is this cycle).
            // Since the flags are updated in the last cycle, the NEW value of the I flag is used.
            bool effectiveI = GetFlag(CpuFlags.I);

            _pendingIrq = irqActive && !effectiveI;
        }

        /// <summary>
        /// Reads a byte from memory and ticks the bus.
        /// </summary>
        public byte Read(ushort address)
        {
            _bus.Tick();
            _lastReadAddress = address;
            
            // During stall cycles (DMC DMA), perform dummy reads from the same address
            // This is required by the hardware - the CPU repeats the last read cycle
            while (_stallCycles > 0)
            {
                _stallCycles--;
                _bus.Tick();
                // Perform dummy read to update the data bus, as the real hardware does
                _bus.Read(address);
            }
            return _bus.Read(address);
        }

        public byte Peek(ushort address)
        {
            return _bus.Peek(address);
        }

        /// <summary>
        /// Writes a byte to memory and ticks the bus.
        /// </summary>
        public void Write(ushort address, byte data)
        {
            _bus.Tick();
            // DMA cannot halt on a write cycle, so we don't process stalls here.
            // Any pending stalls will be handled on the next read cycle.
            _bus.Write(address, data);
        }

        public string GetStateLog()
        {
            byte opcode = Peek(PC);
            string name = GetOpcodeName(opcode);
            int length = GetOpcodeLength(opcode);
            
            string bytes = $"{opcode:X2}";
            if (length > 1) bytes += $" {Peek((ushort)(PC + 1)):X2}";
            else bytes += "   ";
            if (length > 2) bytes += $" {Peek((ushort)(PC + 2)):X2}";
            else bytes += "   ";

            string disasm = $"{name} {GetDisasmOperand(opcode, PC)}".PadRight(32);
            
            int ppuScanline = _bus.Ppu?.Scanline ?? 0;
            int ppuCycle = _bus.Ppu?.Cycle ?? 0;

            return $"{PC:X4} {bytes} {disasm} A:{A:X2} X:{X:X2} Y:{Y:X2} P:{P:X2} SP:{S:X2} PPU:{ppuScanline,3}, {ppuCycle,3} CYC:{TotalCycles}";
        }

        public string Disassemble(ushort pc)
        {
            byte opcode = Peek(pc);
            string name = GetOpcodeName(opcode);
            string operand = GetDisasmOperand(opcode, pc);
            return $"{name} {operand}";
        }

        private string GetOpcodeName(byte opcode)
        {
            return opcode switch
            {
                0xA9 or 0xA5 or 0xB5 or 0xAD or 0xBD or 0xB9 or 0xA1 or 0xB1 => "LDA",
                0x85 or 0x95 or 0x8D or 0x9D or 0x99 or 0x81 or 0x91 => "STA",
                0xA2 or 0xA6 or 0xB6 or 0xAE or 0xBE => "LDX",
                0xA0 or 0xA4 or 0xB4 or 0xAC or 0xBC => "LDY",
                0x86 or 0x96 or 0x8E => "STX",
                0x84 or 0x94 or 0x8C => "STY",
                0x69 or 0x65 or 0x75 or 0x6D or 0x7D or 0x79 or 0x61 or 0x71 => "ADC",
                0xE9 or 0xE5 or 0xF5 or 0xED or 0xFD or 0xF9 or 0xE1 or 0xF1 => "SBC",
                0xC9 or 0xC5 or 0xD5 or 0xCD or 0xDD or 0xD9 or 0xC1 or 0xD1 => "CMP",
                0xE0 or 0xE4 or 0xEC => "CPX",
                0xC0 or 0xC4 or 0xCC => "CPY",
                0x29 or 0x25 or 0x35 or 0x2D or 0x3D or 0x39 or 0x21 or 0x31 => "AND",
                0x09 or 0x05 or 0x15 or 0x0D or 0x1D or 0x19 or 0x01 or 0x11 => "ORA",
                0x49 or 0x45 or 0x55 or 0x4D or 0x5D or 0x59 or 0x41 or 0x51 => "EOR",
                0x24 or 0x2C => "BIT",
                0x10 => "BPL", 0x30 => "BMI", 0x50 => "BVC", 0x70 => "BVS",
                0x90 => "BCC", 0xB0 => "BCS", 0xD0 => "BNE", 0xF0 => "BEQ",
                0x4C or 0x6C => "JMP", 0x20 => "JSR", 0x60 => "RTS", 0x40 => "RTI",
                0x48 => "PHA", 0x08 => "PHP", 0x68 => "PLA", 0x28 => "PLP",
                0xAA => "TAX", 0x8A => "TXA", 0xA8 => "TAY", 0x98 => "TYA",
                0xBA => "TSX", 0x9A => "TXS",
                0xE8 => "INX", 0xCA => "DEX", 0xC8 => "INY", 0x88 => "DEY",
                0x18 => "CLC", 0x38 => "SEC", 0x58 => "CLI", 0x78 => "SEI",
                0xB8 => "CLV", 0xD8 => "CLD", 0xF8 => "SED",
                0x0A or 0x06 or 0x16 or 0x0E or 0x1E => "ASL",
                0x4A or 0x46 or 0x56 or 0x4E or 0x5E => "LSR",
                0x2A or 0x26 or 0x36 or 0x2E or 0x3E => "ROL",
                0x6A or 0x66 or 0x76 or 0x6E or 0x7E => "ROR",
                0xE6 or 0xF6 or 0xEE or 0xFE => "INC",
                0xC6 or 0xD6 or 0xCE or 0xDE => "DEC",
                0x00 => "BRK",
                0xEA => "NOP",
                0x0B or 0x2B => "AAC",
                0x4B => "ASR",
                0x6B => "ARR",
                0xAB => "LAX",
                0xCB => "AXS",
                0xEB => "SBC",
                0x80 or 0x82 or 0x89 or 0xC2 or 0xE2 => "DOP",
                0x04 or 0x44 or 0x64 or 0x14 or 0x34 or 0x54 or 0x74 or 0xD4 or 0xF4 => "DOP",
                0x0C or 0x1C or 0x3C or 0x5C or 0x7C or 0xDC or 0xFC => "TOP",
                0x07 or 0x17 or 0x0F or 0x1F or 0x1B or 0x03 or 0x13 => "SLO",
                0x27 or 0x37 or 0x2F or 0x3F or 0x3B or 0x23 or 0x33 => "RLA",
                0x47 or 0x57 or 0x4F or 0x5F or 0x5B or 0x43 or 0x53 => "SRE",
                0x67 or 0x77 or 0x6F or 0x7F or 0x7B or 0x63 or 0x73 => "RRA",
                0x87 or 0x97 or 0x8F or 0x83 => "AAX",
                0xA7 or 0xB7 or 0xAF or 0xBF or 0xA3 or 0xB3 => "LAX",
                0xC7 or 0xD7 or 0xCF or 0xDF or 0xDB or 0xC3 or 0xD3 => "DCP",
                0xE7 or 0xF7 or 0xEF or 0xFF or 0xFB or 0xE3 or 0xF3 => "ISC",
                0x93 or 0x9F => "SHA",
                0x9B => "SHS",
                0x9C => "SYA",
                0x9E => "SXA",
                0x8B => "ANE",
                _ => "???"
            };
        }

        private int GetOpcodeLength(byte opcode)
        {
            return opcode switch
            {
                // Implied / Accumulator
                0x18 or 0x38 or 0x58 or 0x78 or 0xB8 or 0xD8 or 0xF8 or
                0xAA or 0x8A or 0xA8 or 0x98 or 0xBA or 0x9A or
                0xE8 or 0xCA or 0xC8 or 0x88 or
                0x48 or 0x08 or 0x68 or 0x28 or
                0x40 or 0x60 or 0x0A or 0x4A or 0x2A or 0x6A or
                0xEA or 0x00 => 1,

                // Immediate / Zero Page / Relative
                0xA9 or 0xA5 or 0xB5 or 0xA2 or 0xA6 or 0xB6 or 0xA0 or 0xA4 or 0xB4 or
                0x85 or 0x95 or 0x86 or 0x96 or 0x84 or 0x94 or
                0x69 or 0x65 or 0x75 or 0xE9 or 0xE5 or 0xF5 or
                0xC9 or 0xC5 or 0xD5 or 0xE0 or 0xE4 or 0xC0 or 0xC4 or
                0x29 or 0x25 or 0x35 or 0x09 or 0x05 or 0x15 or 0x49 or 0x45 or 0x55 or
                0x24 or 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0 or
                0x06 or 0x16 or 0x46 or 0x56 or 0x26 or 0x36 or 0x66 or 0x76 or
                0xE6 or 0xF6 or 0xC6 or 0xD6 or 0xA1 or 0xB1 or 0x81 or 0x91 or 0x61 or 0x71 or 0xE1 or 0xF1 or 0xC1 or 0xD1 or 0x21 or 0x31 or 0x01 or 0x11 or 0x41 or 0x51 or
                0x0B or 0x2B or 0x4B or 0x6B or 0xAB or 0xCB or 0xEB or
                0x80 or 0x82 or 0x89 or 0xC2 or 0xE2 or
                0x04 or 0x44 or 0x64 or 0x07 or 0x27 or 0x47 or 0x67 or 0x87 or 0xA7 or 0xC7 or 0xE7 or
                0x14 or 0x34 or 0x54 or 0x74 or 0xD4 or 0xF4 or 0x17 or 0x37 or 0x57 or 0x77 or 0xD7 or 0xF7 or 0x97 or 0xB7 or 0x93 or
                0x03 or 0x23 or 0x43 or 0x63 or 0x83 or 0xA3 or 0xC3 or 0xE3 or
                0x13 or 0x33 or 0x53 or 0x73 or 0xB3 or 0xD3 or 0xF3 => 2,

                // Absolute / Indirect
                _ => 3
            };
        }

        private string GetDisasmOperand(byte opcode, ushort pc)
        {
            int length = GetOpcodeLength(opcode);
            if (length == 1) return "";
            if (length == 2)
            {
                byte val = Peek((ushort)(pc + 1));
                // Check if it's immediate
                if (opcode == 0xA9 || opcode == 0xA2 || opcode == 0xA0 || opcode == 0x69 || opcode == 0xE9 || opcode == 0xC9 || opcode == 0xE0 || opcode == 0xC0 || opcode == 0x29 || opcode == 0x09 || opcode == 0x49 ||
                    opcode == 0x0B || opcode == 0x2B || opcode == 0x4B || opcode == 0x6B || opcode == 0xAB || opcode == 0xCB || opcode == 0xEB)
                    return $"#${val:X2}";
                // Check if it's relative
                if (opcode == 0x10 || opcode == 0x30 || opcode == 0x50 || opcode == 0x70 || opcode == 0x90 || opcode == 0xB0 || opcode == 0xD0 || opcode == 0xF0)
                    return $"${(ushort)(pc + 2 + (sbyte)val):X4}";

                // Check if it's Zero Page,X
                if (opcode == 0xB5 || opcode == 0xB4 || opcode == 0x95 || opcode == 0x94 || opcode == 0xF6 || opcode == 0xD6 || opcode == 0x16 || opcode == 0x56 || opcode == 0x36 || opcode == 0x76 || opcode == 0x75 || opcode == 0xF5 || opcode == 0x15 || opcode == 0x35 || opcode == 0x55 || opcode == 0xD5 ||
                    opcode == 0x14 || opcode == 0x34 || opcode == 0x54 || opcode == 0x74 || opcode == 0xD4 || opcode == 0xF4 || opcode == 0x17 || opcode == 0x37 || opcode == 0x57 || opcode == 0x77 || opcode == 0xD7 || opcode == 0xF7 || opcode == 0x07 || opcode == 0x27 || opcode == 0x47 || opcode == 0x67 || opcode == 0x87 || opcode == 0xA7 || opcode == 0xC7 || opcode == 0xE7)
                    return $"${val:X2},X";

                // Check if it's Zero Page,Y
                if (opcode == 0xB6 || opcode == 0x96 || opcode == 0x97 || opcode == 0xB7)
                    return $"${val:X2},Y";

                // Check if it's Indirect,X
                if (opcode == 0xA1 || opcode == 0x81 || opcode == 0x61 || opcode == 0xE1 || opcode == 0xC1 || opcode == 0x21 || opcode == 0x01 || opcode == 0x41 || opcode == 0x03 || opcode == 0x23 || opcode == 0x43 || opcode == 0x63 || opcode == 0x83 || opcode == 0xA3 || opcode == 0xC3 || opcode == 0xE3)
                    return $"(${val:X2},X)";

                // Check if it's Indirect,Y
                if (opcode == 0xB1 || opcode == 0x91 || opcode == 0x71 || opcode == 0xF1 || opcode == 0xD1 || opcode == 0x31 || opcode == 0x11 || opcode == 0x51 || opcode == 0x13 || opcode == 0x33 || opcode == 0x53 || opcode == 0x73 || opcode == 0x93 || opcode == 0xB3 || opcode == 0xD3 || opcode == 0xF3)
                    return $"(${val:X2}),Y";

                // Zero page
                return $"${val:X2}";
            }
            ushort lo = Peek((ushort)(pc + 1));
            ushort hi = Peek((ushort)(pc + 2));
            ushort addr = (ushort)((hi << 8) | lo);
            if (opcode == 0x6C) return $"(${addr:X4})";

            // Check if it's Absolute,X
            if (opcode == 0xBD || opcode == 0xBC || opcode == 0x9D || opcode == 0xFE || opcode == 0xDE || opcode == 0x1E || opcode == 0x5E || opcode == 0x3E || opcode == 0x7E || opcode == 0x7D || opcode == 0xFD || opcode == 0x1D || opcode == 0x3D || opcode == 0x5D || opcode == 0xDD ||
                opcode == 0x1C || opcode == 0x3C || opcode == 0x5C || opcode == 0x7C || opcode == 0xDC || opcode == 0xFC || opcode == 0x1F || opcode == 0x3F || opcode == 0x5F || opcode == 0x7F || opcode == 0x9C || opcode == 0xDF || opcode == 0xFF)
                return $"${addr:X4},X";

            // Check if it's Absolute,Y
            if (opcode == 0xB9 || opcode == 0xBE || opcode == 0x99 || opcode == 0x79 || opcode == 0xF9 || opcode == 0x19 || opcode == 0x39 || opcode == 0x59 || opcode == 0xD9 ||
                opcode == 0x1B || opcode == 0x3B || opcode == 0x5B || opcode == 0x7B || opcode == 0x9E || opcode == 0xBF || opcode == 0xDB || opcode == 0xFB || opcode == 0x9F || opcode == 0x9B)
                return $"${addr:X4},Y";

            return $"${addr:X4}";
        }

        private void Execute(byte opcode)
        {
            switch (opcode)
            {
                // --- LDA ---
                case 0xA9: LDA(AddrImmediate()); break;
                case 0xA5: LDA(AddrZeroPage()); break;
                case 0xB5: LDA(AddrZeroPageX()); break;
                case 0xAD: LDA(AddrAbsolute()); break;
                case 0xBD: LDA(AddrAbsoluteX(false)); break;
                case 0xB9: LDA(AddrAbsoluteY(false)); break;
                case 0xA1: LDA(AddrIndirectX()); break;
                case 0xB1: LDA(AddrIndirectY(false)); break;

                // --- LDX ---
                case 0xA2: LDX(AddrImmediate()); break;
                case 0xA6: LDX(AddrZeroPage()); break;
                case 0xB6: LDX(AddrZeroPageY()); break;
                case 0xAE: LDX(AddrAbsolute()); break;
                case 0xBE: LDX(AddrAbsoluteY(false)); break;

                // --- LDY ---
                case 0xA0: LDY(AddrImmediate()); break;
                case 0xA4: LDY(AddrZeroPage()); break;
                case 0xB4: LDY(AddrZeroPageX()); break;
                case 0xAC: LDY(AddrAbsolute()); break;
                case 0xBC: LDY(AddrAbsoluteX(false)); break;

                // --- STA ---
                case 0x85: STA(AddrZeroPage()); break;
                case 0x95: STA(AddrZeroPageX()); break;
                case 0x8D: STA(AddrAbsolute()); break;
                case 0x9D: STA(AddrAbsoluteX(true)); break;
                case 0x99: STA(AddrAbsoluteY(true)); break;
                case 0x81: STA(AddrIndirectX()); break;
                case 0x91: STA(AddrIndirectY(true)); break;

                // --- STX ---
                case 0x86: STX(AddrZeroPage()); break;
                case 0x96: STX(AddrZeroPageY()); break;
                case 0x8E: STX(AddrAbsolute()); break;

                // --- STY ---
                case 0x84: STY(AddrZeroPage()); break;
                case 0x94: STY(AddrZeroPageX()); break;
                case 0x8C: STY(AddrAbsolute()); break;

                // --- Arithmetic ---
                case 0x69: ADC(AddrImmediate()); break;
                case 0x65: ADC(AddrZeroPage()); break;
                case 0x75: ADC(AddrZeroPageX()); break;
                case 0x6D: ADC(AddrAbsolute()); break;
                case 0x7D: ADC(AddrAbsoluteX(false)); break;
                case 0x79: ADC(AddrAbsoluteY(false)); break;
                case 0x61: ADC(AddrIndirectX()); break;
                case 0x71: ADC(AddrIndirectY(false)); break;

                case 0xE9: SBC(AddrImmediate()); break;
                case 0xE5: SBC(AddrZeroPage()); break;
                case 0xF5: SBC(AddrZeroPageX()); break;
                case 0xED: SBC(AddrAbsolute()); break;
                case 0xFD: SBC(AddrAbsoluteX(false)); break;
                case 0xF9: SBC(AddrAbsoluteY(false)); break;
                case 0xE1: SBC(AddrIndirectX()); break;
                case 0xF1: SBC(AddrIndirectY(false)); break;

                // --- Compare ---
                case 0xC9: CMP(AddrImmediate()); break;
                case 0xC5: CMP(AddrZeroPage()); break;
                case 0xD5: CMP(AddrZeroPageX()); break;
                case 0xCD: CMP(AddrAbsolute()); break;
                case 0xDD: CMP(AddrAbsoluteX(false)); break;
                case 0xD9: CMP(AddrAbsoluteY(false)); break;
                case 0xC1: CMP(AddrIndirectX()); break;
                case 0xD1: CMP(AddrIndirectY(false)); break;

                case 0xE0: CPX(AddrImmediate()); break;
                case 0xE4: CPX(AddrZeroPage()); break;
                case 0xEC: CPX(AddrAbsolute()); break;

                case 0xC0: CPY(AddrImmediate()); break;
                case 0xC4: CPY(AddrZeroPage()); break;
                case 0xCC: CPY(AddrAbsolute()); break;

                // --- Logical ---
                case 0x29: AND(AddrImmediate()); break;
                case 0x25: AND(AddrZeroPage()); break;
                case 0x35: AND(AddrZeroPageX()); break;
                case 0x2D: AND(AddrAbsolute()); break;
                case 0x3D: AND(AddrAbsoluteX(false)); break;
                case 0x39: AND(AddrAbsoluteY(false)); break;
                case 0x21: AND(AddrIndirectX()); break;
                case 0x31: AND(AddrIndirectY(false)); break;

                case 0x09: ORA(AddrImmediate()); break;
                case 0x05: ORA(AddrZeroPage()); break;
                case 0x15: ORA(AddrZeroPageX()); break;
                case 0x0D: ORA(AddrAbsolute()); break;
                case 0x1D: ORA(AddrAbsoluteX(false)); break;
                case 0x19: ORA(AddrAbsoluteY(false)); break;
                case 0x01: ORA(AddrIndirectX()); break;
                case 0x11: ORA(AddrIndirectY(false)); break;

                case 0x49: EOR(AddrImmediate()); break;
                case 0x45: EOR(AddrZeroPage()); break;
                case 0x55: EOR(AddrZeroPageX()); break;
                case 0x4D: EOR(AddrAbsolute()); break;
                case 0x5D: EOR(AddrAbsoluteX(false)); break;
                case 0x59: EOR(AddrAbsoluteY(false)); break;
                case 0x41: EOR(AddrIndirectX()); break;
                case 0x51: EOR(AddrIndirectY(false)); break;

                case 0x24: BIT(AddrZeroPage()); break;
                case 0x2C: BIT(AddrAbsolute()); break;

                // --- Branch ---
                case 0x10: Branch(!GetFlag(CpuFlags.N)); break; // BPL
                case 0x30: Branch(GetFlag(CpuFlags.N)); break;  // BMI
                case 0x50: Branch(!GetFlag(CpuFlags.V)); break; // BVC
                case 0x70: Branch(GetFlag(CpuFlags.V)); break;  // BVS
                case 0x90: Branch(!GetFlag(CpuFlags.C)); break; // BCC
                case 0xB0: Branch(GetFlag(CpuFlags.C)); break;  // BCS
                case 0xD0: Branch(!GetFlag(CpuFlags.Z)); break; // BNE
                case 0xF0: Branch(GetFlag(CpuFlags.Z)); break;  // BEQ

                // --- Jumps & Calls ---
                case 0x4C: JMP(AddrAbsolute()); break;
                case 0x6C: JMP(AddrIndirect()); break;
                case 0x20: JSR(); break;
                case 0x60: RTS(); break;
                case 0x40: RTI(); break;

                // --- Stack ---
                case 0x48: PHA(); break;
                case 0x08: PHP(); break;
                case 0x68: PLA(); break;
                case 0x28: PLP(); break;

                // --- Register Transfers ---
                case 0xAA: TAX(); break;
                case 0x8A: TXA(); break;
                case 0xA8: TAY(); break;
                case 0x98: TYA(); break;
                case 0xBA: TSX(); break;
                case 0x9A: TXS(); break;

                // --- Increments & Decrements ---
                case 0xE8: INX(); break;
                case 0xCA: DEX(); break;
                case 0xC8: INY(); break;
                case 0x88: DEY(); break;

                // --- Flag Changes ---
                case 0x18: CLC(); break;
                case 0x38: SEC(); break;
                case 0x58: CLI(); break;
                case 0x78: SEI(); break;
                case 0xB8: CLV(); break;
                case 0xD8: CLD(); break;
                case 0xF8: SED(); break;

                // --- Shifts ---
                case 0x0A: ASL_Acc(); break;
                case 0x06: ASL(AddrZeroPage()); break;
                case 0x16: ASL(AddrZeroPageX()); break;
                case 0x0E: ASL(AddrAbsolute()); break;
                case 0x1E: ASL(AddrAbsoluteX(true)); break;

                case 0x4A: LSR_Acc(); break;
                case 0x46: LSR(AddrZeroPage()); break;
                case 0x56: LSR(AddrZeroPageX()); break;
                case 0x4E: LSR(AddrAbsolute()); break;
                case 0x5E: LSR(AddrAbsoluteX(true)); break;

                case 0x2A: ROL_Acc(); break;
                case 0x26: ROL(AddrZeroPage()); break;
                case 0x36: ROL(AddrZeroPageX()); break;
                case 0x2E: ROL(AddrAbsolute()); break;
                case 0x3E: ROL(AddrAbsoluteX(true)); break;

                case 0x6A: ROR_Acc(); break;
                case 0x66: ROR(AddrZeroPage()); break;
                case 0x76: ROR(AddrZeroPageX()); break;
                case 0x6E: ROR(AddrAbsolute()); break;
                case 0x7E: ROR(AddrAbsoluteX(true)); break;

                // --- INC/DEC ---
                case 0xE6: INC(AddrZeroPage()); break;
                case 0xF6: INC(AddrZeroPageX()); break;
                case 0xEE: INC(AddrAbsolute()); break;
                case 0xFE: INC(AddrAbsoluteX(true)); break;

                case 0xC6: DEC(AddrZeroPage()); break;
                case 0xD6: DEC(AddrZeroPageX()); break;
                case 0xCE: DEC(AddrAbsolute()); break;
                case 0xDE: DEC(AddrAbsoluteX(true)); break;

                // --- BRK ---
                case 0x00: BRK(); break;

                // --- NOP ---
                case 0xEA: NOP(); break;

                // --- Unofficial Opcodes ---
                case 0x0B: case 0x2B: AAC(AddrImmediate()); break;
                case 0x4B: ASR(AddrImmediate()); break;
                case 0x6B: ARR(AddrImmediate()); break;
                case 0xAB: ATX(AddrImmediate()); break; // LAX imm
                case 0xCB: AXS(AddrImmediate()); break;
                case 0xEB: SBC(AddrImmediate()); break;
                case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2: DOP(AddrImmediate()); break;

                // Zero Page Unofficial
                case 0x04: case 0x44: case 0x64: DOP(AddrZeroPage()); break;
                case 0x07: SLO(AddrZeroPage()); break;
                case 0x27: RLA(AddrZeroPage()); break;
                case 0x47: SRE(AddrZeroPage()); break;
                case 0x67: RRA(AddrZeroPage()); break;
                case 0x87: AAX(AddrZeroPage()); break;
                case 0xA7: LAX(AddrZeroPage()); break;
                case 0xC7: DCP(AddrZeroPage()); break;
                case 0xE7: ISC(AddrZeroPage()); break;

                // Zero Page Indexed Unofficial
                case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4: DOP(AddrZeroPageX()); break;
                case 0x17: SLO(AddrZeroPageX()); break;
                case 0x37: RLA(AddrZeroPageX()); break;
                case 0x57: SRE(AddrZeroPageX()); break;
                case 0x77: RRA(AddrZeroPageX()); break;
                case 0xD7: DCP(AddrZeroPageX()); break;
                case 0xF7: ISC(AddrZeroPageX()); break;
                case 0x97: AAX(AddrZeroPageY()); break;
                case 0xB7: LAX(AddrZeroPageY()); break;

                // Indirect Unofficial
                case 0x03: SLO(AddrIndirectX()); break;
                case 0x23: RLA(AddrIndirectX()); break;
                case 0x43: SRE(AddrIndirectX()); break;
                case 0x63: RRA(AddrIndirectX()); break;
                case 0x83: AAX(AddrIndirectX()); break;
                case 0xA3: LAX(AddrIndirectX()); break;
                case 0xC3: DCP(AddrIndirectX()); break;
                case 0xE3: ISC(AddrIndirectX()); break;

                // Indirect Indexed Unofficial
                case 0x13: SLO(AddrIndirectY(true)); break;
                case 0x33: RLA(AddrIndirectY(true)); break;
                case 0x53: SRE(AddrIndirectY(true)); break;
                case 0x73: RRA(AddrIndirectY(true)); break;
                case 0xB3: LAX(AddrIndirectY(false)); break;
                case 0xD3: DCP(AddrIndirectY(true)); break;
                case 0xF3: ISC(AddrIndirectY(true)); break;

                // Absolute Unofficial
                case 0x0C: TOP(AddrAbsolute()); break;
                case 0x0F: SLO(AddrAbsolute()); break;
                case 0x2F: RLA(AddrAbsolute()); break;
                case 0x4F: SRE(AddrAbsolute()); break;
                case 0x6F: RRA(AddrAbsolute()); break;
                case 0x8F: AAX(AddrAbsolute()); break;
                case 0xAF: LAX(AddrAbsolute()); break;
                case 0xCF: DCP(AddrAbsolute()); break;
                case 0xEF: ISC(AddrAbsolute()); break;

                // Absolute Indexed Unofficial
                case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC: TOP(AddrAbsoluteX(false)); break;
                case 0x1F: SLO(AddrAbsoluteX(true)); break;
                case 0x3F: RLA(AddrAbsoluteX(true)); break;
                case 0x5F: SRE(AddrAbsoluteX(true)); break;
                case 0x7F: RRA(AddrAbsoluteX(true)); break;
                case 0x93: SHA_IndY(); break; // SHA (ind),Y
                case 0x9C: SHY(); break; // SYA abs,X
                case 0x9B: SHS(); break; // SHS abs,Y
                case 0x9F: SHA_AbsY(); break; // SHA abs,Y
                case 0xDF: DCP(AddrAbsoluteX(true)); break;
                case 0xFF: ISC(AddrAbsoluteX(true)); break;

                case 0x1B: SLO(AddrAbsoluteY(true)); break;
                case 0x3B: RLA(AddrAbsoluteY(true)); break;
                case 0x5B: SRE(AddrAbsoluteY(true)); break;
                case 0x7B: RRA(AddrAbsoluteY(true)); break;
                case 0x8B: ANE(AddrImmediate()); break;
                case 0x9E: SHX(); break; // SXA abs,Y
                case 0xBF: LAX(AddrAbsoluteY(false)); break;
                case 0xDB: DCP(AddrAbsoluteY(true)); break;
                case 0xFB: ISC(AddrAbsoluteY(true)); break;

                default:
                    // TODO: Implement other opcodes
                    break;
            }
        }

        #region Addressing Modes

        private ushort AddrImmediate() => PC++;

        private ushort AddrZeroPage() => Read(PC++);

        private ushort AddrZeroPageX()
        {
            byte zp = Read(PC++);
            Read(zp); // Dummy read
            return (byte)(zp + X);
        }

        private ushort AddrZeroPageY()
        {
            byte zp = Read(PC++);
            Read(zp); // Dummy read
            return (byte)(zp + Y);
        }

        private ushort AddrAbsolute()
        {
            ushort lo = Read(PC++);
            ushort hi = Read(PC++);
            return (ushort)((hi << 8) | lo);
        }

        private ushort AddrAbsoluteX(bool isWrite)
        {
            ushort lo = Read(PC++);
            ushort hi = Read(PC++);
            ushort addr = (ushort)((hi << 8) | lo);
            ushort target = (ushort)(addr + X);
            if (isWrite || (target & 0xFF00) != (addr & 0xFF00))
            {
                Read((ushort)((hi << 8) | (target & 0x00FF))); // Dummy read
            }
            return target;
        }

        private ushort AddrAbsoluteY(bool isWrite)
        {
            ushort lo = Read(PC++);
            ushort hi = Read(PC++);
            ushort addr = (ushort)((hi << 8) | lo);
            ushort target = (ushort)(addr + Y);
            if (isWrite || (target & 0xFF00) != (addr & 0xFF00))
            {
                Read((ushort)((hi << 8) | (target & 0x00FF))); // Dummy read
            }
            return target;
        }

        private ushort AddrIndirectX()
        {
            byte zp = Read(PC++);
            Read(zp); // Dummy read
            byte lo = Read((byte)(zp + X));
            byte hi = Read((byte)(zp + X + 1));
            return (ushort)((hi << 8) | lo);
        }

        private ushort AddrIndirectY(bool isWrite)
        {
            byte zp = Read(PC++);
            byte lo = Read(zp);
            byte hi = Read((byte)(zp + 1));
            ushort addr = (ushort)((hi << 8) | lo);
            ushort target = (ushort)(addr + Y);
            if (isWrite || (target & 0xFF00) != (addr & 0xFF00))
            {
                Read((ushort)((hi << 8) | (target & 0x00FF))); // Dummy read
            }
            return target;
        }

        private ushort AddrIndirect()
        {
            ushort loPtr = Read(PC++);
            ushort hiPtr = Read(PC++);
            ushort ptr = (ushort)((hiPtr << 8) | loPtr);
            
            byte lo = Read(ptr);
            // The 6502 has a bug where if the pointer is at the end of a page,
            // it wraps around to the beginning of the same page instead of crossing.
            ushort hiAddr = (ushort)((ptr & 0xFF00) | ((ptr + 1) & 0x00FF));
            byte hi = Read(hiAddr);
            
            return (ushort)((hi << 8) | lo);
        }

        #endregion

        #region Instructions

        private void LDA(ushort address)
        {
            A = Read(address);
            UpdateZeroAndNegativeFlags(A);
        }

        private void LDX(ushort address)
        {
            X = Read(address);
            UpdateZeroAndNegativeFlags(X);
        }

        private void LDY(ushort address)
        {
            Y = Read(address);
            UpdateZeroAndNegativeFlags(Y);
        }

        private void STA(ushort address)
        {
            Write(address, A);
        }

        private void STX(ushort address)
        {
            Write(address, X);
        }

        private void STY(ushort address)
        {
            Write(address, Y);
        }

        private void ADC(ushort address)
        {
            byte val = Read(address);
            int sum = A + val + (GetFlag(CpuFlags.C) ? 1 : 0);
            
            SetFlag(CpuFlags.C, sum > 0xFF);
            SetFlag(CpuFlags.V, ((~(A ^ val) & (A ^ sum)) & 0x80) != 0);
            
            A = (byte)sum;
            UpdateZeroAndNegativeFlags(A);
        }

        private void SBC(ushort address)
        {
            byte val = Read(address);
            // SBC is ADC with the value inverted
            byte invertedVal = (byte)(val ^ 0xFF);
            int sum = A + invertedVal + (GetFlag(CpuFlags.C) ? 1 : 0);
            
            SetFlag(CpuFlags.C, sum > 0xFF);
            SetFlag(CpuFlags.V, ((~(A ^ invertedVal) & (A ^ sum)) & 0x80) != 0);
            
            A = (byte)sum;
            UpdateZeroAndNegativeFlags(A);
        }

        private void CMP(ushort address)
        {
            byte val = Read(address);
            int res = A - val;
            SetFlag(CpuFlags.C, A >= val);
            UpdateZeroAndNegativeFlags((byte)res);
        }

        private void CPX(ushort address)
        {
            byte val = Read(address);
            int res = X - val;
            SetFlag(CpuFlags.C, X >= val);
            UpdateZeroAndNegativeFlags((byte)res);
        }

        private void CPY(ushort address)
        {
            byte val = Read(address);
            int res = Y - val;
            SetFlag(CpuFlags.C, Y >= val);
            UpdateZeroAndNegativeFlags((byte)res);
        }

        private void AND(ushort address)
        {
            A &= Read(address);
            UpdateZeroAndNegativeFlags(A);
        }

        private void ORA(ushort address)
        {
            A |= Read(address);
            UpdateZeroAndNegativeFlags(A);
        }

        private void EOR(ushort address)
        {
            A ^= Read(address);
            UpdateZeroAndNegativeFlags(A);
        }

        private void BIT(ushort address)
        {
            byte val = Read(address);
            SetFlag(CpuFlags.Z, (A & val) == 0);
            SetFlag(CpuFlags.N, (val & 0x80) != 0);
            SetFlag(CpuFlags.V, (val & 0x40) != 0);
        }

        private bool IsIrqAsserted()
        {
            return (_bus.Cartridge != null && _bus.Cartridge.IrqActive) ||
                   (_bus.Apu != null && _bus.Apu.IrqActive);
        }

        private void Branch(bool condition)
        {
            sbyte offset = (sbyte)Read(PC++);
            bool irqAfterCycle2 = IsIrqAsserted();

            if (condition)
            {
                Read(PC); // Dummy read
                bool irqAfterCycle3 = IsIrqAsserted();

                ushort oldPC = PC;
                PC = (ushort)(PC + offset);
                if ((PC & 0xFF00) != (oldPC & 0xFF00))
                {
                    Read((ushort)((oldPC & 0xFF00) | (PC & 0x00FF))); // Dummy read
                    // Page crossing (4 cycles). Test C implies latching.
                    _overrideIrq = _irqAfterOpcode || irqAfterCycle2 || irqAfterCycle3;
                }
                else
                {
                    // Taken (3 cycles). Test C implies latching.
                    _overrideIrq = _irqAfterOpcode || irqAfterCycle2 || irqAfterCycle3;
                }
            }
            else
            {
                _overrideIrq = _irqAfterOpcode;
            }
        }

        private void JMP(ushort address)
        {
            PC = address;
        }

        private void JSR()
        {
            ushort lo = Read(PC++);
            Read((ushort)(0x0100 + S)); // Dummy read
            PushStack((byte)((PC >> 8) & 0xFF));
            PushStack((byte)(PC & 0xFF));
            byte hi = Read(PC);
            PC = (ushort)((hi << 8) | lo);
        }

        private void RTS()
        {
            Read(PC); // Dummy read
            Read((ushort)(0x0100 + S)); // Dummy read
            byte lo = PopStack();
            byte hi = PopStack();
            PC = (ushort)((hi << 8) | lo);
            Read(PC++); // Increment PC
        }

        private void RTI()
        {
            Read(PC); // Dummy read
            Read((ushort)(0x0100 + S)); // Dummy read
            P = (byte)((PopStack() & ~((byte)CpuFlags.B)) | (byte)CpuFlags.U);
            byte lo = PopStack();
            byte hi = PopStack();
            PC = (ushort)((hi << 8) | lo);
        }

        private void PHA()
        {
            Read(PC); // Dummy read
            PushStack(A);
        }

        private void PHP()
        {
            Read(PC); // Dummy read
            PushStack((byte)(P | (byte)CpuFlags.B | (byte)CpuFlags.U));
        }

        private void PLA()
        {
            Read(PC); // Dummy read
            Read((ushort)(0x0100 + S)); // Dummy read
            A = PopStack();
            UpdateZeroAndNegativeFlags(A);
        }

        private void PLP()
        {
            Read(PC); // Dummy read
            Read((ushort)(0x0100 + S)); // Dummy read
            P = (byte)((PopStack() & ~((byte)CpuFlags.B)) | (byte)CpuFlags.U);
        }

        private void TAX() { X = A; UpdateZeroAndNegativeFlags(X); Read(PC); }
        private void TXA() { A = X; UpdateZeroAndNegativeFlags(A); Read(PC); }
        private void TAY() { Y = A; UpdateZeroAndNegativeFlags(Y); Read(PC); }
        private void TYA() { A = Y; UpdateZeroAndNegativeFlags(A); Read(PC); }
        private void TSX() { X = S; UpdateZeroAndNegativeFlags(X); Read(PC); }
        private void TXS() { S = X; Read(PC); }

        private void INX() { X++; UpdateZeroAndNegativeFlags(X); Read(PC); }
        private void DEX() { X--; UpdateZeroAndNegativeFlags(X); Read(PC); }
        private void INY() { Y++; UpdateZeroAndNegativeFlags(Y); Read(PC); }
        private void DEY() { Y--; UpdateZeroAndNegativeFlags(Y); Read(PC); }

        private void CLC() { SetFlag(CpuFlags.C, false); Read(PC); }
        private void SEC() { SetFlag(CpuFlags.C, true); Read(PC); }
        private void CLI() { SetFlag(CpuFlags.I, false); Read(PC); }
        private void SEI() { SetFlag(CpuFlags.I, true); Read(PC); }
        private void CLV() { SetFlag(CpuFlags.V, false); Read(PC); }
        private void CLD() { SetFlag(CpuFlags.D, false); Read(PC); }
        private void SED() { SetFlag(CpuFlags.D, true); Read(PC); }

        private void ASL_Acc() { SetFlag(CpuFlags.C, (A & 0x80) != 0); A <<= 1; UpdateZeroAndNegativeFlags(A); Read(PC); }
        private void ASL(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            SetFlag(CpuFlags.C, (val & 0x80) != 0);
            val <<= 1;
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void LSR_Acc() { SetFlag(CpuFlags.C, (A & 0x01) != 0); A >>= 1; UpdateZeroAndNegativeFlags(A); Read(PC); }
        private void LSR(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            SetFlag(CpuFlags.C, (val & 0x01) != 0);
            val >>= 1;
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void ROL_Acc()
        {
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (A & 0x80) != 0);
            A = (byte)((A << 1) | (oldC ? 1 : 0));
            UpdateZeroAndNegativeFlags(A);
            Read(PC);
        }
        private void ROL(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (val & 0x80) != 0);
            val = (byte)((val << 1) | (oldC ? 1 : 0));
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void ROR_Acc()
        {
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (A & 0x01) != 0);
            A = (byte)((A >> 1) | (oldC ? 0x80 : 0));
            UpdateZeroAndNegativeFlags(A);
            Read(PC);
        }
        private void ROR(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (val & 0x01) != 0);
            val = (byte)((val >> 1) | (oldC ? 0x80 : 0));
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void INC(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            val++;
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void DEC(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            val--;
            Write(address, val);
            UpdateZeroAndNegativeFlags(val);
        }

        private void BRK()
        {
            Read(PC++); // Dummy read
            PushStack((byte)((PC >> 8) & 0xFF));
            PushStack((byte)(PC & 0xFF));
            
            ushort vectorAddr = 0xFFFE;
            if (_bus.Ppu != null && _bus.Ppu.TriggerNmi)
            {
                _bus.Ppu.TriggerNmi = false;
                vectorAddr = 0xFFFA;
                PushStack((byte)(P & ~((byte)CpuFlags.B) | (byte)CpuFlags.U));
            }
            else
            {
                PushStack((byte)(P | (byte)CpuFlags.B | (byte)CpuFlags.U));
            }

            SetFlag(CpuFlags.I, true);

            ushort lo = Read(vectorAddr);
            ushort hi = Read((ushort)(vectorAddr + 1));
            PC = (ushort)((hi << 8) | lo);
        }

        public void IRQ()
        {
            if (GetFlag(CpuFlags.I)) return;

            Read(PC); // Dummy read
            Read(PC); // Dummy read
            PushStack((byte)((PC >> 8) & 0xFF));
            PushStack((byte)(PC & 0xFF));
            PushStack((byte)(P & ~((byte)CpuFlags.B)));
            SetFlag(CpuFlags.I, true);
            ushort lo = Read(0xFFFE);
            ushort hi = Read(0xFFFF);
            PC = (ushort)((hi << 8) | lo);
        }

        private void NOP()
        {
            Read(PC); // Dummy read
        }

        private void AAC(ushort address)
        {
            byte value = Read(address);
            A &= value;
            SetFlag(CpuFlags.C, (A & 0x80) != 0);
            UpdateZeroAndNegativeFlags(A);
        }

        private void ASR(ushort address)
        {
            byte value = Read(address);
            A &= value;
            SetFlag(CpuFlags.C, (A & 0x01) != 0);
            A >>= 1;
            UpdateZeroAndNegativeFlags(A);
        }

        private void ANE(ushort address)
        {
            byte value = Read(address);
            const byte temp = 0xEE;
            A = (byte)((A | temp) & X & value);
            UpdateZeroAndNegativeFlags(A);
        }

        private void ARR(ushort address)
        {
            byte value = Read(address);
            A &= value;
            byte result = (byte)((A >> 1) | (GetFlag(CpuFlags.C) ? 0x80 : 0x00));
            A = result;
            UpdateZeroAndNegativeFlags(A);
            SetFlag(CpuFlags.C, (A & 0x40) != 0);
            SetFlag(CpuFlags.V, (((A >> 6) ^ (A >> 5)) & 0x01) != 0);
        }

        private void ATX(ushort address)
        {
            byte value = Read(address);
            // ATX (LXA/LAX imm) is unstable. 
            // For many tests, it behaves like LDA imm + TAX: A = X = value
            A = X = value;
            UpdateZeroAndNegativeFlags(A);
        }

        private void AXS(ushort address)
        {
            byte value = Read(address);
            byte combined = (byte)(A & X);
            int result = combined - value;
            X = (byte)result;
            SetFlag(CpuFlags.C, combined >= value);
            UpdateZeroAndNegativeFlags(X);
        }

        private void SLO(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            SetFlag(CpuFlags.C, (val & 0x80) != 0);
            val <<= 1;
            Write(address, val);
            A |= val;
            UpdateZeroAndNegativeFlags(A);
        }

        private void RLA(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (val & 0x80) != 0);
            val = (byte)((val << 1) | (oldC ? 1 : 0));
            Write(address, val);
            A &= val;
            UpdateZeroAndNegativeFlags(A);
        }

        private void SRE(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            SetFlag(CpuFlags.C, (val & 0x01) != 0);
            val >>= 1;
            Write(address, val);
            A ^= val;
            UpdateZeroAndNegativeFlags(A);
        }

        private void RRA(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            bool oldC = GetFlag(CpuFlags.C);
            SetFlag(CpuFlags.C, (val & 0x01) != 0);
            val = (byte)((val >> 1) | (oldC ? 0x80 : 0));
            Write(address, val);

            // ADC logic
            int temp = A + val + (GetFlag(CpuFlags.C) ? 1 : 0);
            SetFlag(CpuFlags.V, (~(A ^ val) & (A ^ temp) & 0x80) != 0);
            A = (byte)temp;
            SetFlag(CpuFlags.C, temp > 0xFF);
            UpdateZeroAndNegativeFlags(A);
        }

        private void AAX(ushort address)
        {
            Write(address, (byte)(A & X));
        }

        private void LAX(ushort address)
        {
            byte val = Read(address);
            A = X = val;
            UpdateZeroAndNegativeFlags(A);
        }

        private void DCP(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            val--;
            Write(address, val);

            // CMP logic
            SetFlag(CpuFlags.C, A >= val);
            UpdateZeroAndNegativeFlags((byte)(A - val));
        }

        private void ISC(ushort address)
        {
            byte val = Read(address);
            Write(address, val); // Dummy write
            val++;
            Write(address, val);

            // SBC logic
            int sub = val + (GetFlag(CpuFlags.C) ? 0 : 1);
            int temp = A - sub;
            SetFlag(CpuFlags.V, ((A ^ temp) & (A ^ val) & 0x80) != 0);
            SetFlag(CpuFlags.C, temp >= 0);
            A = (byte)temp;
            UpdateZeroAndNegativeFlags(A);
        }

        private void DOP(ushort address)
        {
            Read(address); // Just read the operand
        }

        private void TOP(ushort address)
        {
            Read(address); // Just read the operand
        }

        private void SHX()
        {
            byte lo = Read(PC++);
            long startCyc = TotalCycles;
            byte hi = Read(PC++);
            Read((ushort)((hi << 8) | ((lo + Y) & 0xFF))); // Dummy read
            bool stalled = (TotalCycles - startCyc) > 2;

            ushort target = (ushort)(((hi << 8) | lo) + Y);
            bool crossed = (lo + Y) > 0xFF;

            byte high = (byte)(hi + 1);
            if (stalled) high = 0xFF;

            byte val = (byte)(X & high);
            if (crossed || stalled)
            {
                target = (ushort)((val << 8) | (target & 0xFF));
            }
            Write(target, val);
        }

        private void SHY()
        {
            byte lo = Read(PC++);
            long startCyc = TotalCycles;
            byte hi = Read(PC++);
            Read((ushort)((hi << 8) | ((lo + X) & 0xFF))); // Dummy read
            bool stalled = (TotalCycles - startCyc) > 2;

            ushort target = (ushort)(((hi << 8) | lo) + X);
            bool crossed = (lo + X) > 0xFF;

            byte high = (byte)(hi + 1);
            if (stalled) high = 0xFF;

            byte val = (byte)(Y & high);
            if (crossed || stalled)
            {
                target = (ushort)((val << 8) | (target & 0xFF));
            }
            Write(target, val);
        }

        private void SHA_AbsY()
        {
            byte lo = Read(PC++);
            long startCyc = TotalCycles;
            byte hi = Read(PC++);
            Read((ushort)((hi << 8) | ((lo + Y) & 0xFF))); // Dummy read
            bool stalled = (TotalCycles - startCyc) > 2;

            ushort target = (ushort)(((hi << 8) | lo) + Y);
            bool crossed = (lo + Y) > 0xFF;

            byte high = (byte)(hi + 1);
            if (stalled) high = 0xFF;

            byte val = (byte)(A & X & high);
            if (crossed || stalled)
            {
                target = (ushort)((val << 8) | (target & 0xFF));
            }
            Write(target, val);
        }

        private void SHA_IndY()
        {
            byte zp = Read(PC++);
            byte lo = Read(zp);
            long startCyc = TotalCycles;
            byte hi = Read((byte)(zp + 1));
            Read((ushort)((hi << 8) | ((lo + Y) & 0xFF))); // Dummy read
            bool stalled = (TotalCycles - startCyc) > 2;

            ushort target = (ushort)(((hi << 8) | lo) + Y);
            bool crossed = (lo + Y) > 0xFF;

            byte high = (byte)(hi + 1);
            if (stalled) high = 0xFF;

            byte val = (byte)(A & X & high);
            if (crossed || stalled)
            {
                target = (ushort)((val << 8) | (target & 0xFF));
            }
            Write(target, val);
        }

        private void SHS()
        {
            byte lo = Read(PC++);
            long startCyc = TotalCycles;
            byte hi = Read(PC++);
            Read((ushort)((hi << 8) | ((lo + Y) & 0xFF))); // Dummy read
            bool stalled = (TotalCycles - startCyc) > 2;

            ushort target = (ushort)(((hi << 8) | lo) + Y);
            bool crossed = (lo + Y) > 0xFF;

            S = (byte)(A & X);
            byte high = (byte)(hi + 1);
            if (stalled) high = 0xFF;

            byte val = (byte)(S & high);
            if (crossed || stalled)
            {
                target = (ushort)((val << 8) | (target & 0xFF));
            }
            Write(target, val);
        }

        #endregion

        private void PushStack(byte value)
        {
            Write((ushort)(0x0100 + S--), value);
        }

        private byte PopStack()
        {
            return Read((ushort)(0x0100 + ++S));
        }

        private void UpdateZeroAndNegativeFlags(byte value)
        {
            SetFlag(CpuFlags.Z, value == 0);
            SetFlag(CpuFlags.N, (value & 0x80) != 0);
        }

        public void SetFlag(CpuFlags flag, bool value)
        {
            if (value) P |= (byte)flag;
            else P &= (byte)~flag;
        }

        public bool GetFlag(CpuFlags flag)
        {
            return (P & (byte)flag) != 0;
        }
    }
}
