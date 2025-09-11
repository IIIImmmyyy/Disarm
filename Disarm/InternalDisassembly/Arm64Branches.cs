﻿namespace Disarm.InternalDisassembly;

internal static class Arm64Branches
{
    public static Arm64Instruction ConditionalBranchImmediate(uint instruction)
    {
        if (instruction.TestBit(24))
            throw new Arm64UndefinedInstructionException("Conditional branch (immediate): o1 bit set");

        var imm19 = (instruction >> 5) & 0b111_1111_1111_1111_1111;

        var isConsistent = instruction.TestBit(4);
        var cond = (Arm64ConditionCode)(instruction & 0b1111);

        var imm = Arm64CommonUtils.SignExtend(imm19 << 2, 21, 64);

        return new()
        {
            Mnemonic = isConsistent ? Arm64Mnemonic.BC : Arm64Mnemonic.B,
            MnemonicConditionCode = cond,
            Op0Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Imm = imm,
            MnemonicCategory = Arm64MnemonicCategory.ConditionalBranch,
        };
    }

    public static Arm64Instruction UnconditionalBranchImmediate(uint instruction)
    {
        var comingBack = instruction.TestBit(31);
        var imm26 = instruction & ((1 << 26) - 1);

        imm26 <<= 2; // Multiply by 4 because jump dest has to be aligned anyway

        var relativeJump = Arm64CommonUtils.SignExtend(imm26, 28, 64);
        
        return new()
        {
            Mnemonic = comingBack ? Arm64Mnemonic.BL : Arm64Mnemonic.B,
            Op0Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Imm = relativeJump,
            MnemonicCategory = Arm64MnemonicCategory.Branch,
        };
    }

