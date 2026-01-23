namespace MaxonSharp.Codegen;

public class X86Encoder
{
    private readonly List<byte> _code = new();

    public byte[] GetCode() => _code.ToArray();

    // push reg64
    public void Push(Reg reg)
    {
        if ((int)reg >= 8)
        {
            _code.Add(0x41); // REX.B
            _code.Add((byte)(0x50 + ((int)reg - 8)));
        }
        else
        {
            _code.Add((byte)(0x50 + (int)reg));
        }
    }

    // pop reg64
    public void Pop(Reg reg)
    {
        if ((int)reg >= 8)
        {
            _code.Add(0x41); // REX.B
            _code.Add((byte)(0x58 + ((int)reg - 8)));
        }
        else
        {
            _code.Add((byte)(0x58 + (int)reg));
        }
    }

    // mov reg64, reg64
    public void MovRegReg(Reg dest, Reg src)
    {
        byte rex = 0x48; // REX.W
        if ((int)src >= 8) rex |= 0x04; // REX.R
        if ((int)dest >= 8) rex |= 0x01; // REX.B

        _code.Add(rex);
        _code.Add(0x89); // MOV r/m64, r64
        _code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
    }

    // mov reg64, imm64 (or smaller if fits)
    public void MovRegImm(Reg dest, long value)
    {
        // For small values, use mov eax, imm32 (zero-extended)
        if (value >= 0 && value <= uint.MaxValue)
        {
            if ((int)dest >= 8)
            {
                _code.Add(0x41); // REX.B
            }
            _code.Add((byte)(0xB8 + ((int)dest & 7))); // MOV r32, imm32
            EmitInt32((int)value);
        }
        else
        {
            // Full 64-bit move
            byte rex = 0x48; // REX.W
            if ((int)dest >= 8) rex |= 0x01; // REX.B

            _code.Add(rex);
            _code.Add((byte)(0xB8 + ((int)dest & 7))); // MOV r64, imm64
            EmitInt64(value);
        }
    }

    // ret
    public void Ret()
    {
        _code.Add(0xC3);
    }

    private static byte ModRM(int mod, int reg, int rm)
    {
        return (byte)((mod << 6) | ((reg & 7) << 3) | (rm & 7));
    }

    private void EmitInt32(int value)
    {
        _code.Add((byte)(value & 0xFF));
        _code.Add((byte)((value >> 8) & 0xFF));
        _code.Add((byte)((value >> 16) & 0xFF));
        _code.Add((byte)((value >> 24) & 0xFF));
    }

    private void EmitInt64(long value)
    {
        EmitInt32((int)(value & 0xFFFFFFFF));
        EmitInt32((int)((value >> 32) & 0xFFFFFFFF));
    }
}
