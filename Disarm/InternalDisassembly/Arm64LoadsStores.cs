namespace Disarm.InternalDisassembly;

internal static class Arm64LoadsStores
{
    // 表只在类型初始化时按架构拼写解析一次；不依赖 mnemonic enum 的数值排列。
    private static readonly Arm64Mnemonic[,,] OrderedAtomicMnemonics = BuildOrderedAtomicMnemonics();

    private static Arm64Mnemonic[,,] BuildOrderedAtomicMnemonics()
    {
        var operations = new[] { "LDADD", "LDCLR", "LDEOR", "LDSET", "LDSMAX", "LDSMIN", "LDUMAX", "LDUMIN", "SWP", "CAS" };
        var widths = new[] { "", "B", "H" };
        var orders = new[] { "", "A", "AL", "L" };
        var table = new Arm64Mnemonic[operations.Length, widths.Length, orders.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        for (var width = 0; width < widths.Length; width++)
        for (var order = 0; order < orders.Length; order++)
            table[operation, width, order] = (Arm64Mnemonic)Enum.Parse(typeof(Arm64Mnemonic),
                operations[operation] + orders[order] + widths[width]);
        return table;
    }

    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = instruction >> 28; //Bits 28-31
        var op1 = (instruction >> 26) & 1; //Bit 26
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11

        //As can perhaps be imagined, this is by far the most deeply-nested tree of instructions
        //At this level, despite having 5 separate operands to differentiate which path we take, most of these paths are defined by masks, not by values.
        //Unfortunately, this makes the code a bit ugly.

        //They are, at least, *somewhat* grouped by category using op0
        // 同一 op0 还包括 B/H exclusive/CAS；只有 V=1 才是 SIMD structure 访存。
        if ((op0 & 0b1011) == 0 && op1 == 1)
            //Mostly undefined instructions, but a couple of them are defined
            return DisassembleAdvancedLoadStore(instruction);

        if (op0 == 0b1101 && op1 == 0 && op2 >> 1 == 1 && op3 >> 5 == 1)
            //Literally the only concretely defined value for op0, but it still needs others to match conditions - load/store memory tags
            return DisassembleLoadStoreMemoryTags(instruction);

        //Five more categories for op0

        //The last 4 categories look only at the last 2 bits of op0, so we can switch now
        op0 &= 0b11;

        //Ok i lied half of these are barely grouped at all in any way that makes sense to me 
        return op0 switch
        {
            0b00 => DisassembleLoadStoreExclusiveRegOrderedOrCompareSwap(instruction), //load/store exclusive reg, load/store ordered, or compare + swap 
            0b01 => DisassembleLdAprRegisterLiteralOrMemoryCopySet(instruction), //ldapr/stlr unscaled immediate, load register literal, or memory copy/set
            0b10 => DisassembleLoadStorePairs(instruction), //actual group! load/store pairs
            0b11 => DisassembleLoadStoreRegisterOrAtomic(instruction), //various kinds of load/store register, or atomic memory operations
            _ => throw new("Loads/stores: Impossible op0 value")
        };
    }

    private static Arm64Instruction DisassembleAdvancedLoadStore(uint instruction)
    {
        //Most of these are actually unimplemented. Only two categories are defined, and they are both SIMD, so we can shunt over to that class.

        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21

        if (op2 == 0b11)
            //Post-indexed simd load/store structure
            return Arm64Simd.LoadStoreSingleStructurePostIndexed(instruction);

        //Doesn't matter what op2 is at this point, unless the bottom 5 bits of op3 are zeroed, this is unimplemented.
        if ((op3 & 0b1_1111) == 0)
            return Arm64Simd.LoadStoreSingleStructure(instruction);

        throw new Arm64UndefinedInstructionException($"Advanced load/store: Congrats, you hit the minefield of undefined instructions. op2: {op2}, op3: {op3}");
    }

