using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

// 固定 HINT 空间别名、PAUTH 隐含 modifier 和 TME 结果寄存器的不同编码契约。
public class SystemCompletionTests : BaseDisarmTest
{
    public SystemCompletionTests(ITestOutputHelper outputHelper) : base(outputHelper) { }

    [Fact]
    public void HintAliasesKeepBranchTargetAndUnknownHintImmediate()
    {
        var bti = DisassembleAndCheckMnemonic(0xD50324DF, Arm64Mnemonic.BTI);
        Assert.Equal(3, bti.Op0Imm);
        Assert.EndsWith("BTI jc", bti.ToString());
        var hint = DisassembleAndCheckMnemonic(0xD5032FFF, Arm64Mnemonic.HINT);
        Assert.Equal(127, hint.Op0Imm);
        DisassembleAndCheckMnemonic(0xD503233F, Arm64Mnemonic.PACIASP);
    }

    [Fact]
    public void PointerAuthenticationZeroModifierDoesNotReadSp()
    {
        var zero = DisassembleAndCheckMnemonic(0xDAC123FD, Arm64Mnemonic.PACIZA);
        Assert.Equal(Arm64Register.X29, zero.Op0Reg);
        Assert.Equal(Arm64OperandKind.None, zero.Op1Kind);
        var sp = DisassembleAndCheckMnemonic(0xDAC103FD, Arm64Mnemonic.PACIA);
        Assert.Equal(Arm64Register.X31, sp.Op1Reg);
    }

    [Theory]
    [InlineData(0xD523307D, "TSTART")]
    [InlineData(0xD523317D, "TTEST")]
    public void TransactionalSystemInstructionDecodesResultRatherThanSysl(uint word, string mnemonic)
    {
        var instruction = Disassembler.DisassembleSingleInstruction(word);
        Assert.Equal(mnemonic, instruction.Mnemonic.ToString());
        Assert.Equal(Arm64Register.X29, instruction.Op0Reg);
        Assert.Equal(Arm64OperandKind.None, instruction.Op1Kind);
    }
}