    public static Arm64Instruction TestAndBranch(uint instruction)
    {
        var isNegated = instruction.TestBit(24);
        var imm14 = (instruction >> 5) & 0b11_1111_1111_1111;
        var rt = (int)(instruction & 0b1_1111);
        var b5 = instruction.TestBit(31);
        var b40 = (instruction >> 19) & 0b1_1111;

        var mnemonic = isNegated ? Arm64Mnemonic.TBNZ : Arm64Mnemonic.TBZ;

        // 计算要测试的位号：b5位决定是32位还是64位，b40是位号的高5位
        var bitToTest = b40;
        if (b5)
            bitToTest |= 32; // 对于64位寄存器，b5=1时位号需要加上32

        var jumpTo = Arm64CommonUtils.CorrectSignBit(imm14, 14) * 4;
        
        // 根据b5位决定使用32位还是64位寄存器
        var baseReg = b5 ? Arm64Register.X0 : Arm64Register.W0;
        var regT = baseReg + rt;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Immediate,
            Op2Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Reg = regT,
            Op1Imm = bitToTest,
            Op2Imm = jumpTo,
            MnemonicCategory = Arm64MnemonicCategory.ConditionalBranch,
        };
    }

    public static Arm64Instruction CompareAndBranch(uint instruction)
    {
        var is64Bit = instruction.TestBit(31); //sf flag
        var isNegated = instruction.TestBit(24);
        var imm19 = (instruction >> 5) & ((1 << 19) - 1);
        var rt = (int)(instruction & 0b1_1111);

        var mnemonic = isNegated ? Arm64Mnemonic.CBNZ : Arm64Mnemonic.CBZ;
        var baseReg = is64Bit ? Arm64Register.X0 : Arm64Register.W0;

        var immediate = Arm64CommonUtils.CorrectSignBit(imm19, 19) * 4;
        var regT = baseReg + rt;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Reg = regT,
            Op1Imm = immediate,
            MnemonicCategory = Arm64MnemonicCategory.ConditionalBranch,
        };
    }

    public static Arm64Instruction UnconditionalBranchRegister(uint instruction)
    {
        //This is by far the most cursed instruction table in the specification. 90% of it is unallocated.
        
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op2 = (instruction >> 16) & 0b1_1111; //Bits 16-20
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        //opc:
        //  0000 - some variant of BR - no modifier variant
        //  0001 - some variant of BL - no modifier variant 
        //  0010 - some variant of RET
        //  0011 - unallocated
        //  0100 - some variant of ERET
        //  0101 - some variant of DRPS
        //  011x - unallocated
        //  1000 - some variant of BR - register modifier variant
        //  1001 - some variant of BL - register modifier variant
        //  11xx - unallocated
        
        if(op2 != 0b11111)
            throw new Arm64UndefinedInstructionException($"Unconditional Branch: op2 != 0b11111: {op2:X}");

        return opc switch
        {
            0b0011 or 0b0110 or 0b0111 or 0b1100 or 0b1101 or 0b1110 or 0b1111 => throw new Arm64UndefinedInstructionException($"Unconditional Branch: Unallocated opc: {opc}"),
            0b0000 or 0b1000 => HandleBrFamily(instruction),
            0b0001 or 0b1001 => HandleBlFamily(instruction),
            0b0010 => HandleRetFamily(instruction),
            0b0100 => HandleEretFamily(instruction),
            0b0101 => HandleDrpsFamily(instruction),
            _ => throw new($"Impossible opc: {opc}")
        };
    }

    private static Arm64Instruction HandleRetFamily(uint instruction)
    {
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4


        switch (op3)
        {
            //ret. but sanity check op4
            case 0:
            {
                if(op4 != 0)
                    throw new Arm64UndefinedInstructionException($"RET with op4 != 0: {op4}");
                
                //By default, ret returns to the caller, the address of which is in X30, however X30 can be overriden by providing a register in rn.
                //As X30 is the default, we don't need to disassemble to it explicitly.
                if (rn == 30)
                    return new() { Mnemonic = Arm64Mnemonic.RET };

                return new()
                {
                    Mnemonic = Arm64Mnemonic.RET,
                    Op0Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn,
                    MnemonicCategory = Arm64MnemonicCategory.Return,
                };
            }
            case 0b000010: //FEAT_PAUTH
                if(rn != 0b11111)
                    throw new Arm64UndefinedInstructionException($"RETAA with rn != 0b11111: {rn}");
                
                if(op4 != 0b11111)
                    throw new Arm64UndefinedInstructionException($"RETAA with op4 != 0b11111: {op4}");
                
                return new()
                {
                    Mnemonic = Arm64Mnemonic.RETAA,
                    MnemonicCategory = Arm64MnemonicCategory.Return, 
                };
            case 0b000011: //FEAT_PAUTH
                if(rn != 0b11111)
                    throw new Arm64UndefinedInstructionException($"RETAB with rn != 0b11111: {rn}");
                
                if(op4 != 0b11111)
                    throw new Arm64UndefinedInstructionException($"RETAB with op4 != 0b11111: {op4}");
                
                return new()
                {
                    Mnemonic = Arm64Mnemonic.RETAB,
                    MnemonicCategory = Arm64MnemonicCategory.Return, 
                };
            default:
                throw new Arm64UndefinedInstructionException("Unallocated");
        }
    }

    private static Arm64Instruction HandleBrFamily(uint instruction)
    {
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4

        if (opc is not 0b0000)
        {
            var isKeyA = op3 == 0b10;
            var isKeyB = op3 == 0b11;
            var m = instruction.TestBit(10); //same as isKeyB
            var rm = (int)op4; //Bits 0-4

            return opc switch
            {
                0b1000 when !m => new()
                {
                    Mnemonic = Arm64Mnemonic.BRAA,
                    MnemonicCategory = Arm64MnemonicCategory.Branch,
                    Op0Kind = Arm64OperandKind.Register,
                    Op1Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn,
                    Op1Reg = Arm64Register.X0 + rm,
                },
                0b1000 when m => new()
                {
                    Mnemonic = Arm64Mnemonic.BRAB,
                    MnemonicCategory = Arm64MnemonicCategory.Branch,
                    Op0Kind = Arm64OperandKind.Register,
                    Op1Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn,
                    Op1Reg = Arm64Register.X0 + rm,
                },
                _ => throw new Arm64UndefinedInstructionException($"BR Family: bad opc {opc},")
            };
        }

        if (op3 is 0 && op4 is 0)
        {
            //Simple BR
            return new()
            {
                Mnemonic = Arm64Mnemonic.BR,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                MnemonicCategory = Arm64MnemonicCategory.Branch,
            };
        }

        if (op3 is 0b000010 && op4 is 0b11111)
        {
            return new()
            {
                Mnemonic = Arm64Mnemonic.BRAAZ,
                MnemonicCategory = Arm64MnemonicCategory.Branch,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
            };
        }

        if (op3 is 0b000011 && op4 is 0b11111)
        {
            return new()
            {
                Mnemonic = Arm64Mnemonic.BRABZ,
                MnemonicCategory = Arm64MnemonicCategory.Branch,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
            };
        }

        throw new Arm64UndefinedInstructionException($"BR Family: op3 {op3}, op4 {op4}");
    }
    
    private static Arm64Instruction HandleBlFamily(uint instruction)
    {
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4

        if (op3 == 0)
        {
            //BLR - branch with link to register
            if(op4 != 0)
                throw new Arm64UndefinedInstructionException("BLR with op4 != 0");

            return new()
            {
                Mnemonic = Arm64Mnemonic.BLR,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn
            };
        }

        if (op3 == 0b000010) // m == 0
        {
            if (op4 == 0b1_1111)
                return new()
                {
                    Mnemonic = Arm64Mnemonic.BLRAAZ,
                    MnemonicCategory = Arm64MnemonicCategory.Branch,
                    Op0Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn,
                };
            return new()
            {
                Mnemonic = Arm64Mnemonic.BLRAA,
                MnemonicCategory = Arm64MnemonicCategory.Branch,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + (int)op4, // rm
            };
        }
        
        if (op3 == 0b000011) // m == 1
        {
            if (op4 == 0b1_1111)
                return new()
                {
                    Mnemonic = Arm64Mnemonic.BLRABZ,
                    MnemonicCategory = Arm64MnemonicCategory.Branch,
                    Op0Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn,
                };
            return new()
            {
                Mnemonic = Arm64Mnemonic.BLRAB,
                MnemonicCategory = Arm64MnemonicCategory.Branch,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + (int)op4, // rm
            };
        }
        
        throw new Arm64UndefinedInstructionException($"BL Family: op3 {op3}, op4 {op4}");
    }
    
    private static Arm64Instruction HandleEretFamily(uint instruction)
    {
        var rn = (instruction >> 5) & 0b11111; // Bits 5-9
        var op2 = (instruction >> 16) & 0b11111; // Bits 16-20
        var op3 = (instruction >> 10) & 0b111111; // Bits 10-15
        var op4 = instruction & 0b1_1111;

        if (op2 != 0b1_1111)
            throw new Arm64UndefinedInstructionException("op2 != 0b1_1111");
        
        return op3 switch
        {
            0b0 when op4 == 0 && rn == 0b1_1111 => new()
            {
                Mnemonic = Arm64Mnemonic.ERET,
                MnemonicCategory = Arm64MnemonicCategory.Return 
            },
            0b10 when op4 == rn && rn == 0b1_1111 => new()
            {
                Mnemonic = Arm64Mnemonic.ERETAA,
                MnemonicCategory = Arm64MnemonicCategory.Return 
            },
            0b11 when op4 == rn && rn == 0b1_1111 => new()
            {
                Mnemonic = Arm64Mnemonic.ERETAB,
                MnemonicCategory = Arm64MnemonicCategory.Return 
            },
            _ => throw new Arm64UndefinedInstructionException("Unallocated")
        };
    }
    
    private static Arm64Instruction HandleDrpsFamily(uint instruction)
    {
        //Debug restore process state. No operands. Shouldn't really be in any real code
        return new()
        {
            Mnemonic = Arm64Mnemonic.DRPS,
            MnemonicCategory = Arm64MnemonicCategory.Unspecified,
        };
    }
}