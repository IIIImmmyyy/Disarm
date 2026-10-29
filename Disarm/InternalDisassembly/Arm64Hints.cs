namespace Disarm.InternalDisassembly;

internal static class Arm64Hints
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        // HINT 的 CRm:op2 是完整 imm7。未命名编码仍是合法 HINT，不能误报为未实现。
        var hint = (instruction >> 5) & 127;
        var mnemonic = hint switch
        {
            0 => Arm64Mnemonic.NOP,
            1 => Arm64Mnemonic.YIELD,
            2 => Arm64Mnemonic.WFE,
            3 => Arm64Mnemonic.WFI,
            4 => Arm64Mnemonic.SEV,
            5 => Arm64Mnemonic.SEVL,
            6 => Arm64Mnemonic.DGH,
            7 => Arm64Mnemonic.XPACLRI,
            8 => Arm64Mnemonic.PACIA1716,
            10 => Arm64Mnemonic.PACIB1716,
            12 => Arm64Mnemonic.AUTIA1716,
            14 => Arm64Mnemonic.AUTIB1716,
            16 => Arm64Mnemonic.ESB,
            17 => Arm64Mnemonic.PSB_CSYNC,
            18 => Arm64Mnemonic.TSB_CSYNC,
            20 => Arm64Mnemonic.CSDB,
            24 => Arm64Mnemonic.PACIAZ,
            25 => Arm64Mnemonic.PACIASP,
            26 => Arm64Mnemonic.PACIBZ,
            27 => Arm64Mnemonic.PACIBSP,
            28 => Arm64Mnemonic.AUTIAZ,
            29 => Arm64Mnemonic.AUTIASP,
            30 => Arm64Mnemonic.AUTIBZ,
            31 => Arm64Mnemonic.AUTIBSP,
            32 or 34 or 36 or 38 => Arm64Mnemonic.BTI,
            _ => Arm64Mnemonic.HINT
        };
        // BTI 的两位 target 对应无参数/c/j/jc，由显示层打印关键字。
        var target = (hint >> 1) & 3;
        return new()
        {
            Mnemonic = mnemonic,
            MnemonicCategory = Arm64MnemonicCategory.Hint,
            Op0Kind = mnemonic == Arm64Mnemonic.HINT || (mnemonic == Arm64Mnemonic.BTI && target != 0)
                ? Arm64OperandKind.Immediate : Arm64OperandKind.None,
            Op0Imm = mnemonic == Arm64Mnemonic.BTI ? target : mnemonic == Arm64Mnemonic.HINT ? hint : 0
        };
    }
}
