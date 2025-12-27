using System;

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
        public byte P;      // Status Register

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
        
        public long TotalCycles => _bus.TotalCycles;

        public Cpu(Memory bus)
        {
            _bus = bus;
        }

        public void Reset()
        {
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

        /// <summary>
        /// Executes a single instruction.
        /// </summary>
        public void Step()
        {
            byte opcode = Read(PC++);
            Execute(opcode);
        }

        /// <summary>
        /// Reads a byte from memory and ticks the bus.
        /// </summary>
        public byte Read(ushort address)
        {
            _bus.Tick();
            return _bus.Read(address);
        }

        /// <summary>
        /// Writes a byte to memory and ticks the bus.
        /// </summary>
        public void Write(ushort address, byte data)
        {
            _bus.Tick();
            _bus.Write(address, data);
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
                case 0xD6: DEC(AddrZeroPageY()); break;
                case 0xCE: DEC(AddrAbsolute()); break;
                case 0xDE: DEC(AddrAbsoluteX(true)); break;

                // --- BRK ---
                case 0x00: BRK(); break;

                // --- NOP ---
                case 0xEA: NOP(); break;

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

        private void Branch(bool condition)
        {
            sbyte offset = (sbyte)Read(PC++);
            if (condition)
            {
                Read(PC); // Dummy read
                ushort oldPC = PC;
                PC = (ushort)(PC + offset);
                if ((PC & 0xFF00) != (oldPC & 0xFF00))
                {
                    Read((ushort)((oldPC & 0xFF00) | (PC & 0x00FF))); // Dummy read
                }
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
            PushStack((byte)(P | (byte)CpuFlags.B));
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
            PushStack((byte)(P | (byte)CpuFlags.B));
            SetFlag(CpuFlags.I, true);
            ushort lo = Read(0xFFFE);
            ushort hi = Read(0xFFFF);
            PC = (ushort)((hi << 8) | lo);
        }

        public void NMI()
        {
            Read(PC); // Dummy read
            Read(PC); // Dummy read
            PushStack((byte)((PC >> 8) & 0xFF));
            PushStack((byte)(PC & 0xFF));
            PushStack((byte)(P & ~((byte)CpuFlags.B)));
            SetFlag(CpuFlags.I, true);
            ushort lo = Read(0xFFFA);
            ushort hi = Read(0xFFFB);
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