    private static Arm64Instruction DisassembleLoadStoreMemoryTags(uint instruction)
    {
        var opc = (instruction >> 22) & 0b11; // Bits 22-23
        var imm9 = (long) (instruction >> 12) & 0b1_1111_1111; // Bits 12-20
        var op2 = (instruction >> 10) & 0b11; // Bits 10-11
        var rn = (int)(instruction >> 5) & 0b1_1111; // Bits 5-9
        var rt = (int)instruction & 0b1_1111; // Bits 0-5

        imm9 = Arm64CommonUtils.SignExtend(imm9, 9, 64);
        var offset = imm9 << Arm64CommonUtils.LOG2_TAG_GRANULE;
        
        return opc switch
        {
            0b00 when offset != 0 => new()
            {
                Mnemonic = Arm64Mnemonic.STG,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                MemIndexMode = op2 switch
                {
                    0b01 => Arm64MemoryIndexMode.PostIndex,
                    0b10 => Arm64MemoryIndexMode.Offset,
                    0b11 => Arm64MemoryIndexMode.PreIndex,
                    _ => throw new Arm64UndefinedInstructionException("Bad memory index mode")
                },
                MemOffset = offset,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.X0 + rn,
                MemBase = Arm64Register.X0 + rt
            },
            0b00 when offset == 0 => new()
            {
                Mnemonic = Arm64Mnemonic.STZGM,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + rt
            },
            0b01 when op2 == 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.LDG,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + rt
            },
            0b01 when op2 != 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.STZG,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                MemIndexMode = op2 switch
                {
                    0b01 => Arm64MemoryIndexMode.PostIndex,
                    0b10 => Arm64MemoryIndexMode.Offset,
                    0b11 => Arm64MemoryIndexMode.PreIndex,
                    _ => throw new Arm64UndefinedInstructionException("Bad memory index mode")
                },
                MemOffset = offset,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.X0 + rn,
                MemBase = Arm64Register.X0 + rt
            },
            0b10 when offset != 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.ST2G,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                MemIndexMode = op2 switch
                {
                    0b01 => Arm64MemoryIndexMode.PostIndex,
                    0b10 => Arm64MemoryIndexMode.Offset,
                    0b11 => Arm64MemoryIndexMode.PreIndex,
                    _ => throw new Arm64UndefinedInstructionException("Bad memory index mode")
                },
                MemOffset = offset,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.X0 + rn,
                MemBase = Arm64Register.X0 + rt
            },
            0b10 when offset == 0 && op2 == 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.STGM,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + rt
            },
            0b11 when offset != 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.STZ2G,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                MemIndexMode = op2 switch
                {
                    0b01 => Arm64MemoryIndexMode.PostIndex,
                    0b10 => Arm64MemoryIndexMode.Offset,
                    0b11 => Arm64MemoryIndexMode.PreIndex,
                    _ => throw new Arm64UndefinedInstructionException("Bad memory index mode")
                },
                MemOffset = offset,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.X0 + rn,
                MemBase = Arm64Register.X0 + rt
            },
            0b11 when offset == 0 && op2 == 0 =>  new()
            {
                Mnemonic = Arm64Mnemonic.LDGM,
                MnemonicCategory = Arm64MnemonicCategory.MemoryTagging,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn,
                Op1Reg = Arm64Register.X0 + rt
            },
            _ => throw new Arm64UndefinedInstructionException("Unallocated")
        };
    }

    private static Arm64Instruction DisassembleLoadStoreExclusiveRegOrderedOrCompareSwap(uint instruction)
    {
        // BaseLoadStoreExclusive 的 o2/o1 是 bit23/21，不能测试六位 op3 的不存在的 bit6。
        if (instruction.TestBit(26))
            throw new Arm64UndefinedInstructionException("Exclusive/ordered/CAS: V=1 is unallocated");
        var size = instruction >> 30;
        var o2 = instruction.TestBit(23);
        var o1 = instruction.TestBit(21);
        if (o1)
            return !o2 && size >= 2 ? LoadStoreExclusivePair(instruction) : CompareAndSwap(instruction);
        return o2 ? LoadStoreOrdered(instruction) : LoadStoreExclusive(instruction);
    }
    
    private static Arm64Instruction LoadStoreExclusive(uint instruction)
    {
        var size = (int)(instruction >> 30);
        var load = instruction.TestBit(22);
        var ordered = instruction.TestBit(15);
        var rs = (int)((instruction >> 16) & 31);
        // Rs(加载)/Rt2 是未使用的字段；架构将非全一值归为 CONSTRAINED UNPREDICTABLE，
        // 不把这些不参与操作数的位误当成另一个寄存器或未分配指令。
        var mnemonic = (load, ordered, size) switch
        {
            (false, false, 0) => Arm64Mnemonic.STXRB,
            (false, false, 1) => Arm64Mnemonic.STXRH,
            (false, false, _) => Arm64Mnemonic.STXR,
            (false, true, 0) => Arm64Mnemonic.STLXRB,
            (false, true, 1) => Arm64Mnemonic.STLXRH,
            (false, true, _) => Arm64Mnemonic.STLXR,
            (true, false, 0) => Arm64Mnemonic.LDXRB,
            (true, false, 1) => Arm64Mnemonic.LDXRH,
            (true, false, _) => Arm64Mnemonic.LDXR,
            (true, true, 0) => Arm64Mnemonic.LDAXRB,
            (true, true, 1) => Arm64Mnemonic.LDAXRH,
            _ => Arm64Mnemonic.LDAXR
        };
        var data = (size == 3 ? Arm64Register.X0 : Arm64Register.W0) + (int)(instruction & 31);
        // Store-exclusive 的首操作数始终是 W 状态寄存器，不随数据宽度改变。
        return new()
        {
            Mnemonic = mnemonic,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register,
            Op0Reg = load ? data : Arm64Register.W0 + rs,
            Op1Kind = load ? Arm64OperandKind.Memory : Arm64OperandKind.Register,
            Op1Reg = load ? Arm64Register.INVALID : data,
            Op2Kind = load ? Arm64OperandKind.None : Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
        };
    }
    
    private static Arm64Instruction CompareAndSwap(uint instruction)
    {
        var size = (int)(instruction >> 30);
        var pair = !instruction.TestBit(23);
        var rs = (int)((instruction >> 16) & 31);
        var rt = (int)(instruction & 31);
        if (((instruction >> 10) & 31) != 31 || (pair && (size > 1 || ((rs | rt) & 1) != 0)))
            throw new Arm64UndefinedInstructionException("CAS/CASP: invalid fixed field, pair size, or odd pair register");
        var acquire = instruction.TestBit(22);
        var release = instruction.TestBit(15);
        var order = acquire ? release ? 2 : 1 : release ? 3 : 0;
        var registerBase = (pair ? size == 1 : size == 3) ? Arm64Register.X0 : Arm64Register.W0;
        if (pair)
        {
            // CASP 的第二个寄存器是编码隐含的相邻寄存器，必须展开两对，不能丢失一半数据。
            return new()
            {
                Mnemonic = order switch { 0 => Arm64Mnemonic.CASP, 1 => Arm64Mnemonic.CASPA,
                    2 => Arm64Mnemonic.CASPAL, _ => Arm64Mnemonic.CASPL },
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                Op0Kind = Arm64OperandKind.Register, Op0Reg = registerBase + rs,
                Op1Kind = Arm64OperandKind.Register, Op1Reg = registerBase + rs + 1,
                Op2Kind = Arm64OperandKind.Register, Op2Reg = registerBase + rt,
                Op3Kind = Arm64OperandKind.Register, Op3Reg = registerBase + rt + 1,
                Op4Kind = Arm64OperandKind.Memory,
                MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
            };
        }
        return new()
        {
            Mnemonic = OrderedAtomicMnemonics[9, size < 2 ? size + 1 : 0, order],
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register, Op0Reg = registerBase + rs,
            Op1Kind = Arm64OperandKind.Register, Op1Reg = registerBase + rt,
            Op2Kind = Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
        };
    }
    
    private static Arm64Instruction LoadStoreOrdered(uint instruction)
    {
        var size = (int)(instruction >> 30);
        var load = instruction.TestBit(22);
        var ordered = instruction.TestBit(15);
        var mnemonic = (load, ordered, size) switch
        {
            (false, false, 0) => Arm64Mnemonic.STLLRB,
            (false, false, 1) => Arm64Mnemonic.STLLRH,
            (false, false, _) => Arm64Mnemonic.STLLR,
            (false, true, 0) => Arm64Mnemonic.STLRB,
            (false, true, 1) => Arm64Mnemonic.STLRH,
            (false, true, _) => Arm64Mnemonic.STLR,
            (true, false, 0) => Arm64Mnemonic.LDLARB,
            (true, false, 1) => Arm64Mnemonic.LDLARH,
            (true, false, _) => Arm64Mnemonic.LDLAR,
            (true, true, 0) => Arm64Mnemonic.LDARB,
            (true, true, 1) => Arm64Mnemonic.LDARH,
            _ => Arm64Mnemonic.LDAR
        };
        return new()
        {
            Mnemonic = mnemonic,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register,
            Op0Reg = (size == 3 ? Arm64Register.X0 : Arm64Register.W0) + (int)(instruction & 31),
            Op1Kind = Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
        };
    }

    private static Arm64Instruction DisassembleLdAprRegisterLiteralOrMemoryCopySet(uint instruction)
    {
        //LDAPR/STLR, load/store register literal, memory copy, or memory set
        var op1 = instruction.TestBit(26); //Bit 26
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11

        if (!op2.TestBit(1))
            return LoadRegisterLiteral(instruction);
        
        if(op3.TestBit(5))
            throw new Arm64UndefinedInstructionException("LdAprRegisterLiteralOrMemoryCopySet: op3 hi bit set");

        return op4.TestBit(0)
            ? MemoryCopyOrSet(instruction)
            : LdarpOrStlr(instruction);
    }
    
    private static Arm64Instruction LoadRegisterLiteral(uint instruction)
    {
        var opc = (instruction >> 30) & 0b11; // Bits 30-31
        var v = instruction.TestBit(26);
        var imm19 = (instruction >> 5) & 0b111_1111_1111_1111_1111; // Bits 5-23
        imm19 <<= 2; //4-byte aligned
        var label = Arm64CommonUtils.SignExtend(imm19, 21, 64); //21 is 19 + 2 for the left shift
        var rt = (int)instruction & 0b1_1111;
        
        return opc switch
        {
            (0b00 or 0b01) when !v => new()
            {
                Mnemonic = Arm64Mnemonic.LDR,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister, 
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.ImmediatePcRelative,
                Op0Reg = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rt,
                Op1Imm = label
            },
            0b10 when !v =>  new()
            {
                Mnemonic = Arm64Mnemonic.LDRSW,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister, 
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.ImmediatePcRelative,
                Op0Reg = Arm64Register.X0 + rt,
                Op1Imm = label
            },
            0b11 when !v =>  new()
            {
                Mnemonic = Arm64Mnemonic.PRFM,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister, 
                Op0Kind = Arm64OperandKind.Immediate,
                Op1Kind = Arm64OperandKind.ImmediatePcRelative,
                Op0Imm = rt, //TODO Prefetch isn't just a raw imm, it's a combination of well-defined type | cache level | cache policy
                Op1Imm = label
            },
            (0b00 or 0b01 or 0b10) when v => new()
            {
                Mnemonic = Arm64Mnemonic.LDR,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister, 
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.ImmediatePcRelative,
                Op0Reg = (opc == 0b00 ? Arm64Register.S0 : opc == 0b01 ? Arm64Register.D0 : Arm64Register.V0) + rt,
                Op1Imm = label
            },
            _ => throw new Arm64UndefinedInstructionException("Unallocated")
        };
    }
    
    private static Arm64Instruction MemoryCopyOrSet(uint instruction)
    {
        return new()
        {
            Mnemonic = Arm64Mnemonic.UNIMPLEMENTED,
            MnemonicCategory = Arm64MnemonicCategory.Move, 
        };
    }
    
    private static Arm64Instruction LdarpOrStlr(uint instruction)
    {
        // FEAT_LRCPC2 的 imm9 是未缩放的有符号字节偏移；复用整数 sign-load 宽度规则。
        if ((instruction & 0x04200C00) != 0)
            throw new Arm64UndefinedInstructionException("LRCPC2: reserved fixed field");
        return UnscaledIntegerAccess(instruction, true);
    }

    private static Arm64Instruction DisassembleLoadStorePairs(uint instruction)
    {
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24

        return op2 switch
        {
            0b00 => LoadStoreNoAllocatePairs(instruction), //load/store no-allocate pairs
            0b01 => LoadStoreRegisterPair(instruction, Arm64MemoryIndexMode.PostIndex), //load/store register pair (post-indexed)
            0b10 => LoadStoreRegisterPair(instruction, Arm64MemoryIndexMode.Offset), //load/store register pair (offset)
            0b11 => LoadStoreRegisterPair(instruction, Arm64MemoryIndexMode.PreIndex), //load/store register pair (pre-indexed)
            _ => throw new("Loads/store pairs: Impossible op2 value")
        };
    }

    //The 'xx11' category of loads/stores
    private static Arm64Instruction DisassembleLoadStoreRegisterOrAtomic(uint instruction)
    {
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11

        //Bottom bit of op2 is irrelevant
        op2 >>= 1;

        if (op2 == 1)
            //Load/store reg unsigned immediate
            return LoadStoreRegFromImmUnsigned(instruction);

        //Check top bit of op3
        if (op3 >> 5 == 1)
            //Atomic, or load/store reg with non-immediate, depending on op1
            return op4 switch
            {
                0b00 => AtomicMemoryOperation(instruction), //Atomic
                0b10 => LoadStoreRegisterFromRegisterOffset(instruction), //Load/store (reg), (reg + x)
                _ => LoadStoreRegisterFromPac(instruction), //Load store (reg), (pac)
            };

        //Some kind of load/store reg with an immediate
        return op4 switch
        {
            0b00 => LoadStoreRegisterFromImmUnscaled(instruction), //Load/store (reg), (unscaled immediate)
            0b01 => LoadStoreRegisterFromImm(instruction, Arm64MemoryIndexMode.PostIndex), //Load/store (reg), (post-indexed immediate)
            0b10 => LoadStoreRegisterUnprivileged(instruction), //Load/store (reg), (unprivileged)
            0b11 => LoadStoreRegisterFromImm(instruction, Arm64MemoryIndexMode.PreIndex), //Load/Store (reg), (pre-indexed immediate)
            _ => throw new("Impossible op4"),
        };
    }

    private static Arm64Instruction LoadStoreRegisterFromImm(uint instruction, Arm64MemoryIndexMode memoryIndexMode)
    {
        // Load/store immediate pre-indexed

        var size = (instruction >> 30) & 0b11; //Bits 30-31
        var isVector = instruction.TestBit(26); //Bit 26
        var opc = (instruction >> 22) & 0b11; //Bits 22-23
        var imm9 = (instruction >> 12) & 0b1_1111_1111; //Bits 12-20
        var rn = (int)(instruction >> 5) & 0b11111; //Bits 5-9
        var rt = (int)(instruction & 0b11111); //Bits 0-4

        if (size is 0b10 or 0b11)
        {
            var invalid = isVector ? opc is 0b10 or 0b11 : opc is 0b11;
            
            if (invalid)
                throw new Arm64UndefinedInstructionException($"Load/store immediate pre-indexed: Invalid size/opc combination. size: {size}, opc: {opc}");
        }
        
        //Note to self - this logic is copied from further down but has had some minor adjustments made, it may still be incorrect in places
        //so if something seems wrong, it probably is!
        var mnemonic = opc switch
        {
            0b00 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.STRB,
                0b01 when !isVector => Arm64Mnemonic.STRH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.STR,
                _ when isVector => Arm64Mnemonic.STR,
                _ => throw new($"Impossible size: {size}")
            },
            0b01 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRB,
                0b01 when !isVector => Arm64Mnemonic.LDRH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.LDR,
                _ when isVector => Arm64Mnemonic.LDR,
                _ => throw new($"Impossible size: {size}")
            },
            0b10 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRSB, //64-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDRSH, //64-bit variant
                0b10 when !isVector => Arm64Mnemonic.LDRSW,
                0b00 when isVector => Arm64Mnemonic.STR, //128-bit store
                _ => throw new($"Impossible size: {size}")
            },
            0b11 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRSB, //32-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDRSH, //32-bit variant
                0b00 when isVector => Arm64Mnemonic.LDR, //128-bit load
                _ => throw new($"Impossible size: {size}")
            },
            _ => throw new("Impossible opc value")
        };
        
        var baseReg = mnemonic switch
        {
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector && opc is 0 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector && opc is 1 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector => Arm64Register.V0, //128-bit vector
            Arm64Mnemonic.STRB or Arm64Mnemonic.LDRB or Arm64Mnemonic.STRH or Arm64Mnemonic.LDRH => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when size is 0b10 => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR => Arm64Register.X0,
            // signed B/H 的访问宽度不决定目标宽度；opc=10 扩展到 X，opc=11 扩展到 W。
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH when opc is 0b10 => Arm64Register.X0,
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH => Arm64Register.W0,
            Arm64Mnemonic.LDRSW => Arm64Register.X0,
            _ => throw new("Impossible mnemonic")
        };
        
        var regT = baseReg + rt;
        var regN = Arm64Register.X0 + rn;
        
        var offset = Arm64CommonUtils.SignExtend(imm9, 9, 64);

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Memory,
            MemIndexMode = memoryIndexMode,
            Op0Reg = regT,
            MemBase = regN,
            MemOffset = offset,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
        };
    }

    private static Arm64Instruction LoadStoreNoAllocatePairs(uint instruction)
    {
        var opc = (instruction >> 30) & 0b11; // Bits 30-31
        var v = instruction.TestBit(26);
        var l = instruction.TestBit(22);

        var imm7 = (instruction >> 15) & 0b111_1111; // Bits - 15-21
        var rt2 = (int)(instruction >> 10) & 0b1_1111; // Bits - 10-14
        var rn = (int)(instruction >> 5) & 0b1_1111; // Bits - 5-9
        var rt = (int)instruction & 0b1_1111; // Bits - 0-14

        var offset = Arm64CommonUtils.SignExtend(imm7, 7, 64) << (2 + (l ? 1 : 0));
        
        return opc switch
        {
            (0b00 or 0b10) when !v && !l => new()
            {
                Mnemonic = Arm64Mnemonic.STNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rt,
                Op1Reg = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rt2
            },
            (0b00 or 0b10) when !v && l => new()
            {
                Mnemonic = Arm64Mnemonic.LDNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rt,
                Op1Reg = (opc == 0b00 ? Arm64Register.W0 : Arm64Register.X0) + rt2
            },
            0b00 when v && !l => new()
            {
                Mnemonic = Arm64Mnemonic.STNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.S0 + rt,
                Op1Reg = Arm64Register.S0 + rt2
            },
            0b01 when v && !l => new()
            {
                Mnemonic = Arm64Mnemonic.STNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.D0 + rt,
                Op1Reg = Arm64Register.D0 + rt2
            },
            0b10 when v && !l => new()
            {
                Mnemonic = Arm64Mnemonic.STNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.V0 + rt, // aka Q0
                Op1Reg = Arm64Register.V0 + rt2 // aka Q0
            },
            0b00 when v && l => new()
            {
                Mnemonic = Arm64Mnemonic.LDNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.S0 + rt,
                Op1Reg = Arm64Register.S0 + rt2
            },
            0b01 when v && l => new()
            {
                Mnemonic = Arm64Mnemonic.LDNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.D0 + rt,
                Op1Reg = Arm64Register.D0 + rt2
            },
            0b10 when v && l => new()
            {
                Mnemonic = Arm64Mnemonic.LDNP,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MemOffset = offset,
                MemBase = Arm64Register.X0 + rn,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Register,
                Op2Kind = Arm64OperandKind.Memory,
                Op0Reg = Arm64Register.V0 + rt, // aka Q0
                Op1Reg = Arm64Register.V0 + rt2 // aka Q0
            },
            _ => throw new Arm64UndefinedInstructionException("Unallocated")
        };
    }

    private static Arm64Instruction LoadStoreRegisterPair(uint instruction, Arm64MemoryIndexMode mode)
    {
        //Page C4-559

        var opc = (instruction >> 30) & 0b11; //Bits 30-31
        var imm7 = (instruction >> 15) & 0b111_1111; //Bits 15-21
        var rt2 = (int)(instruction >> 10) & 0b1_1111; //Bits 10-14
        var rn = (int)(instruction >> 5) & 0b1_1111; //Bits 5-9
        var rt = (int)(instruction & 0b1_1111); //Bits 0-4

        var isVector = instruction.TestBit(26);
        var isLoad = instruction.TestBit(22);

        //opc: 
        //00 - stp/ldp (32-bit + 32-bit fp)
        //01 - stgp, ldpsw, stp/ldp (64-bit fp)
        //10 - stp/ldp (64-bit + 128-bit fp)
        //11 - reserved

        if (opc == 0b11)
            throw new Arm64UndefinedInstructionException("Load/store register pair (pre-indexed): opc == 0b11");

        var mnemonic = isLoad ? Arm64Mnemonic.LDP : Arm64Mnemonic.STP;

        if (opc == 1 && !isVector)
            mnemonic = isLoad ? Arm64Mnemonic.LDPSW : Arm64Mnemonic.STGP; //Store Allocation taG (64-bit) and Pair/LoaD Pair of registers Signed Ward (32-bit) 

        var destBaseReg = opc switch
        {
            0b00 when isVector => Arm64Register.S0, //32-bit vector
            0b00 => Arm64Register.W0, //32-bit
            0b01 when mnemonic == Arm64Mnemonic.STGP => Arm64Register.W0, //32-bit
            0b01 => Arm64Register.D0, //All other group 1 is 64-bit vector
            0b10 when isVector => Arm64Register.V0, //128-bit vector
            0b10 => Arm64Register.X0, //64-bit
            _ => throw new("Impossible opc value")
        };

        var dataSizeBits = opc switch
        {
            0b00 => 32,
            0b01 when mnemonic == Arm64Mnemonic.STGP => 32,
            0b01 => 64,
            0b10 when isVector => 128,
            0b10 => 64,
            _ => throw new("Impossible opc value")
        };

        var dataSizeBytes = dataSizeBits / 8;

        //The offset must be aligned to the size of the data so is stored in imm7 divided by this factor
        //So we multiply by the size of the data to get the offset
        //It is stored signed.
        var realImm7 = Arm64CommonUtils.CorrectSignBit(imm7, 7);

        var reg1 = destBaseReg + rt;
        var reg2 = destBaseReg + rt2;
        var regN = Arm64Register.X0 + rn;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op2Kind = Arm64OperandKind.Memory,
            Op0Reg = reg1,
            Op1Reg = reg2,
            MemBase = regN,
            MemOffset = realImm7 * dataSizeBytes,
            MemIndexMode = mode,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
        };
    }

    private static Arm64Instruction LoadStoreRegFromImmUnsigned(uint instruction)
    {
        var size = (instruction >> 30) & 0b11; //Bits 30-31
        var isVector = instruction.TestBit(26);
        var opc = (instruction >> 22) & 0b11; //Bits 22-23
        var imm12 = (instruction >> 10) & 0b1111_1111_1111; //Bits 10-21
        var rn = (int)(instruction >> 5) & 0b11111; //Bits 5-9
        var rt = (int)(instruction & 0b11111); //Bits 0-4
        
        //Zero extend imm12 to 64-bit
        var immediate = (long)imm12;
        immediate <<= (int) size; //Shift left by the size... apparently?

        Arm64Register baseReg;
        Arm64Mnemonic mnemonic;
        
        if (isVector)
        {
            //For once, SIMD/FP is the simple path. It's always LDR or STR, and for all but the 128-bit version, the register depends on size
            //Let's get the 128-bit check out first
            if (opc is 0b10 or 0b11)
            {
                //128-bit. Ensure size is 00
                if (size != 0)
                    throw new Arm64UndefinedInstructionException("Load/store register from immediate (unsigned): opc 0b10/0b11 unallocated for size > 0");
                
                mnemonic = opc == 0b10 ? Arm64Mnemonic.STR : Arm64Mnemonic.LDR;
                baseReg = Arm64Register.V0; //128-bit variant
                immediate <<= 4;
            }
            else
            {
                mnemonic = opc == 0b00 ? Arm64Mnemonic.STR : Arm64Mnemonic.LDR;
                baseReg = size switch
                {
                    0b00 => Arm64Register.B0, //8-bit variant
                    0b01 => Arm64Register.H0, //16-bit variant
                    0b10 => Arm64Register.S0, //32-bit variant
                    0b11 => Arm64Register.D0, //64-bit variant
                    _ => throw new("Impossible size")
                };
            }
            
            return new()
            {
                Mnemonic = mnemonic,
                Op0Kind = Arm64OperandKind.Register,
                Op1Kind = Arm64OperandKind.Memory,
                Op0Reg = baseReg + rt,
                MemBase = Arm64Register.X0 + rn,
                MemOffset = immediate,
                MemIndexMode = Arm64MemoryIndexMode.Offset,
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            };
        }
        
        //Now we have to deal with the non-SIMD/FP case.

        //This is considerably less clean than it perhaps could be because they don't stick to patterns and 
        //the mnemonics are different for different sizes.
        //Example - in general, even opc is a store, odd is a load. But opc == 11 && !v && size = 0 => LDRSB. :(
        mnemonic = opc switch
        {
            0b00 => size switch
            {
                0b00 => Arm64Mnemonic.STRB,
                0b01 => Arm64Mnemonic.STRH,
                0b10 or 0b11 => Arm64Mnemonic.STR, //32 or 64-bit
                _ => throw new($"Impossible size: {size}")
            },
            0b01 => size switch
            {
                0b00 => Arm64Mnemonic.LDRB,
                0b01 => Arm64Mnemonic.LDRH,
                0b10 or 0b11 => Arm64Mnemonic.LDR, //32 or 64-bit
                _ => throw new($"Impossible size: {size}")
            },
            0b10 => size switch
            {
                //These all break the rules, they are loads but they are even opc
                0b00 => Arm64Mnemonic.LDRSB, //64-bit variant
                0b01 => Arm64Mnemonic.LDRSH, //64-bit variant
                0b10 => Arm64Mnemonic.LDRSW,
                0b11 => throw new Arm64UndefinedInstructionException("Load/store register from immediate (unsigned): opc 0b10 unallocated for size 0b11"), 
                _ => throw new($"Impossible size: {size}")
            },
            0b11 => size switch
            {
                0b00 => Arm64Mnemonic.LDRSB, //32-bit variant
                0b01 => Arm64Mnemonic.LDRSH, //32-bit variant
                0b10 => Arm64Mnemonic.PRFM, //TODO?
                0b11 => throw new Arm64UndefinedInstructionException("Load/store register from immediate (unsigned): opc 0b11 unallocated for size 0b11"),
                _ => throw new($"Impossible size: {size}")
            },
            _ => throw new("Impossible opc value")
        };

        if (mnemonic == Arm64Mnemonic.PRFM)
            throw new NotImplementedException("If you're seeing this, reach out, because PRFM is not implemented.");

        baseReg = mnemonic switch
        {
            Arm64Mnemonic.STRB or Arm64Mnemonic.LDRB or Arm64Mnemonic.STRH or Arm64Mnemonic.LDRH => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when size is 0b10 => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR => Arm64Register.X0,
            // signed B/H 的访问宽度不决定目标宽度；opc=10 扩展到 X，opc=11 扩展到 W。
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH when opc is 0b10 => Arm64Register.X0,
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH => Arm64Register.W0,
            Arm64Mnemonic.LDRSW => Arm64Register.X0,
            _ => throw new("Impossible mnemonic")
        };

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Memory,
            Op0Reg = baseReg + rt,
            MemBase = Arm64Register.X0 + rn,
            MemOffset = immediate,
            MemIndexMode = Arm64MemoryIndexMode.Offset,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
        };
    }

    private static Arm64Instruction AtomicMemoryOperation(uint instruction)
    {
        var size = (int)(instruction >> 30);
        var acquire = instruction.TestBit(23);
        var release = instruction.TestBit(22);
        var opcode = (int)((instruction >> 12) & 15);
        var rs = (int)((instruction >> 16) & 31);
        if (instruction.TestBit(26))
            throw new Arm64UndefinedInstructionException("Atomic memory operation: V=1 is unallocated");
        if (opcode == 0b1100 && acquire && !release && rs == 31)
        {
            // LDAPR 占用 atomic 编码组，但只有一个结果寄存器，不是三操作数的 RMW。
            return new()
            {
                Mnemonic = size switch { 0 => Arm64Mnemonic.LDAPRB, 1 => Arm64Mnemonic.LDAPRH, _ => Arm64Mnemonic.LDAPR },
                MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = (size == 3 ? Arm64Register.X0 : Arm64Register.W0) + (int)(instruction & 31),
                Op1Kind = Arm64OperandKind.Memory,
                MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
            };
        }
        if (opcode > 8)
            throw new Arm64UndefinedInstructionException("Atomic memory operation: unallocated opcode/LDAPR fields");
        var order = acquire ? release ? 2 : 1 : release ? 3 : 0;
        var registerBase = size == 3 ? Arm64Register.X0 : Arm64Register.W0;
        return new()
        {
            Mnemonic = OrderedAtomicMnemonics[opcode, size < 2 ? size + 1 : 0, order],
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register, Op0Reg = registerBase + rs,
            Op1Kind = Arm64OperandKind.Register, Op1Reg = registerBase + (int)(instruction & 31),
            Op2Kind = Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
        };
    }

    private static Arm64Instruction LoadStoreRegisterFromRegisterOffset(uint instruction)
    {
        var size = (instruction >> 30) & 0b11; //Bits 30-31
        var isVector = instruction.TestBit(26);
        var opc = (instruction >> 22) & 0b11; //Bits 22-23
        var rm = (int)(instruction >> 16) & 0b1_1111; //Bits 16-20
        var option = (instruction >> 13) & 0b111; //Bits 13-15
        var sFlag = instruction.TestBit(12);
        var rn = (int)(instruction >> 5) & 0b1_1111; //Bits 5-9
        var rt = (int)(instruction & 0b1_1111); //Bits 0-4
        
        var mnemonic = opc switch
        {
            0b00 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.STRB,
                0b01 when !isVector => Arm64Mnemonic.STRH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.STR,
                _ when isVector => Arm64Mnemonic.STR,
                _ => throw new($"Impossible size: {size}")
            },
            0b01 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRB,
                0b01 when !isVector => Arm64Mnemonic.LDRH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.LDR,
                _ when isVector => Arm64Mnemonic.LDR,
                _ => throw new($"Impossible size: {size}")
            },
            0b10 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRSB, //64-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDRSH, //64-bit variant
                0b10 when !isVector => Arm64Mnemonic.LDRSW,
                0b11 when !isVector => Arm64Mnemonic.PRFM,
                0b00 when isVector => Arm64Mnemonic.STR, 
                _ when isVector => throw new Arm64UndefinedInstructionException("Load/store register from register offset: opc 0b10 unallocated for vectors when size > 0"),
                _ => throw new($"Impossible size: {size}")
            },
            0b11 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDRSB, //32-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDRSH, //32-bit variant
                0b00 when isVector => Arm64Mnemonic.LDR, //128-bit load
                0b10 or 0b11 => throw new Arm64UndefinedInstructionException("Load/store register from register offset: opc 0b11 unallocated for size 0b1x"),
                _ when isVector => throw new Arm64UndefinedInstructionException("Load/store register from register offset: opc 0b11 unallocated for vectors when size > 0"),
                _ => throw new($"Impossible size: {size}")
            },
            _ => throw new("Impossible opc value")
        };

        var isShiftedRegister = option == 0b011;
        
        if (mnemonic == Arm64Mnemonic.PRFM)
            throw new NotImplementedException("If you're seeing this, reach out, because PRFM is not implemented.");

        var baseReg = mnemonic switch
        {
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector && opc is 0 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector && opc is 1 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when isVector => Arm64Register.V0, //128-bit vector
            Arm64Mnemonic.STRB or Arm64Mnemonic.LDRB or Arm64Mnemonic.STRH or Arm64Mnemonic.LDRH => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR when size is 0b10 => Arm64Register.W0,
            Arm64Mnemonic.STR or Arm64Mnemonic.LDR => Arm64Register.X0,
            // signed B/H 的访问宽度不决定目标宽度；opc=10 扩展到 X，opc=11 扩展到 W。
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH when opc is 0b10 => Arm64Register.X0,
            Arm64Mnemonic.LDRSB or Arm64Mnemonic.LDRSH => Arm64Register.W0,
            Arm64Mnemonic.LDRSW => Arm64Register.X0,
            _ => throw new("Impossible mnemonic")
        };

        var secondReg64Bit = option.TestBit(0);
        var secondRegBase = secondReg64Bit ? Arm64Register.X0 : Arm64Register.W0;
        var extendKind = (Arm64ExtendType)option;
        //Extended register: Mnemonic Wt, [Xn, Xm|Wm, ExtendKind Amount]
        //Shifted register: Mnemonic Wt, [Xn, Xm|Wm, LSL Amount]

        var shiftAmount = 0;
        // 寄存器偏移寻址的 S 位同样适用于 UXTW/SXTW 等扩展形式；
        // 它表示扩展后再按访问宽度缩放，不能只在纯 LSL 形式下保留。
        if (sFlag)
        {
            //Shift set, amount is size-dependent
            shiftAmount = size switch
            {
                0b00 when isVector && opc == 0b11 => 4, //128-bit variant
                0b00 => 0, //8-bit variant, vector or otherwise
                0b01 => 1,
                0b10 => 2,
                0b11 => 3,
                _ => throw new("Impossible size")
            };
        }
        
        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Memory,
            Op0Reg = baseReg + rt,
            MemBase = Arm64Register.X0 + rn,
            MemAddendReg = secondRegBase + rm,
            MemIndexMode = Arm64MemoryIndexMode.Offset,
            MemExtendType = isShiftedRegister ? Arm64ExtendType.NONE : extendKind,
            MemShiftType = isShiftedRegister && shiftAmount != 0 ? Arm64ShiftType.LSL : Arm64ShiftType.NONE,
            MemExtendOrShiftAmount = shiftAmount,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
        };
    }

    private static Arm64Instruction LoadStoreRegisterFromPac(uint instruction)
    {
        if ((instruction & 0xFE200400) != 0xF8200400)
            throw new Arm64UndefinedInstructionException("Authenticated load: reserved fixed field");
        // signed imm10 分散在 S:imm9 中，再按八字节缩放；W 仅选择 offset/pre-index。
        var imm10 = (((instruction >> 22) & 1) << 9) | ((instruction >> 12) & 511);
        return new()
        {
            Mnemonic = instruction.TestBit(23) ? Arm64Mnemonic.LDRAB : Arm64Mnemonic.LDRAA,
            MnemonicCategory = Arm64MnemonicCategory.PointerAuthentication,
            Op0Kind = Arm64OperandKind.Register,
            Op0Reg = Arm64Register.X0 + (int)(instruction & 31),
            Op1Kind = Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31),
            MemOffset = Arm64CommonUtils.SignExtend(imm10, 10, 64) << 3,
            MemIndexMode = instruction.TestBit(11) ? Arm64MemoryIndexMode.PreIndex : Arm64MemoryIndexMode.Offset
        };
    }

    private static Arm64Instruction LoadStoreRegisterFromImmUnscaled(uint instruction)
    {
        var size = (instruction >> 30) & 0b11; //Bits 30-31
        var isVector = instruction.TestBit(26);
        var opc = (instruction >> 22) & 0b11; //Bits 22-23
        var imm9 = (instruction >> 12) & 0b1_1111_1111; //Bits 12-20
        var rn = (int)(instruction >> 5) & 0b1_1111; //Bits 5-9
        var rt = (int)(instruction & 0b1_1111); //Bits 0-4
        
        if(size is 1 or 3 && isVector && opc > 1)
            throw new Arm64UndefinedInstructionException("Load/store register from immediate (unsigned): opc > 1 unallocated for vectors when size > 1");
        
        //Here we go with this dance again...
        var mnemonic = opc switch
        {
            0b00 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.STURB,
                0b01 when !isVector => Arm64Mnemonic.STURH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.STUR,
                _ when isVector => Arm64Mnemonic.STUR,
                _ => throw new($"Impossible size: {size}")
            },
            0b01 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDURB,
                0b01 when !isVector => Arm64Mnemonic.LDURH,
                0b10 or 0b11 when !isVector => Arm64Mnemonic.LDUR,
                _ when isVector => Arm64Mnemonic.LDUR,
                _ => throw new($"Impossible size: {size}")
            },
            0b10 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDURSB, //64-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDURSH, //64-bit variant
                0b10 when !isVector => Arm64Mnemonic.LDURSW,
                0b00 when isVector => Arm64Mnemonic.STUR, //128-bit store
                _ when isVector => throw new Arm64UndefinedInstructionException("Load/store register from immediate (unscaled): opc 0b10 unallocated for vectors when size > 0"),
                _ => throw new($"Impossible size: {size}")
            },
            0b11 => size switch
            {
                0b00 when !isVector => Arm64Mnemonic.LDURSB, //32-bit variant
                0b01 when !isVector => Arm64Mnemonic.LDURSH, //32-bit variant
                0b10 when !isVector => Arm64Mnemonic.PRFUM, //TODO?
                0b00 when isVector => Arm64Mnemonic.LDUR, //128-bit store
                0b11 => throw new Arm64UndefinedInstructionException("Load/store register from immediate (unscaled): opc 0b11 unallocated for size 0b11"),
                _ => throw new($"Impossible size: {size}")
            },
            _ => throw new("Impossible opc value")
        };
        
        if (mnemonic == Arm64Mnemonic.PRFUM)
            throw new NotImplementedException("If you're seeing this, reach out, because PRFUM is not implemented.");
        
        var baseReg = mnemonic switch
        {
            Arm64Mnemonic.STUR or Arm64Mnemonic.LDUR when isVector && opc is 0 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STUR or Arm64Mnemonic.LDUR when isVector && opc is 1 => size switch
            {
                0 => Arm64Register.B0,
                1 => Arm64Register.H0,
                2 => Arm64Register.S0,
                3 => Arm64Register.D0,
                _ => throw new("Impossible size")
            },
            Arm64Mnemonic.STUR or Arm64Mnemonic.LDUR when isVector => Arm64Register.V0, //128-bit vector
            Arm64Mnemonic.STURB or Arm64Mnemonic.LDURB or Arm64Mnemonic.STURH or Arm64Mnemonic.LDURH => Arm64Register.W0,
            Arm64Mnemonic.STUR or Arm64Mnemonic.LDUR when size is 0b10 => Arm64Register.W0,
            Arm64Mnemonic.STUR or Arm64Mnemonic.LDUR => Arm64Register.X0,
            // 未缩放 signed-byte 与 signed-half 共用 opc 的 W/X 扩展规则，偏移计算保持原路径。
            Arm64Mnemonic.LDURSB or Arm64Mnemonic.LDURSH when opc is 0b10 => Arm64Register.X0,
            Arm64Mnemonic.LDURSB or Arm64Mnemonic.LDURSH => Arm64Register.W0,
            Arm64Mnemonic.LDURSW => Arm64Register.X0,
            _ => throw new("Impossible mnemonic")
        };
        
        var regT = baseReg + rt;
        var regN = Arm64Register.X0 + rn;
        
        //Sign extend imm9 to 64-bit
        var immediate = Arm64CommonUtils.SignExtend(imm9, 9, 64);
        
        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Memory,
            Op0Reg = regT,
            MemBase = regN,
            MemOffset = immediate,
            MemIndexMode = Arm64MemoryIndexMode.Offset,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
        };
    }

    private static Arm64Instruction LoadStoreRegisterUnprivileged(uint instruction)
    {
        if (instruction.TestBit(26))
            throw new Arm64UndefinedInstructionException("Unprivileged load/store: vector encoding is unallocated");
        return UnscaledIntegerAccess(instruction, false);
    }

    private static Arm64Instruction UnscaledIntegerAccess(uint instruction, bool rcpc)
    {
        // 两个指令族共享 size/opc 的扩展规则；W/X 目标宽度不能由字节/半字访问宽度代替。
        var size = (int)(instruction >> 30);
        var opc = (instruction >> 22) & 3;
        var mnemonic = (opc, size) switch
        {
            (0, 0) => rcpc ? Arm64Mnemonic.STLURB : Arm64Mnemonic.STTRB,
            (0, 1) => rcpc ? Arm64Mnemonic.STLURH : Arm64Mnemonic.STTRH,
            (0, _) => rcpc ? Arm64Mnemonic.STLUR : Arm64Mnemonic.STTR,
            (1, 0) => rcpc ? Arm64Mnemonic.LDAPURB : Arm64Mnemonic.LDTRB,
            (1, 1) => rcpc ? Arm64Mnemonic.LDAPURH : Arm64Mnemonic.LDTRH,
            (1, _) => rcpc ? Arm64Mnemonic.LDAPUR : Arm64Mnemonic.LDTR,
            (2, 0) or (3, 0) => rcpc ? Arm64Mnemonic.LDAPURSB : Arm64Mnemonic.LDTRSB,
            (2, 1) or (3, 1) => rcpc ? Arm64Mnemonic.LDAPURSH : Arm64Mnemonic.LDTRSH,
            (2, 2) => rcpc ? Arm64Mnemonic.LDAPURSW : Arm64Mnemonic.LDTRSW,
            _ => throw new Arm64UndefinedInstructionException("Unscaled integer access: unallocated size/opc")
        };
        var wide = size == 3 || opc == 2;
        return new()
        {
            Mnemonic = mnemonic,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register,
            Op0Reg = (wide ? Arm64Register.X0 : Arm64Register.W0) + (int)(instruction & 31),
            Op1Kind = Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31),
            MemOffset = Arm64CommonUtils.SignExtend((instruction >> 12) & 511, 9, 64)
        };
    }

    private static Arm64Instruction LoadStoreExclusivePair(uint instruction)
    {
        var size = instruction >> 30;
        var load = instruction.TestBit(22);
        var ordered = instruction.TestBit(15);
        var rs = (int)((instruction >> 16) & 31);
        if (size < 2)
            throw new Arm64UndefinedInstructionException("Exclusive pair: invalid element size");
        var registerBase = size == 3 ? Arm64Register.X0 : Arm64Register.W0;
        var first = registerBase + (int)(instruction & 31);
        var second = registerBase + (int)((instruction >> 10) & 31);
        return new()
        {
            Mnemonic = load ? ordered ? Arm64Mnemonic.LDAXP : Arm64Mnemonic.LDXP
                : ordered ? Arm64Mnemonic.STLXP : Arm64Mnemonic.STXP,
            MnemonicCategory = Arm64MnemonicCategory.MemoryToOrFromRegister,
            Op0Kind = Arm64OperandKind.Register, Op0Reg = load ? first : Arm64Register.W0 + rs,
            Op1Kind = Arm64OperandKind.Register, Op1Reg = load ? second : first,
            Op2Kind = load ? Arm64OperandKind.Memory : Arm64OperandKind.Register,
            Op2Reg = load ? Arm64Register.INVALID : second,
            Op3Kind = load ? Arm64OperandKind.None : Arm64OperandKind.Memory,
            MemBase = Arm64Register.X0 + (int)((instruction >> 5) & 31)
        };
    }
}
